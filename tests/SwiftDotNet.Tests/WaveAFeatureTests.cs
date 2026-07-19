using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Wire-format tests for the Wave-A framework features from <c>plans/controls-missing-features-plan.md</c>:
/// F3 raster images, F4 transform modifiers + looping animation, F5 gradient brushes. These assert the
/// serialized node/modifier props so every backend interprets the same contract.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class WaveAFeatureTests
{
    // ---- F5: gradient brushes ------------------------------------------------

    [Fact]
    public void LinearGradient_SerializesAngleAndStops()
    {
        var node = Render(new VStack().Background(
            new LinearGradient(45, new GradientStop(Color.Red, 0), new GradientStop(Color.Blue, 1))));
        var bg = Modifier(node, "background");
        Assert.Equal("linear:45:red@0;blue@1", bg.GetProperty("gradient").GetString());
    }

    [Fact]
    public void RadialGradient_SerializesStops()
    {
        var node = Render(new VStack().Background(new RadialGradient(Color.Hex("#fff"), Color.Hex("#000"))));
        Assert.Equal("radial:#fff@0;#000@1", Modifier(node, "background").GetProperty("gradient").GetString());
    }

    [Fact]
    public void FlatColorBackground_StillUsesValueNotGradient()
    {
        var node = Render(new VStack().Background(Color.Green));
        var bg = Modifier(node, "background");
        Assert.Equal("green", bg.GetProperty("value").GetString());
        Assert.False(bg.TryGetProperty("gradient", out _));
    }

    // ---- F4: transforms ------------------------------------------------------

    [Fact]
    public void Offset_SerializesXY()
    {
        var m = Modifier(Render(new Text("x").Offset(10, -5)), "offset");
        Assert.Equal(10, m.GetProperty("x").GetDouble());
        Assert.Equal(-5, m.GetProperty("y").GetDouble());
    }

    [Fact]
    public void Rotation_SerializesDegreesAndAnchor()
    {
        var m = Modifier(Render(new Text("x").Rotation(90, Alignment.TopLeading)), "rotation");
        Assert.Equal(90, m.GetProperty("degrees").GetDouble());
        Assert.Equal("topLeading", m.GetProperty("value").GetString());
    }

    // ---- F4: looping animation ----------------------------------------------

    [Fact]
    public void RepeatingAnimation_EmitsRepeatCountAndAutoreverse()
    {
        var m = Modifier(Render(new Text("x").Animation(Anim.EaseInOut().Repeating(count: -1, autoreverse: true), on: 1)), "animation");
        Assert.Equal(-1, m.GetProperty("repeatCount").GetDouble());
        Assert.Equal("true", m.GetProperty("autoreverse").GetString());
    }

    [Fact]
    public void OneShotAnimation_OmitsRepeatFields()
    {
        var m = Modifier(Render(new Text("x").Animation(Anim.EaseInOut(), on: 1)), "animation");
        Assert.False(m.TryGetProperty("repeatCount", out _));
    }

    // ---- F3: raster images ---------------------------------------------------

    [Fact]
    public void ImageFromUrl_EmitsUrlAndContentMode()
    {
        var node = Render(Image.FromUrl("https://x/y.png").ContentMode(ImageContentMode.Fill));
        Assert.Equal("https://x/y.png", node.GetProperty("props").GetProperty("url").GetString());
        Assert.Equal("fill", node.GetProperty("props").GetProperty("contentMode").GetString());
    }

    [Fact]
    public void ImageFromBytes_EmitsBase64()
    {
        var node = Render(Image.FromBytes(new byte[] { 1, 2, 3, 4 }));
        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            node.GetProperty("props").GetProperty("bytes").GetString());
    }

    [Fact]
    public void ImageSystem_HasNoContentMode()
    {
        var node = Render(Image.System("star.fill"));
        Assert.Equal("star.fill", node.GetProperty("props").GetProperty("system").GetString());
        Assert.False(node.GetProperty("props").TryGetProperty("contentMode", out _));
    }

    // ---- F1: continuous drag / pinch -----------------------------------------

    [Fact]
    public void OnDrag_SerializesEventAndMinimumDistance()
    {
        var m = Modifier(Render(new VStack().OnDrag(_ => { }, minimumDistance: 8)), "onDrag");
        // Event id is the node's structural id plus the modifier slot, so multiple event modifiers coexist.
        Assert.Equal("0$0", m.GetProperty("event").GetString());
        Assert.Equal(8, m.GetProperty("amount").GetDouble());
    }

    [Fact]
    public void OnMagnify_SerializesEvent()
    {
        var m = Modifier(Render(new VStack().OnMagnify(_ => { })), "onMagnify");
        Assert.Equal("0$0", m.GetProperty("event").GetString());
    }

    [Fact]
    public void Drag_RoundTripsGrammarIntoDragInfo()
    {
        DragInfo? got = null;
        var bridge = new FiringWaveBridge();
        SwiftApp.Run(new WaveHost(new VStack().OnDrag(i => got = i)), bridge);

        bridge.Fire("0$0", "c;10,20;5,6;100,-200");

        Assert.NotNull(got);
        Assert.Equal(GesturePhase.Changed, got!.Value.Phase);
        Assert.Equal((10.0, 20.0), got.Value.Translation);
        Assert.Equal((5.0, 6.0), got.Value.Location);
        Assert.Equal((100.0, -200.0), got.Value.Velocity);
    }

    [Fact]
    public void Drag_ParsesBeganAndEndedPhases()
    {
        DragInfo? got = null;
        var bridge = new FiringWaveBridge();
        SwiftApp.Run(new WaveHost(new VStack().OnDrag(i => got = i)), bridge);

        bridge.Fire("0$0", "b;0,0;1,2;0,0");
        Assert.Equal(GesturePhase.Began, got!.Value.Phase);
        bridge.Fire("0$0", "e;3,4;5,6;0,0");
        Assert.Equal(GesturePhase.Ended, got!.Value.Phase);
    }

    // ---- F2: overlay host ----------------------------------------------------

    [Fact]
    public void OverlayHost_WithNoOverlays_RendersRootDirectly()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();

        var bridge = new CapturingWaveBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);
        Assert.Equal("Text", LastRoot(bridge.LastJson).GetProperty("type").GetString());
    }

    [Fact]
    public void Present_StacksScrimAndContentOverRoot()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();
        var bridge = new CapturingWaveBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);

        Overlay.Present(new Text("sheet"));
        pump.Drain();                       // the version bump schedules a render onto the pump
        var root = LastRoot(bridge.LastJson);

        Assert.Equal("ZStack", root.GetProperty("type").GetString());
        Assert.Contains(Walk(root), n => n.GetProperty("type").GetString() == "Text" && Text(n) == "sheet");
        Assert.Contains(Walk(root), n => n.GetProperty("type").GetString() == "Text" && Text(n) == "root");
        Overlay.DismissAll();
        pump.Drain();
    }

    [Fact]
    public void Dismiss_RemovesTheOverlay()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();
        var bridge = new CapturingWaveBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);

        var id = Overlay.Present(new Text("sheet"));
        pump.Drain();
        Assert.Contains(Walk(LastRoot(bridge.LastJson)), n => Text(n) == "sheet");
        Overlay.Dismiss(id);
        pump.Drain();

        var root = LastRoot(bridge.LastJson);
        Assert.Equal("Text", root.GetProperty("type").GetString());  // back to bare root
        Assert.DoesNotContain(Walk(root), n => Text(n) == "sheet");
        Overlay.DismissAll();
        pump.Drain();
    }

    // ---- helpers -------------------------------------------------------------

    static IEnumerable<JsonElement> Walk(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("children", out var kids))
            foreach (var c in kids.EnumerateArray())
                foreach (var d in Walk(c)) yield return d;
    }

    static string Text(JsonElement node) =>
        node.TryGetProperty("props", out var p) && p.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

    static JsonElement RenderRoot(View root)
    {
        var bridge = new CapturingWaveBridge();
        SwiftApp.Run(root, bridge);
        return LastRoot(bridge.LastJson);
    }

    static JsonElement LastRoot(string? json)
    {
        var op = JsonDocument.Parse(json!).RootElement.GetProperty("ops").EnumerateArray().First();
        return (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
    }

    static JsonElement Render(View view)
    {
        var bridge = new CapturingWaveBridge();
        SwiftApp.Run(new WaveHost(view), bridge);
        var op = JsonDocument.Parse(bridge.LastJson!).RootElement.GetProperty("ops").EnumerateArray().First();
        var root = (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
        // WaveHost renders the view directly, so the root node IS the view under test.
        return root;
    }

    static JsonElement Modifier(JsonElement node, string type) =>
        node.GetProperty("modifiers").EnumerateArray().First(m => m.GetProperty("type").GetString() == type);
}

file sealed class WaveHost(View child) : View
{
    public override View Body => child;
}

file sealed class CapturingWaveBridge : IBridge
{
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) { }
    public void Render(string json) => LastJson = json;
}

/// <summary>Captures the event handler so a test can simulate a backend firing an event by (id, value).</summary>
file sealed class FiringWaveBridge : IBridge
{
    Action<string, string?>? _handler;
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Render(string json) { }
    public void Fire(string id, string? value) => _handler!(id, value);
}

/// <summary>A manual UI pump so a test can deterministically flush a deferred (posted) render.</summary>
file sealed class ManualSyncContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    public void Drain() { while (_queue.Count > 0) { var (cb, state) = _queue.Dequeue(); cb(state); } }
    public IDisposable Install() { var previous = Current; SetSynchronizationContext(this); return new Restore(previous); }
    sealed class Restore(SynchronizationContext? previous) : IDisposable { public void Dispose() => SetSynchronizationContext(previous); }
}
