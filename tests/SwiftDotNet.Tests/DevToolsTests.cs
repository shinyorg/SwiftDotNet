using System.Net.Sockets;
using System.Text;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The dev-tools channel the IDE tooling reads: the framing, the server, and the
/// <see cref="PatchTapBridge"/> decorator that exposes the patch stream without Core knowing.
///
/// Two properties matter more than the rest and are asserted directly, because getting them wrong turns
/// a debugging aid into a liability:
/// <list type="bullet">
/// <item>the tap hands the patch to the <b>real bridge first</b> — a dev tool must never sit between the
/// app and the screen;</item>
/// <item>with no environment variable set, <c>Wrap</c> returns the <b>same instance</b> it was given, so
/// leaving the call in a host's startup path costs nothing in a normal run.</item>
/// </list>
/// </summary>
public class DevToolsTests
{
    // ---- framing -------------------------------------------------------------------------------

    [Fact]
    public void Protocol_RoundTripsATextFrame()
    {
        var stream = new MemoryStream();
        DevToolsProtocol.Write(stream, DevToolsProtocol.ServerFrames.Patch, """{"op":"replace"}""");
        stream.Position = 0;

        var frame = DevToolsProtocol.Read(stream);

        Assert.NotNull(frame);
        Assert.Equal("patch", frame!.Value.Type);
        Assert.Equal("""{"op":"replace"}""", frame.Value.Text);
    }

    [Fact]
    public void Protocol_RoundTripsBinaryPayloadContainingNewlines()
    {
        // The reason the framing is length-prefixed rather than line-delimited: PNG frames from the
        // preview host are full of 0x0A. A newline-delimited protocol would truncate this payload.
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x0A };
        var stream = new MemoryStream();
        DevToolsProtocol.Write(stream, DevToolsProtocol.ServerFrames.Frame, png);
        stream.Position = 0;

        var frame = DevToolsProtocol.Read(stream);

