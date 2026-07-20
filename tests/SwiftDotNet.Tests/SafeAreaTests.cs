// The safe-area API is annotated [SupportedOSPlatform("ios"/"android")] and this test project targets
// plain net10.0, so calling it here warns CA1416 *by design* — that warning is the feature. Suppress it
// for the file rather than weakening the annotations.
#pragma warning disable CA1416

using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Pins the safe-area contract: the wire shape both native shims parse (<c>safeAreaPadding</c> /
/// <c>ignoresSafeArea</c>), the host→C# inset channel on the reserved <c>$safeArea</c> event id, and the
/// de-duplication that stops per-layout-pass inset reports from spinning the render loop.
/// See <c>Core/SafeArea.cs</c> and <c>docs/modifiers-gestures-animation.md</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SafeAreaTests
{
    // ---- wire shape ----------------------------------------------------------

    [Fact]
    public void SafeAreaPadding_SerializesEdgesAndRegions()
    {
        var mod = SingleModifier(new Text("x").SafeAreaPadding(Edge.Top | Edge.Bottom));

        Assert.Equal("safeAreaPadding", mod.GetProperty("type").GetString());
        Assert.Equal("top,bottom", mod.GetProperty("value").GetString());
        Assert.Equal("container", mod.GetProperty("regions").GetString());
    }

    [Fact]
    public void IgnoresSafeArea_DefaultsToAllEdgesAndAllRegions()
    {
        var mod = SingleModifier(new Text("x").IgnoresSafeArea());

        Assert.Equal("ignoresSafeArea", mod.GetProperty("type").GetString());
        Assert.Equal("all", mod.GetProperty("value").GetString());
        Assert.Equal("all", mod.GetProperty("regions").GetString());
    }

    [Fact]
    public void KeyboardRegion_SerializesItsToken()
    {
        var mod = SingleModifier(new Text("x").SafeAreaPadding(Edge.Bottom, SafeAreaRegions.Keyboard));

        Assert.Equal("bottom", mod.GetProperty("value").GetString());
        Assert.Equal("keyboard", mod.GetProperty("regions").GetString());
    }

    [Theory]
    [InlineData(Edge.Top, "top")]
    [InlineData(Edge.Leading, "leading")]
    [InlineData(Edge.Bottom, "bottom")]
    [InlineData(Edge.Trailing, "trailing")]
    [InlineData(Edge.Horizontal, "leading,trailing")]
    [InlineData(Edge.Vertical, "top,bottom")]
    [InlineData(Edge.All, "all")]
    // Edge order in the token is fixed (top, leading, bottom, trailing) regardless of how the flags were
    // combined — the shims split on "," and don't care, but a stable token keeps the diff quiet.
    [InlineData(Edge.Trailing | Edge.Top, "top,trailing")]
    public void EdgeFlags_MapToStableTokens(Edge edges, string expected)
        => Assert.Equal(expected, SingleModifier(new Text("x").SafeAreaPadding(edges)).GetProperty("value").GetString());

    [Fact]
    public void ModifiersAreOrderPreserving_AlongsideOtherModifiers()
    {
        var node = Render(new Text("x").Padding(4).SafeAreaPadding(Edge.Top).Opacity(0.5));
        var types = node.GetProperty("modifiers").EnumerateArray()
            .Select(m => m.GetProperty("type").GetString()).ToArray();

        Assert.Equal(new[] { "padding", "safeAreaPadding", "opacity" }, types);
    }

    // ---- host → C# inset channel ---------------------------------------------

    [Fact]
    public void InsetReport_PopulatesCurrentAndRerendersReaders()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new InsetReadingView(), bridge);
        var baseline = bridge.RenderCount;

        bridge.Emit("$safeArea", "47;1;34;2;0");
        pump.Drain();

        Assert.Equal(47, SafeArea.Current.Top);
        Assert.Equal(1, SafeArea.Current.Leading);
        Assert.Equal(34, SafeArea.Current.Bottom);
        Assert.Equal(2, SafeArea.Current.Trailing);
        Assert.Equal(0, SafeArea.Current.Keyboard);

        // A Body that reads the insets is recomputed and the new value actually crosses the bridge —
        // that's the whole point of routing the report through RequestRender rather than a plain setter.
        Assert.Equal(baseline + 1, bridge.RenderCount);
        Assert.Contains("top=47", bridge.LastJson);
    }

    [Fact]
    public void RepeatedIdenticalReport_DoesNotRender()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new InsetReadingView(), bridge);

        // Use a value distinct from every other test's so this is a real change the first time,
        // whatever order the collection runs in.
        bridge.Emit("$safeArea", "11;0;22;0;0");
        pump.Drain();
        var afterFirst = bridge.RenderCount;

        // Both shims report on every layout pass; an unchanged report must not even schedule a render.
        bridge.Emit("$safeArea", "11;0;22;0;0");
        Assert.Equal(0, pump.PendingCount);
        pump.Drain();

        Assert.Equal(afterFirst, bridge.RenderCount);
    }

    [Fact]
    public void KeyboardHeight_IsReportedAndCleared()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root"), bridge);

        bridge.Emit("$safeArea", "47;0;34;0;336");
        pump.Drain();
        Assert.Equal(336, SafeArea.Current.Keyboard);

        bridge.Emit("$safeArea", "47;0;34;0;0");
        pump.Drain();
        Assert.Equal(0, SafeArea.Current.Keyboard);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("47;0;34")]              // too few fields
    [InlineData("47;0;34;0;not-a-number")]
    public void MalformedReport_IsIgnoredAndKeepsLastKnownValue(string? payload)
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root"), bridge);

        bridge.Emit("$safeArea", "5;6;7;8;9");
        pump.Drain();
        var good = SafeArea.Current;

        bridge.Emit("$safeArea", payload);
        pump.Drain();

        Assert.Equal(good, SafeArea.Current);
    }

    [Fact]
    public void ReservedEventId_DoesNotReachNodeCallbacks()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var tapped = false;
        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root").OnTapGesture(() => tapped = true), bridge);

        bridge.Emit("$safeArea", "3;0;4;0;0");
        pump.Drain();

        Assert.False(tapped);
    }

    // ---- other backends ------------------------------------------------------

    [Fact]
    public void NonMobileBackend_RendersUnaffectedByTheModifiers()
    {
        // Skia is the self-drawing backend that runs headless here. It has no safe-area concept, so the
        // two wire types must fall through its modifier switch untouched — no throw, and layout identical
        // to the same tree without them.
        static SkiaBridge LayOut(View root)
        {
            var bridge = new SkiaBridge();
            var host = new SkiaImageHost(bridge);
            SwiftApp.Run(root, bridge);
            host.RenderPng(400, 600);
            return bridge;
        }

        var plain = LayOut(new VStack(new Text("hello")));
        var withSafeArea = LayOut(new VStack(new Text("hello").SafeAreaPadding().IgnoresSafeArea()));

        Assert.True(plain.TryGetFrame("0.0", out var a), "baseline text should be laid out");
        Assert.True(withSafeArea.TryGetFrame("0.0", out var b), "text should still be laid out");
        Assert.Equal(a, b);
    }

    // ---- helpers -------------------------------------------------------------

    static JsonElement Render(View view)
    {
        var bridge = new EventBridge();
        SwiftApp.Run(view, bridge);
        using var doc = JsonDocument.Parse(bridge.LastJson!);
        return doc.RootElement.GetProperty("ops")[0].GetProperty("node").Clone();
    }

    static JsonElement SingleModifier(View view)
        => Assert.Single(Render(view).GetProperty("modifiers").EnumerateArray().ToArray());
}

