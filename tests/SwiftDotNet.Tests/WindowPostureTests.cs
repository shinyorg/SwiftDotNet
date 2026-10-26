using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Pins the posture channel: the <c>$posture</c> wire both native shims (and the web/Skia reporters) emit,
/// the de-duplication that keeps per-layout-pass reports from spinning the render loop, and
/// <see cref="TwoPaneView"/>'s layout choice — driven through <see cref="WindowPosture.Report"/> exactly as a
/// host would. See <c>Core/WindowPosture.cs</c>, <c>Core/Views/TwoPaneView.cs</c> and
/// <c>docs/views-and-controls.md#twopaneview</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public sealed class WindowPostureTests : IDisposable
{
    // Posture is process-wide; leave it as every other test expects to find it. Under a throwaway pump: a
    // report schedules a render on the context SwiftApp captured, and under xUnit's own context that render
    // would run later on another thread and could land on the *next* test's bridge.
    public void Dispose()
    {
        using var _ = new PumpContext().Install();
        WindowPosture.Report(PostureInfo.None);
    }

    // ---- host → C# channel ---------------------------------------------------

    [Fact]
    public void BookReport_PopulatesCurrentAndRerendersReaders()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new PostureReadingView(), bridge);
        var baseline = bridge.RenderCount;

        bridge.Emit("$posture", "halfOpen;vertical;420.5;0;0;841;;841;701");
        pump.Drain();

        var p = WindowPosture.Current;
        Assert.Equal(FoldState.HalfOpen, p.State);
        Assert.Equal(FoldOrientation.Vertical, p.Orientation);
        Assert.Equal(420.5, p.FoldX);
        Assert.Equal(841, p.FoldHeight);
        Assert.Null(p.HingeAngleDegrees);
        Assert.Equal(841, p.WindowWidth);
        Assert.Equal(701, p.WindowHeight);
        Assert.True(p.IsBook);
        Assert.False(p.IsTabletop);

        Assert.Equal(baseline + 1, bridge.RenderCount);
        Assert.Contains("fold=HalfOpen", bridge.LastJson);
    }

    [Fact]
    public void TabletopReport_WithHingeAngle()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root"), bridge);

        bridge.Emit("$posture", "halfOpen;horizontal;0;380;402;12;97.25;402;874");
        pump.Drain();

        var p = WindowPosture.Current;
        Assert.True(p.IsTabletop);
        Assert.Equal(380, p.FoldY);
        Assert.Equal(12, p.FoldHeight);
        Assert.Equal(97.25, p.HingeAngleDegrees);
    }

    [Fact]
    public void NonHalfOpenReport_DropsStaleGeometry()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();
        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root"), bridge);

        // A host that keeps sending the last fold rect after the device flattens must not leave a "fold".
        bridge.Emit("$posture", "flat;vertical;420;0;0;841;180;841;701");
        pump.Drain();

        var p = WindowPosture.Current;
        Assert.Equal(FoldState.Flat, p.State);
        Assert.Equal(FoldOrientation.None, p.Orientation);
        Assert.Equal(0, p.FoldX);
        Assert.False(p.HasFold);
        Assert.Equal(841, p.WindowWidth);
    }

    [Fact]
    public void RepeatedIdenticalReport_DoesNotRender()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var bridge = new EventBridge();
        SwiftApp.Run(new PostureReadingView(), bridge);

        bridge.Emit("$posture", "none;none;0;0;0;0;;393;852");
        pump.Drain();
        var afterFirst = bridge.RenderCount;

        bridge.Emit("$posture", "none;none;0;0;0;0;;393;852");
        Assert.Equal(0, pump.PendingCount);
        pump.Drain();

        Assert.Equal(afterFirst, bridge.RenderCount);
    }

    [Fact]
    public void ReportApi_RendersOnceAndDedupes()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();
        var bridge = new EventBridge();
        SwiftApp.Run(new PostureReadingView(), bridge);
        var baseline = bridge.RenderCount;

        var tabletop = new PostureInfo(FoldState.HalfOpen, FoldOrientation.Horizontal, 0, 300, 600, 0, null, 600, 640);
        WindowPosture.Report(tabletop);
        WindowPosture.Report(tabletop);
        pump.Drain();

        Assert.Equal(tabletop, WindowPosture.Current);
        Assert.Equal(baseline + 1, bridge.RenderCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("halfOpen;vertical;1;2;3;4;;5")]              // too few fields
    [InlineData("ajar;vertical;1;2;3;4;;5;6")]                // unknown state
    [InlineData("halfOpen;diagonal;1;2;3;4;;5;6")]            // unknown orientation
    [InlineData("halfOpen;vertical;1;2;x;4;;5;6")]            // bad number
    [InlineData("halfOpen;vertical;1;2;3;4;NaN;5;6")]         // non-finite angle
    public void MalformedReport_IsIgnoredAndKeepsLastKnownValue(string? payload)
    {
        var pump = new PumpContext();
        using var _ = pump.Install();
        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root"), bridge);

        bridge.Emit("$posture", "halfOpen;vertical;7;0;1;9;;20;9");
        pump.Drain();
        var good = WindowPosture.Current;

        bridge.Emit("$posture", payload);
        pump.Drain();

        Assert.Equal(good, WindowPosture.Current);
    }

    [Fact]
    public void ReservedEventId_DoesNotReachNodeCallbacksOrSafeArea()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();

        var tapped = false;
        var bridge = new EventBridge();
        SwiftApp.Run(new Text("root").OnTapGesture(() => tapped = true), bridge);