        Assert.NotNull(frame);
        Assert.Equal("frame", frame!.Value.Type);
        Assert.Equal(png, frame.Value.Payload);
    }

    [Fact]
    public void Protocol_ReadsFramesBackToBackWithoutLosingAlignment()
    {
        var stream = new MemoryStream();
        DevToolsProtocol.Write(stream, "hello", "backend=skia");
        DevToolsProtocol.Write(stream, "patch", "one");
        DevToolsProtocol.Write(stream, "patch", "two");
        stream.Position = 0;

        Assert.Equal("backend=skia", DevToolsProtocol.Read(stream)!.Value.Text);
        Assert.Equal("one", DevToolsProtocol.Read(stream)!.Value.Text);
        Assert.Equal("two", DevToolsProtocol.Read(stream)!.Value.Text);
        Assert.Null(DevToolsProtocol.Read(stream));      // clean end of stream, not an exception
    }

    [Fact]
    public void Protocol_EmptyPayloadIsAValidFrame()
    {
        var stream = new MemoryStream();
        DevToolsProtocol.Write(stream, DevToolsProtocol.ClientFrames.Reload, "");
        stream.Position = 0;

        var frame = DevToolsProtocol.Read(stream);

        Assert.Equal("reload", frame!.Value.Type);
        Assert.Empty(frame.Value.Payload);
    }

    [Fact]
    public void Protocol_RejectsAForeignHeader()
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes("GET / HTTP/1.1\n"));

        Assert.Throws<InvalidDataException>(() => DevToolsProtocol.Read(stream));
    }

    // ---- the tap -------------------------------------------------------------------------------

    [Fact]
    public void PatchTap_RendersToTheRealBridgeFirst()
    {
        using var server = new DevToolsServer(0);
        var inner = new RecordingBridge();
        var tap = new PatchTapBridge(inner, server);

        tap.Render("""{"op":"replace","id":"0"}""");

        Assert.Equal(["""{"op":"replace","id":"0"}"""], inner.Renders);
        Assert.Equal("""{"op":"replace","id":"0"}""", tap.LastPatch);
    }

    [Fact]
    public void PatchTap_PassesEventsThroughToTheApp()
    {
        using var server = new DevToolsServer(0);
        var inner = new RecordingBridge();
        var tap = new PatchTapBridge(inner, server);

        (string Id, string? Value)? seen = null;
        tap.SetEventHandler((id, value) => seen = (id, value));
        inner.RaiseEvent("0.1", "hello");

        Assert.Equal(("0.1", "hello"), seen);
    }

    // ---- the server ----------------------------------------------------------------------------

    [Fact]
    public void Server_GreetsAClientAndBroadcastsToIt()
    {
        using var server = new DevToolsServer(0) { Greeting = "backend=test;protocol=1" };
        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();

        var hello = DevToolsProtocol.Read(stream);
        Assert.Equal("hello", hello!.Value.Type);
        Assert.Equal("backend=test;protocol=1", hello.Value.Text);

        WaitUntil(() => server.HasClients);
        server.Broadcast(DevToolsProtocol.ServerFrames.Log, "hi");

        var log = DevToolsProtocol.Read(stream);
        Assert.Equal("log", log!.Value.Type);
        Assert.Equal("hi", log.Value.Text);
    }

    [Fact]
    public void Server_RaisesCommandsSentByAClient()
    {
        using var server = new DevToolsServer(0);
        using var received = new ManualResetEventSlim(false);
        DevToolsProtocol.Frame? command = null;
        server.CommandReceived += frame => { command = frame; received.Set(); };

        using var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        var stream = client.GetStream();
        DevToolsProtocol.Read(stream);                    // the hello

        DevToolsProtocol.Write(stream, DevToolsProtocol.ClientFrames.Input, "tap 10 20");

        Assert.True(received.Wait(TimeSpan.FromSeconds(5)), "no command arrived");
        Assert.Equal("input", command!.Value.Type);
        Assert.Equal("tap 10 20", command.Value.Text);
    }

    [Fact]
    public void Server_BroadcastWithNoClientsIsHarmless()
    {
        using var server = new DevToolsServer(0);

        Assert.False(server.HasClients);
        server.Broadcast(DevToolsProtocol.ServerFrames.Patch, "{}");   // must not throw
    }

    [Fact]
    public void Server_SurvivesAClientThatDisconnectsMidStream()
    {
        // The realistic failure: the IDE is closed while the app keeps rendering. A dev tool that takes
        // the app down with it when the developer closes a window has failed at its only job.
        using var server = new DevToolsServer(0);
        var client = new TcpClient();
        client.Connect("127.0.0.1", server.Port);
        DevToolsProtocol.Read(client.GetStream());
        WaitUntil(() => server.HasClients);

        client.Dispose();

        for (var i = 0; i < 50; i++)
            server.Broadcast(DevToolsProtocol.ServerFrames.Patch, $"{{\"n\":{i}}}");

        WaitUntil(() => !server.HasClients);
        Assert.False(server.HasClients);
    }

    // ---- the opt-in ----------------------------------------------------------------------------

    [Fact]
    public void Wrap_ReturnsTheBridgeUntouchedWhenTheEnvironmentIsSilent()
    {
        var previous = Environment.GetEnvironmentVariable(DevTools.PortVariable);
        Environment.SetEnvironmentVariable(DevTools.PortVariable, null);
        try
        {
            var inner = new RecordingBridge();

            Assert.Same(inner, DevTools.Wrap(inner, "skia"));
            Assert.False(DevTools.IsEnabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DevTools.PortVariable, previous);
        }
    }

    [Fact]
    public void Wrap_TapsTheBridgeWhenAPortIsRequested()
    {
        var previous = Environment.GetEnvironmentVariable(DevTools.PortVariable);
        Environment.SetEnvironmentVariable(DevTools.PortVariable, "0");
        try
        {
            var inner = new RecordingBridge();

            var wrapped = DevTools.Wrap(inner, "skia");

            Assert.IsType<PatchTapBridge>(wrapped);
            Assert.True(DevTools.IsEnabled);

            wrapped.Render("{}");
            Assert.Equal(["{}"], inner.Renders);
        }
        finally
        {
            DevTools.Shutdown();
            Environment.SetEnvironmentVariable(DevTools.PortVariable, previous);
        }
    }

    static void WaitUntil(Func<bool> condition)
    {
        // The accept happens on the server's own thread, so "connected" is not observable the instant
        // Connect returns.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            Thread.Sleep(10);
    }
}

file sealed class RecordingBridge : IBridge
{
    Action<string, string?>? _handler;

    public List<string> Renders { get; } = [];

    public void Render(string json) => Renders.Add(json);

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    public void RaiseEvent(string id, string? value) => _handler?.Invoke(id, value);
}
