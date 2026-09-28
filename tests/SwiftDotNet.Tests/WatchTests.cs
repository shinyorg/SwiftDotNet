using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using SwiftDotNet.Hosting;
using SwiftDotNet.Watch;
using SwiftDotNet.Watch.Live;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The watchOS target: the <c>digitalCrown</c> wire contract (shared with the Wear OS rotary mapping), the
/// in-process watch host driven through a fake native platform, and the live-mode socket protocol driven
/// by a fake watch shell. The Swift halves were verified on the watchOS simulator — see docs/backends/watchos.md.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public sealed class WatchTests : IDisposable
{
    public void Dispose() => WatchHost.Reset();

    // ---- DigitalCrownRotation wire contract ---------------------------------------------------------

    [Fact]
    public void DigitalCrown_SerializesExactWireContract()
    {
        var node = RenderOnce(new VStack().DigitalCrownRotation(42.5, _ => { }, from: 0, through: 100, by: 0.5));
        var m = node.GetProperty("modifiers").EnumerateArray().Single(x => x.GetProperty("type").GetString() == "digitalCrown");

        Assert.Equal(42.5, m.GetProperty("value").GetDouble());
        Assert.Equal(0, m.GetProperty("from").GetDouble());
        Assert.Equal(100, m.GetProperty("through").GetDouble());
        Assert.Equal(0.5, m.GetProperty("by").GetDouble());
        Assert.Equal("0$0", m.GetProperty("event").GetString());
    }

    [Fact]
    public void DigitalCrown_DefaultsToUnitRangeContinuous()
    {
        var node = RenderOnce(new VStack().DigitalCrownRotation(0.25, _ => { }));
        var m = node.GetProperty("modifiers").EnumerateArray().Single();
        Assert.Equal(0, m.GetProperty("from").GetDouble());
        Assert.Equal(1, m.GetProperty("through").GetDouble());
        Assert.Equal(0, m.GetProperty("by").GetDouble());
    }

    [Fact]
    public void DigitalCrown_EventParsesInvariantDoubleAndIgnoresGarbage()
    {
        var got = new List<double>();
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new Host(new VStack().DigitalCrownRotation(0, got.Add, 0, 100)), platform);

        WatchHost.DispatchEvent("0$0", "12.5");
        WatchHost.DispatchEvent("0$0", "1e1");
        WatchHost.DispatchEvent("0$0", "not a number");

        Assert.Equal([12.5, 10.0], got);
    }

    // ---- in-process host --------------------------------------------------------------------------

    [Fact]
    public void Start_SendsFullTreeToTheNativeRenderer()
    {
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new CounterView(), platform);

        var op = FirstOp(platform.Patches.Single());
        Assert.Equal("replace", op.GetProperty("op").GetString());
        Assert.True(WatchHost.IsRunning);
    }

    [Fact]
    public void UiEvent_RendersImmediatelyWithoutWaitingForADrain()
    {
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new CounterView(), platform);
        var button = FindByType(FirstOp(platform.Patches[0]).GetProperty("node"), "Button");

        WatchHost.DispatchEvent(button.GetProperty("id").GetString()!, null);

        // The handler's State write posts a render; DispatchEvent drains it inline so the tap's result is
        // on screen in the same turn (no frame of stale state under controlled controls).
        Assert.Equal(2, platform.Patches.Count);
        Assert.Contains("\"1\"", platform.Patches[1]);
    }

    [Fact]
    public void BackgroundStateWrite_PostsToTheMainThreadAndAsksForADrain()
    {
        var platform = new FakeWatchPlatform();
        var view = new CounterView();
        WatchHost.Start(view, platform);

        var worker = new Thread(() => view.Count.Value = 7);
        worker.Start();
        worker.Join();

        Assert.Single(platform.Patches);           // nothing rendered off the main thread
        Assert.Equal(1, platform.DrainRequests);   // the host was asked to call swiftdotnet_watch_drain
        WatchHost.Drain();
        Assert.Equal(2, platform.Patches.Count);
        Assert.Contains("\"7\"", platform.Patches[1]);
    }

    [Fact]
    public void RootOnCreated_CanAlreadyCallNativeServices()
    {
        var platform = new FakeWatchPlatform();
        platform.Services["prefs.get"] = _ => "\"5\"";
        var app = BuildApp(new PersistedView());

        WatchHost.Start(app, platform);

        Assert.Contains("\"5\"", platform.Patches[0]);
    }

    [Fact]
    public void Preferences_RoundTripThroughNativeCalls()
    {
        var platform = new FakeWatchPlatform();
        var store = new Dictionary<string, string>();
        platform.Services["prefs.set"] = a => { var d = JsonDocument.Parse(a).RootElement; store[d.GetProperty("key").GetString()!] = d.GetProperty("value").GetString()!; return null; };
        platform.Services["prefs.get"] = a => store.TryGetValue(JsonDocument.Parse(a).RootElement.GetProperty("key").GetString()!, out var v) ? JsonSerializer.Serialize(v) : null;
        WatchHost.Start(new CounterView(), platform);

        Preferences.Set("n", 3);
        Assert.Equal(3, Preferences.Get("n", 0));
        Assert.Equal(9, Preferences.Get("missing", 9));
    }

    [Fact]
    public void NativeEvent_ReachesSubscribersAndPhaseChangesAreReported()
    {
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new CounterView(), platform);
        string? got = null;
        WatchAppPhase? phase = null;
        using var sub = WatchHost.OnNativeEvent("phone.message", e => got = e.GetProperty("hi").GetString());
        WatchHost.PhaseChanged += p => phase = p;

        WatchHost.RaiseNativeEvent("phone.message", "{\"hi\":\"there\"}");
        WatchHost.SetPhase(WatchAppPhase.Background);

        Assert.Equal("there", got);
        Assert.Equal(WatchAppPhase.Background, phase);
    }

    [Fact]
    public async Task WatchHttp_MapsTheUrlSessionResponse()
    {
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new CounterView(), platform);
        // Start installed the watch's main-thread pump on this (test) thread; nothing drains it here, so
        // this test's own awaits must not resume through it.
        SynchronizationContext.SetSynchronizationContext(null);
        platform.Services["http.send"] = a =>
        {
            var id = JsonDocument.Parse(a).RootElement.GetProperty("id").GetString();
            var body = Convert.ToBase64String(Encoding.UTF8.GetBytes("pong"));
            // URLSession completes later, on its own queue; the host raises http.response.
            ThreadPool.QueueUserWorkItem(_ => WatchHost.RaiseNativeEvent("http.response",
                $"{{\"id\":\"{id}\",\"status\":201,\"headers\":{{\"X-Test\":\"1\"}},\"body\":\"{body}\"}}"));
            return null;
        };

        using var client = WatchHttp.CreateClient();
        using var response = await client.GetAsync("https://example.test/ping");

        Assert.Equal(201, (int)response.StatusCode);
        Assert.Equal("pong", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.Contains("X-Test"));
    }

    // ---- live mode (socket protocol) --------------------------------------------------------------

    [Fact]
    public void LiveMode_RendersDispatchesProxiesCallsAndResendsOnReconnect()
    {
        var session = new LiveSession(() => BuildApp(new LiveView()), new LiveOptions { Port = 0, LaunchShell = false });
        session.Listen();
        var loop = new Thread(session.RunMainLoop) { IsBackground = true };
        loop.Start();
        try
        {
            using (var shell = new FakeShell(session.Port))
            {
                shell.Send(new { op = "hello", client = "watch" });
                var first = shell.ReadPatch();
                Assert.Equal("replace", FirstOp(first).GetProperty("op").GetString());
                var root = FirstOp(first).GetProperty("node");
                var buttons = FindAll(root, "Button").Select(b => b.GetProperty("id").GetString()!).ToArray();

                // a tap: event in, patch out
                shell.Send(new { op = "event", id = buttons[0], value = (string?)null });
                Assert.Contains("\"1\"", shell.ReadPatch());

                // a native call made by the app is answered by the shell while the main loop waits
                shell.Send(new { op = "event", id = buttons[1], value = (string?)null });
                var call = shell.Read();
                Assert.Equal("call", call.GetProperty("op").GetString());
                Assert.Equal("prefs.get", call.GetProperty("name").GetString());
                shell.Send(new { op = "result", id = call.GetProperty("id").GetInt32(), result = "\"from-watch\"" });
                Assert.Contains("from-watch", shell.ReadPatch());
            }

            // a relaunched shell has an empty render store, so it must get the whole tree again
            using (var shell = new FakeShell(session.Port))
            {
                shell.Send(new { op = "hello", client = "watch" });
                var again = shell.ReadPatch();
                Assert.Equal("replace", FirstOp(again).GetProperty("op").GetString());
                Assert.Contains("from-watch", again);
            }
        }
        finally
        {
            session.Stop();
            loop.Join(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public void LiveMode_TurnsAwayANonWatchClient()
    {
        var session = new LiveSession(() => BuildApp(new LiveView()), new LiveOptions { Port = 0, LaunchShell = false });
        session.Listen();
        try
        {
            using var shell = new FakeShell(session.Port);
            shell.Send(new { op = "hello", client = "preview" });
            Assert.Equal("reject", shell.Read().GetProperty("op").GetString());
        }
        finally
        {
            session.Stop();
        }
    }

    // ---- helpers ----------------------------------------------------------------------------------

    static SwiftDotNetApp BuildApp(View root)
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(_ => root);
        return builder.Build();
    }

    static JsonElement RenderOnce(View view)
    {
        var platform = new FakeWatchPlatform();
        WatchHost.Start(new Host(view), platform);
        return FirstOp(platform.Patches[0]).GetProperty("node");
    }

    static JsonElement FirstOp(string patch) =>
        JsonDocument.Parse(patch).RootElement.GetProperty("ops").EnumerateArray().First().Clone();

    static JsonElement FindByType(JsonElement node, string type) => FindAll(node, type).First();

    static IEnumerable<JsonElement> FindAll(JsonElement node, string type)
    {
        if (node.GetProperty("type").GetString() == type)
            yield return node;
        foreach (var c in node.GetProperty("children").EnumerateArray())
            foreach (var hit in FindAll(c, type))
                yield return hit;
    }

    sealed class Host(View child) : View
    {
        public override View Body => child;
    }

    sealed class CounterView : View
    {
        public readonly State<int> Count = State(0);
        public override View Body => new VStack(new Text($"{Count.Value}"), new Button("+", () => Count.Value++));
    }

    sealed class PersistedView : View
    {
        readonly State<string> _value = State("");
        protected override void OnCreated() => _value.Value = Preferences.Get("k") ?? "none";
        public override View Body => new Text(_value.Value);
    }

    sealed class LiveView : View
    {
        readonly State<int> _count = State(0);
        readonly State<string> _pref = State("");
        public override View Body => new VStack(
            new Text($"{_count.Value}"),
            new Text(_pref.Value),
            new Button("+", () => _count.Value++),
            new Button("load", () => _pref.Value = Preferences.Get("k") ?? "null"));
    }

    /// <summary>In-memory stand-in for the Swift host's registered callbacks.</summary>
    sealed class FakeWatchPlatform : IWatchPlatform
    {
        public List<string> Patches { get; } = [];
        public int DrainRequests { get; private set; }
        public Dictionary<string, Func<string, string?>> Services { get; } = new();

        public void Render(string patchJson) => Patches.Add(patchJson);
        public void RequestMainThreadDrain() => DrainRequests++;
        public void Log(WatchLogLevel level, string message) { }
        public string? Call(string operation, string argsJson) =>
            Services.TryGetValue(operation, out var f) ? f(argsJson) : null;
    }

    /// <summary>Speaks the live protocol from the watch side, as LiveConnection.swift does.</summary>
    sealed class FakeShell : IDisposable
    {
        readonly TcpClient _client = new();
        readonly NetworkStream _stream;

        public FakeShell(int port)
        {
            _client.Connect("127.0.0.1", port);
            _client.ReceiveTimeout = 10_000;
            _stream = _client.GetStream();
        }

        public void Send(object message)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(message);
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
            _stream.Write(header);
            _stream.Write(body);
        }

        public JsonElement Read()
        {
            var header = new byte[4];
            _stream.ReadExactly(header);
            var body = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
            _stream.ReadExactly(body);
            return JsonDocument.Parse(body).RootElement.Clone();
        }

        public string ReadPatch()
        {
            var msg = Read();
            Assert.Equal("render", msg.GetProperty("op").GetString());
            return msg.GetProperty("patch").GetString()!;
        }

        public void Dispose() => _client.Dispose();
    }
}
