using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The <c>alignment</c> prop on a ZStack is the whole mechanism behind <see cref="OverlayPosition"/>:
/// <see cref="Overlay"/> lowers a positioned presentation to a ZStack carrying an alignment token, and
/// each backend maps that token to its own layout primitive. GTK, Web and WinUI all used to drop the prop
/// on the floor, so every Toast/Dialog/FloatingPanel rendered centred regardless of what was asked for.
/// These tests pin the wire contract those backends read.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class ZStackAlignmentWireTests
{
    [Theory]
    [InlineData("bottom")]
    [InlineData("top")]
    [InlineData("topLeading")]
    [InlineData("bottomTrailing")]
    public void ZStack_SerializesItsAlignmentToken(string expected)
    {
        var alignment = expected switch
        {
            "bottom" => Alignment.Bottom,
            "top" => Alignment.Top,
            "topLeading" => Alignment.TopLeading,
            _ => Alignment.BottomTrailing,
        };
        var node = Render(new ZStack(new Text("x")).Alignment(alignment));
        Assert.Equal(expected, node.GetProperty("props").GetProperty("alignment").GetString());
    }

    [Fact]
    public void ZStack_WithoutAlignment_OmitsTheProp()
    {
        var node = Render(new ZStack(new Text("x")));
        Assert.False(node.GetProperty("props").TryGetProperty("alignment", out _));
    }

    [Fact]
    public void BottomOverlay_LowersToAZStackAlignedBottom()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();

        var bridge = new CapturingBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);

        Overlay.Present(new Text("toast"), new OverlayOptions { Position = OverlayPosition.Bottom });
        pump.Drain();

        // The presentation layer is a ZStack asking to be pinned to the bottom — not centred.
        var root = LastRoot(bridge.LastJson);
        Assert.Contains(Walk(root), n =>
            n.GetProperty("type").GetString() == "ZStack"
            && n.GetProperty("props").TryGetProperty("alignment", out var a)
            && a.GetString() == "bottom");

        Overlay.DismissAll();
        pump.Drain();
    }

    // ---- helpers -------------------------------------------------------------

    static JsonElement Render(View view)
    {
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);
        return LastRoot(bridge.LastJson);
    }

    static JsonElement LastRoot(string? json)
    {
        using var doc = JsonDocument.Parse(json!);
        return doc.RootElement.GetProperty("ops")[0].GetProperty("node").Clone();
    }

    static IEnumerable<JsonElement> Walk(JsonElement node)
    {
        yield return node;
        if (!node.TryGetProperty("children", out var children)) yield break;
        foreach (var child in children.EnumerateArray())
            foreach (var n in Walk(child))
                yield return n;
    }
}

file sealed class CapturingBridge : IBridge
{
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) { }
    public void Render(string json) => LastJson = json;
}

/// <summary>Deterministic stand-in for the UI thread — Overlay schedules its renders onto it.</summary>
file sealed class PumpContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    public void Drain() { while (_queue.Count > 0) { var (cb, state) = _queue.Dequeue(); cb(state); } }
    public IDisposable Install() { var previous = Current; SetSynchronizationContext(this); return new Restore(previous); }
    sealed class Restore(SynchronizationContext? previous) : IDisposable { public void Dispose() => SetSynchronizationContext(previous); }
}
