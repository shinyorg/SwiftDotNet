using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SwiftDotNet.Hosting;

namespace SwiftDotNet.Watch.Live;

/// <summary>Build-time settings for live mode. The SDK generates these into the app's Main.</summary>
public sealed class LiveOptions
{
    /// <summary>Loopback port the watch shell connects to. 0 picks a free port (tests).</summary>
    public int Port { get; init; }
    /// <summary>The built live-shell .app for the watch simulator.</summary>
    public string? AppBundle { get; init; }
    public string? BundleId { get; init; }
    /// <summary>Simulator name or UDID (empty: a booted watch, else the newest one).</summary>
    public string? Simulator { get; init; }
    /// <summary><c>sdk/tools/watch-launch.sh</c>.</summary>
    public string? LaunchScript { get; init; }
    /// <summary>False: don't install/launch the shell, just wait for one (<c>SWIFTDOTNET_WATCH_NOLAUNCH=1</c>, tests).</summary>
    public bool LaunchShell { get; set; } = true;
}

/// <summary>
/// Live mode: the app runs on the Mac under CoreCLR — so any .NET debugger and Hot Reload work — while a
/// thin shell on the watch simulator renders it with the real watchOS SwiftUI. The shell connects over
/// loopback TCP; frames are a 4-byte big-endian length followed by a UTF-8 JSON object:
/// <list type="table">
/// <item><term>watch → Mac</term><description><c>hello</c> {client}, <c>event</c> {id, value},
/// <c>nativeEvent</c> {name, json}, <c>phase</c> {phase}, <c>result</c> {id, result}</description></item>
/// <item><term>Mac → watch</term><description><c>render</c> {patch} (a TreeDiffer patch as a string),
/// <c>call</c> {id, name, args}, <c>reject</c> {reason}</description></item>
/// </list>
/// The protocol carries exactly what the in-process C ABI carries, so the renderer can't tell the difference.
/// </summary>
public static class LiveHost
{
    /// <summary>Entry point used by the SDK-generated Main in live (Debug) builds.</summary>
    public static int Run(Func<SwiftDotNetApp> createApp, LiveOptions options, string[] args)
    {
        ArgumentNullException.ThrowIfNull(createApp);
        ArgumentNullException.ThrowIfNull(options);
        if (args.Contains("--no-launch") || Environment.GetEnvironmentVariable("SWIFTDOTNET_WATCH_NOLAUNCH") is "1" or "true")
            options.LaunchShell = false;

        using var session = new LiveSession(createApp, options);
        try
        {
            session.Listen();
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.Error.WriteLine($"SwiftDotNet watch live mode: port {options.Port} is in use — is another live session of this app " +
                "still running? Stop it, or set a different <WatchLivePort> in the project.");
            return 1;
        }
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            session.Stop();
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => session.Stop();
        if (options.LaunchShell)
            session.LaunchShell();
        session.RunMainLoop();
        return 0;
    }
}