/// <summary>A composite view whose Body reads the ambient insets — the shape a real app uses to lay out
/// against the safe area, and the only way an inset report produces a visible tree change.</summary>
file sealed class InsetReadingView : View
{
#pragma warning disable CA1416
    public override View? Body => new Text($"top={SafeArea.Current.Top}");
#pragma warning restore CA1416
}

/// <summary>Captures render JSON and lets a test play the host's event callback back into the runtime.</summary>
file sealed class EventBridge : IBridge
{
    Action<string, string?>? _handler;

    public string? LastJson { get; private set; }
    public int RenderCount { get; private set; }

    public void Render(string json) { LastJson = json; RenderCount++; }
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    /// <summary>Raises an event exactly as the native shim would.</summary>
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);
}

/// <summary>Deterministic stand-in for the UI thread, so scheduled renders can be counted.</summary>
file sealed class PumpContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();
    public int PendingCount => _queue.Count;
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    public void Drain() { while (_queue.Count > 0) { var (cb, state) = _queue.Dequeue(); cb(state); } }
    public IDisposable Install() { var previous = Current; SetSynchronizationContext(this); return new Restore(previous); }
    sealed class Restore(SynchronizationContext? previous) : IDisposable { public void Dispose() => SetSynchronizationContext(previous); }
}
