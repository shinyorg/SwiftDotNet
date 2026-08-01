using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// A loopback TCP server the IDE connects to. Broadcasts frames to every attached client and raises
/// <see cref="CommandReceived"/> for frames coming the other way.
///
/// Three rules this type obeys, because it is attached to a *running app* that must not care:
/// <list type="bullet">
/// <item><b>It never throws into the app.</b> Every socket call is wrapped; a client that dies mid-write
/// is dropped, not propagated. A debugging aid that can crash the thing being debugged is worse than no
/// debugging aid.</item>
/// <item><b>It binds loopback only.</b> This channel hands out the app's entire view tree; it has no
/// business being reachable off the machine.</item>
/// <item><b>It is off unless asked for.</b> See <see cref="DevTools"/> — no environment variable, no
/// listener, no thread.</item>
/// </list>
/// </summary>
public sealed class DevToolsServer : IDisposable
{
    readonly TcpListener _listener;
    readonly List<Client> _clients = new();
    readonly object _gate = new();
    volatile bool _disposed;

    /// <summary>Raised on a background thread when a client sends a frame.</summary>
    public event Action<DevToolsProtocol.Frame>? CommandReceived;

    /// <summary>The port actually bound. Meaningful when 0 was requested and the OS chose one.</summary>
    public int Port { get; }

    /// <summary>Sent as the <c>hello</c> payload to each new client.</summary>
    public string Greeting { get; set; } = "backend=unknown";

    public DevToolsServer(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

        var accept = new Thread(AcceptLoop) { IsBackground = true, Name = "SwiftDotNet DevTools accept" };
        accept.Start();
    }

    /// <summary>Send a frame to every connected client. Cheap and safe when nobody is attached.</summary>
    public void Broadcast(string type, ReadOnlySpan<byte> payload)
    {
        if (_disposed)
            return;

        Client[] snapshot;
        lock (_gate)
        {
            if (_clients.Count == 0)
                return;
            snapshot = _clients.ToArray();
        }

        // The payload is copied once here rather than per client: Span cannot cross the closure, and
        // the frames (a patch, a PNG) are big enough that copying per client would show up.
        var bytes = payload.ToArray();
        foreach (var client in snapshot)
        {
            if (!client.TryWrite(type, bytes))
                Remove(client);
        }
    }

    public void Broadcast(string type, string payload)
        => Broadcast(type, Encoding.UTF8.GetBytes(payload));

    /// <summary>True when at least one IDE is attached — lets callers skip expensive work.</summary>
    public bool HasClients
    {
        get { lock (_gate) return _clients.Count > 0; }
    }

    void AcceptLoop()
    {
        while (!_disposed)
        {
            TcpClient tcp;
            try
            {
                tcp = _listener.AcceptTcpClient();
            }
            catch
            {
                // Disposed, or the listener died. Either way there is nothing useful to do here.
                return;
            }

            // Nagle would coalesce small patch frames into a laggy trickle; this channel is latency-
            // sensitive by definition, since its whole job is to show what the app just did.
            tcp.NoDelay = true;

            var client = new Client(tcp);
            lock (_gate)
                _clients.Add(client);

            client.TryWrite(DevToolsProtocol.ServerFrames.Hello, Encoding.UTF8.GetBytes(Greeting));

            var reader = new Thread(() => ReadLoop(client))
            {
                IsBackground = true,
                Name = "SwiftDotNet DevTools reader",
            };
            reader.Start();
        }
    }

    void ReadLoop(Client client)
    {
        try
        {
            while (!_disposed)
            {
                var frame = DevToolsProtocol.Read(client.Stream);
                if (frame is null)
                    break;
                CommandReceived?.Invoke(frame.Value);
            }
        }
        catch
        {
            // Client went away or sent nonsense. Drop it.
        }
        finally
        {
            Remove(client);
        }
    }

    void Remove(Client client)
    {
        lock (_gate)
            _clients.Remove(client);
        client.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try { _listener.Stop(); } catch { /* already down */ }

        Client[] snapshot;
        lock (_gate)
        {
            snapshot = _clients.ToArray();
            _clients.Clear();
        }
        foreach (var client in snapshot)
            client.Dispose();
    }

    sealed class Client(TcpClient tcp) : IDisposable
    {
        readonly object _writeGate = new();

        public NetworkStream Stream { get; } = tcp.GetStream();

        public bool TryWrite(string type, byte[] payload)
        {
            try
            {
                lock (_writeGate)
                    DevToolsProtocol.Write(Stream, type, payload);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            try { Stream.Dispose(); } catch { /* ignore */ }
            try { tcp.Dispose(); } catch { /* ignore */ }
        }
    }
}