/// <summary>
/// One live session. The thread that calls <see cref="RunMainLoop"/> is the app's "main thread": renders,
/// UI events, native events and <c>await</c> continuations all run there, one at a time, as on the watch.
/// </summary>
sealed class LiveSession(Func<SwiftDotNetApp> createApp, LiveOptions options) : IWatchPlatform, IDisposable
{
    static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);

    readonly BlockingCollection<Action> _work = [];
    readonly ConcurrentDictionary<int, TaskCompletionSource<string?>> _pendingCalls = new();
    readonly object _connectionLock = new();
    TcpListener? _listener;
    Connection? _connection;
    int _nextCallId;
    bool _started;

    public int Port => ((IPEndPoint)_listener!.LocalEndpoint).Port;

    /// <summary>Raised on the main loop after each patch is sent. Tests use it.</summary>
    internal event Action<string>? Rendered;

    public void Listen()
    {
        _listener = new TcpListener(IPAddress.Loopback, options.Port);
        _listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, false);
        _listener.Start();
        new Thread(AcceptLoop) { IsBackground = true, Name = "swiftdotnet-watch-live-accept" }.Start();
        Log(WatchLogLevel.Info, $"Live mode: listening on 127.0.0.1:{Port}. Debugger and Hot Reload are available.");
    }

    public void RunMainLoop()
    {
        foreach (var item in _work.GetConsumingEnumerable())
        {
            try { item(); }
            catch (Exception ex) { Log(WatchLogLevel.Error, ex.ToString()); }
        }
    }

    public void Stop()
    {
        if (!_work.IsAddingCompleted)
            _work.CompleteAdding();
        try { _listener?.Stop(); } catch { }
        CloseConnection(_connection);
    }

    public void Dispose() => Stop();

    void Post(Action action)
    {
        try { _work.Add(action); }
        catch (InvalidOperationException) { } // stopping
    }

    public void LaunchShell()
    {
        if (options.AppBundle is null || options.LaunchScript is null || !Directory.Exists(options.AppBundle))
        {
            Log(WatchLogLevel.Warning, $"Live shell not found at '{options.AppBundle}'. Build the project, then launch it on a watch simulator.");
            return;
        }
        var psi = new ProcessStartInfo("/bin/bash") { UseShellExecute = false };
        psi.ArgumentList.Add(options.LaunchScript);
        psi.ArgumentList.Add("--app");
        psi.ArgumentList.Add(options.AppBundle);
        psi.ArgumentList.Add("--bundle-id");
        psi.ArgumentList.Add(options.BundleId ?? "");
        if (!string.IsNullOrEmpty(options.Simulator))
        {
            psi.ArgumentList.Add("--sim");
            psi.ArgumentList.Add(options.Simulator);
        }
        // install + launch takes a few seconds; the shell connects whenever it's up
        new Thread(() =>
        {
            try
            {
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                if (p.ExitCode != 0)
                    Log(WatchLogLevel.Error, "Could not launch the live shell on the simulator (see output above).");
            }
            catch (Exception ex)
            {
                Log(WatchLogLevel.Error, "Could not launch the live shell: " + ex.Message);
            }
        }) { IsBackground = true, Name = "swiftdotnet-watch-live-launch" }.Start();
    }

    // ---- connection -----------------------------------------------------------------------------

    void AcceptLoop()
    {
        while (true)
        {
            TcpClient client;
            try { client = _listener!.AcceptTcpClient(); }
            catch { return; } // listener stopped
            client.NoDelay = true;
            // becomes the current connection once its hello is accepted (see Handle)
            var conn = new Connection(client);
            new Thread(() => ReadLoop(conn)) { IsBackground = true, Name = "swiftdotnet-watch-live-read" }.Start();
        }
    }

    void ReadLoop(Connection conn)
    {
        try
        {
            while (conn.Read() is { } frame)
                Handle(conn, frame);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or JsonException)
        {
        }
        var wasCurrent = false;
        lock (_connectionLock)
        {
            if (_connection == conn)
            {
                _connection = null;
                wasCurrent = true;
            }
        }
        CloseConnection(conn);
        if (wasCurrent && !_work.IsAddingCompleted)
            Log(WatchLogLevel.Warning, "Watch disconnected. Waiting for it to reconnect…");
    }

    void CloseConnection(Connection? conn)
    {
        if (conn is null)
            return;
        conn.Dispose();
        // anything waiting on this connection gets null rather than hanging until the timeout
        foreach (var (id, tcs) in _pendingCalls)
        {
            if (tcs.Task.AsyncState == conn && _pendingCalls.TryRemove(id, out _))
                tcs.TrySetResult(null);
        }
    }

    void Handle(Connection conn, byte[] frame)
    {
        using var doc = JsonDocument.Parse(frame);
        var msg = doc.RootElement;
        switch (msg.GetProperty("op").GetString())
        {
            case "hello":
                var kind = msg.TryGetProperty("client", out var k) ? k.GetString() : null;
                if (kind != "watch")
                {
                    Send(conn, w =>
                    {
                        w.WriteString("op", "reject");
                        w.WriteString("reason", "This .NET session serves the watch simulator shell.");
                    });
                    CloseConnection(conn);
                    return;
                }
                Connection? previous;
                lock (_connectionLock)
                {
                    previous = _connection;
                    _connection = conn;
                }
                // a second shell (another simulator, or a stale one) takes over; the old one is dropped
                if (previous is not null && previous != conn)
                    CloseConnection(previous);
                Post(() => OnHello(conn));
                break;
            case "event":
                var id = msg.GetProperty("id").GetString()!;
                var value = msg.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                Post(() => { if (_started) WatchHost.DispatchEvent(id, value); });
                break;
            case "nativeEvent":
                var name = msg.GetProperty("name").GetString()!;
                var json = msg.TryGetProperty("json", out var j) && j.ValueKind == JsonValueKind.String ? j.GetString() : null;
                Post(() => { if (_started) WatchHost.RaiseNativeEvent(name, json); });
                break;
            case "phase":
                var phase = (WatchAppPhase)msg.GetProperty("phase").GetInt32();
                Post(() => { if (_started) WatchHost.SetPhase(phase); });
                break;
            case "result":
                // completed on the reader thread: the main loop may be blocked inside Call() waiting for exactly this
                var callId = msg.GetProperty("id").GetInt32();
                var result = msg.TryGetProperty("result", out var res) && res.ValueKind == JsonValueKind.String ? res.GetString() : null;
                if (_pendingCalls.TryRemove(callId, out var tcs))
                    tcs.TrySetResult(result);
                break;
        }
    }

    void OnHello(Connection conn)
    {
        if (conn != _connection)
            return;
        Log(WatchLogLevel.Info, "Watch connected.");
        if (!_started)
        {
            _started = true;
            WatchHost.Start(createApp(), this);   // first render goes out synchronously
        }
        else
        {
            // A (re)connected shell has an empty render store: discard the diff baseline so it gets the whole tree.
            SwiftApp.Invalidate();
            WatchHost.Drain();
        }
    }

    void Send(Connection conn, Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }
        try
        {
            conn.Write(buffer.WrittenSpan);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // the reader notices the disconnect and logs it
        }
    }

    // ---- IWatchPlatform -------------------------------------------------------------------------

    public void Render(string patchJson)
    {
        if (_connection is not { } conn)
            return;
        Send(conn, w =>
        {
            w.WriteString("op", "render");
            w.WriteString("patch", patchJson);
        });
        Rendered?.Invoke(patchJson);
    }

    public void RequestMainThreadDrain() => Post(WatchHost.Drain);

    public void Log(WatchLogLevel level, string message)
    {
        var line = $"[{level}] {message}";
        if (level >= WatchLogLevel.Warning)
            Console.Error.WriteLine(line);
        else
            Console.WriteLine(line);
    }

    public string? Call(string operation, string argsJson)
    {
        var conn = _connection;
        if (conn is null)
        {
            Log(WatchLogLevel.Warning, $"Native call '{operation}' ignored: the watch is not connected.");
            return null;
        }
        var id = Interlocked.Increment(ref _nextCallId);
        var tcs = new TaskCompletionSource<string?>(conn, TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCalls[id] = tcs;
        Send(conn, w =>
        {
            w.WriteString("op", "call");
            w.WriteNumber("id", id);
            w.WriteString("name", operation);
            w.WriteString("args", argsJson);
        });
        if (!tcs.Task.Wait(CallTimeout))
        {
            _pendingCalls.TryRemove(id, out _);
            Log(WatchLogLevel.Warning, $"Native call '{operation}' timed out after {CallTimeout.TotalSeconds:0}s.");
            return null;
        }
        return tcs.Task.Result;
    }

    sealed class Connection(TcpClient client) : IDisposable
    {
        readonly NetworkStream _stream = client.GetStream();
        readonly object _writeLock = new();
        readonly byte[] _header = new byte[4];

        public byte[]? Read()
        {
            if (!ReadExactly(_header))
                return null;
            var length = BinaryPrimitives.ReadInt32BigEndian(_header);
            if (length is < 0 or > 64 * 1024 * 1024)
                throw new IOException($"Bad frame length {length}");
            var frame = new byte[length];
            return ReadExactly(frame) ? frame : null;
        }

        bool ReadExactly(byte[] buffer)
        {
            try
            {
                _stream.ReadExactly(buffer);
                return true;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public void Write(ReadOnlySpan<byte> payload)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
            lock (_writeLock)
            {
                _stream.Write(length);
                _stream.Write(payload);
            }
        }

        public void Dispose() => client.Dispose();
    }
}