#pragma warning disable CA1416
        var insets = SafeArea.Current;
#pragma warning restore CA1416

        bridge.Emit("$posture", "halfOpen;vertical;3;0;0;4;;8;4");
        pump.Drain();

        Assert.False(tapped);
#pragma warning disable CA1416
        Assert.Equal(insets, SafeArea.Current);
#pragma warning restore CA1416
    }

    // ---- TwoPaneView layout choice -------------------------------------------

    static PostureInfo Window(double w, double h) => new(FoldState.None, FoldOrientation.None, 0, 0, 0, 0, null, w, h);

    [Fact]
    public void ResolveLayout_FollowsFoldThenWidthThenHeight()
    {
        var view = new TwoPaneView(new Text("a"), new Text("b")).MinWideWidth(600);

        Assert.Equal(TwoPaneLayout.Book, view.ResolveLayout(
            new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 420, 0, 0, 700, null, 841, 700)));
        Assert.Equal(TwoPaneLayout.Tabletop, view.ResolveLayout(
            new PostureInfo(FoldState.HalfOpen, FoldOrientation.Horizontal, 0, 400, 412, 0, null, 412, 900)));
        Assert.Equal(TwoPaneLayout.Wide, view.ResolveLayout(Window(841, 700)));
        Assert.Equal(TwoPaneLayout.SinglePane, view.ResolveLayout(Window(393, 852)));
        // No size reported (GTK, WinUI, a desktop Skia host…): assume room for both panes.
        Assert.Equal(TwoPaneLayout.Wide, view.ResolveLayout(PostureInfo.None));
        // A flat (fully open) device has no fold to honour — width decides.
        Assert.Equal(TwoPaneLayout.Wide, view.ResolveLayout(Window(841, 700) with { State = FoldState.Flat }));
    }

    [Fact]
    public void ResolveLayout_TallModeIsOptIn()
    {
        var phone = Window(393, 852);
        Assert.Equal(TwoPaneLayout.SinglePane,
            new TwoPaneView(new Text("a"), new Text("b")).ResolveLayout(phone));
        Assert.Equal(TwoPaneLayout.Tall,
            new TwoPaneView(new Text("a"), new Text("b")).TallModeConfiguration(TallMode.TopBottom).ResolveLayout(phone));
    }

    [Fact]
    public void ResolveLayout_IgnoresAFoldOutsideTheWindow()
    {
        // A fold reported at the very edge has nothing on one side; splitting there would hide a pane.
        var edge = new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 0, 0, 0, 700, null, 500, 700);
        Assert.Equal(TwoPaneLayout.SinglePane, new TwoPaneView(new Text("a"), new Text("b")).ResolveLayout(edge));
    }

    [Fact]
    public void SinglePane_ShowsThePriorityPane()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(Window(393, 852));

        var primary = Render(new TwoPaneView(new Text("list"), new Text("detail")));
        Assert.Equal("list", primary.GetProperty("props").GetProperty("text").GetString());

        var secondary = Render(new TwoPaneView(new Text("list"), new Text("detail")).PanePriority(Pane.Secondary));
        Assert.Equal("detail", secondary.GetProperty("props").GetProperty("text").GetString());
    }

    [Fact]
    public void BookFold_SplitsExactlyOnTheFold()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 390, 0, 20, 600, 90, 800, 600));

        var grid = Render(new TwoPaneView(new Text("list"), new Text("detail")));

        Assert.Equal("Grid", grid.GetProperty("type").GetString());
        Assert.Equal("fixed:390,fixed:20,star:1", grid.GetProperty("props").GetProperty("columnTracks").GetString());
        var kids = grid.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("type").GetString()).ToArray();
        Assert.Equal(new[] { "Text", "Spacer", "Text" }, kids);
    }

    [Fact]
    public void ZeroWidthFold_HasNoGapTrack()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 420, 0, 0, 700, null, 841, 700));

        var grid = Render(new TwoPaneView(new Text("list"), new Text("detail")));
        Assert.Equal("fixed:420,star:1", grid.GetProperty("props").GetProperty("columnTracks").GetString());
        Assert.Equal(2, grid.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public void Tabletop_StacksAboveAndBelowTheFold()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Horizontal, 0, 400, 412, 0, null, 412, 900));

        var grid = Render(new TwoPaneView(new Text("camera"), new Text("controls")));
        var props = grid.GetProperty("props");
        Assert.Equal("star:1", props.GetProperty("columnTracks").GetString());
        Assert.Equal("fixed:400,star:1", props.GetProperty("rowTracks").GetString());
    }

    [Fact]
    public void Wide_SplitsByRatio_AndBottomTopSwapsTall()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(Window(1000, 700));
        var wide = Render(new TwoPaneView(new Text("a"), new Text("b")).SplitRatio(0.4));
        Assert.Equal("star:0.4,star:0.6", wide.GetProperty("props").GetProperty("columnTracks").GetString());

        WindowPosture.Report(Window(400, 900));
        var tall = Render(new TwoPaneView(new Text("a"), new Text("b")).TallModeConfiguration(TallMode.BottomTop));
        Assert.Equal("b", tall.GetProperty("children")[0].GetProperty("props").GetProperty("text").GetString());
    }

    [Fact]
    public void FoldEvent_RelaysOutTheTwoPaneView()
    {
        var pump = new PumpContext();
        using var _ = pump.Install();
        WindowPosture.Report(Window(841, 701));

        var bridge = new EventBridge();
        SwiftApp.Run(new TwoPaneView(new Text("list"), new Text("detail")), bridge);
        Assert.Contains("star:0.5", bridge.LastJson);

        bridge.Emit("$posture", "halfOpen;vertical;420;0;0;701;;841;701");
        pump.Drain();
        Assert.Contains("fixed:420", bridge.LastJson);
    }

    // ---- native container seam -----------------------------------------------

    [Fact]
    public void NativeCapableBridge_GetsTheTwoPaneNode()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 420, 0, 0, 700, null, 840, 700));

        var bridge = new NativeTwoPaneBridge();
        SwiftApp.Run(new TwoPaneView(new Text("list"), new Text("detail")), bridge);
        using var doc = JsonDocument.Parse(bridge.LastJson!);
        var node = doc.RootElement.GetProperty("ops")[0].GetProperty("node");

        Assert.Equal("TwoPaneView", node.GetProperty("type").GetString());
        Assert.Equal(0.5, node.GetProperty("props").GetProperty("ratio").GetDouble());
        Assert.True(node.GetProperty("props").GetProperty("fold").GetBoolean());
        Assert.Equal(new[] { "0.0", "0.1" },
            node.GetProperty("children").EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToArray());
    }

    [Fact]
    public void NativeCapableBridge_StillGetsCompositesForStackedAndSingle()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        var bridge = new NativeTwoPaneBridge();

        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Horizontal, 0, 400, 412, 0, null, 412, 900));
        SwiftApp.Run(new TwoPaneView(new Text("a"), new Text("b")), bridge);
        Assert.Contains("\"type\":\"Grid\"", bridge.LastJson);

        WindowPosture.Report(Window(393, 852));
        SwiftApp.Run(new TwoPaneView(new Text("a"), new Text("b")), bridge);
        Assert.DoesNotContain("TwoPaneView", bridge.LastJson);

        // Opting out forces the composite even where a native container exists.
        WindowPosture.Report(Window(1000, 700));
        SwiftApp.Run(new TwoPaneView(new Text("a"), new Text("b")).UseNativeContainer(false), bridge);
        Assert.DoesNotContain("TwoPaneView", bridge.LastJson);
    }

    // ---- Skia layout ---------------------------------------------------------

    [Fact]
    public void Skia_BookFold_PlacesPanesEitherSideOfTheHinge()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Vertical, 390, 0, 20, 600, null, 800, 600));

        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new TwoPaneView(new Rectangle(), new Rectangle()), bridge);
        host.RenderPng(800, 600);

        Assert.True(bridge.TryGetFrame("0.0", out var primary));
        Assert.True(bridge.TryGetFrame("0.2", out var secondary));
        Assert.Equal(0, primary.Left, 1);
        Assert.Equal(390, primary.Right, 1);
        Assert.Equal(410, secondary.Left, 1);
        Assert.Equal(800, secondary.Right, 1);
        Assert.Equal(600, primary.Height, 1);
    }

    [Fact]
    public void Skia_Tabletop_PlacesPanesAboveAndBelow()
    {
        using var pump = new PumpContext().Install();   // see Dispose
        WindowPosture.Report(new PostureInfo(FoldState.HalfOpen, FoldOrientation.Horizontal, 0, 300, 400, 0, null, 400, 700));

        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new TwoPaneView(new Rectangle(), new Rectangle()), bridge);
        host.RenderPng(400, 700);

        Assert.True(bridge.TryGetFrame("0.0", out var top));
        Assert.True(bridge.TryGetFrame("0.1", out var bottom));
        Assert.Equal(300, top.Bottom, 1);
        Assert.Equal(300, bottom.Top, 1);
        Assert.Equal(700, bottom.Bottom, 1);
    }

    // ---- helpers -------------------------------------------------------------

    static JsonElement Render(View view)
    {
        var bridge = new EventBridge();
        SwiftApp.Run(view, bridge);
        using var doc = JsonDocument.Parse(bridge.LastJson!);
        return doc.RootElement.GetProperty("ops")[0].GetProperty("node").Clone();
    }
}

file sealed class PostureReadingView : View
{
    public override View? Body => new Text($"fold={WindowPosture.Current.State}");
}

file class EventBridge : IBridge
{
    Action<string, string?>? _handler;
    public string? LastJson { get; private set; }
    public int RenderCount { get; private set; }
    public void Render(string json) { LastJson = json; RenderCount++; }
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);
}

/// <summary>Stands in for the iOS 27.1 / Compose shims, which render <c>TwoPaneView</c> natively.</summary>
file sealed class NativeTwoPaneBridge : EventBridge, IBridgeCapabilities
{
    public bool SupportsNativeView(string viewType) => viewType == "TwoPaneView";
}

file sealed class PumpContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();
    public int PendingCount => _queue.Count;
    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));
    public void Drain() { while (_queue.Count > 0) { var (cb, state) = _queue.Dequeue(); cb(state); } }
    public IDisposable Install() { var previous = Current; SetSynchronizationContext(this); return new Restore(previous); }
    sealed class Restore(SynchronizationContext? previous) : IDisposable { public void Dispose() => SetSynchronizationContext(previous); }
}
