using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// <see cref="SkiaPointerRouter"/> is what makes F1 gestures real on the self-drawing backend. Every other
/// backend inherits tap/long-press/swipe/drag/pinch recognizers from its toolkit; Skia has none, so hosts
/// feed raw pointer events into the router and it resolves them. Before it existed, hosts forwarded only
/// taps and <c>.OnDrag</c>/<c>.OnMagnify</c> were inert — which left the Controls library's Slider,
/// RangeSlider, ColorPicker, FloatingPanel, SwipeContainer, ReorderableList and ImageViewer visually
/// present but non-interactive.
///
/// The router takes an explicit clock, so these tests drive time directly — no sleeping, no flake.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaPointerRouterTests
{
    const int W = 400, H = 800;

    // ---- continuous drag -----------------------------------------------------

    [Fact]
    public void PressMoveRelease_EmitsTheFullDragGrammar()
    {
        var log = new List<DragInfo>();
        var (bridge, router) = Host(new DragTarget(log));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(140, 130), 0.10);
        router.Up(new SKPoint(160, 150), 0.20);

        Assert.Equal(
            new[] { GesturePhase.Began, GesturePhase.Changed, GesturePhase.Ended },
            log.Select(d => d.Phase));

        // Translation is cumulative from the press point, not per-move.
        Assert.Equal((40.0, 30.0), log[1].Translation);
        Assert.Equal((60.0, 50.0), log[2].Translation);
        Assert.Equal((160.0, 150.0), log[2].Location);
        GC.KeepAlive(bridge);
    }

    [Fact]
    public void DragRelease_ReportsFlingVelocity()
    {
        var log = new List<DragInfo>();
        var (_, router) = Host(new DragTarget(log));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(120, 100), 0.10);
        router.Move(new SKPoint(140, 100), 0.20);
        router.Up(new SKPoint(200, 100), 0.30);   // 60px in 100ms → 600 px/s

        var ended = log[^1];
        Assert.Equal(GesturePhase.Ended, ended.Phase);
        Assert.Equal(600, ended.Velocity.X, 0);
        Assert.Equal(0, ended.Velocity.Y, 0);
    }

    [Fact]
    public void DragOnAControl_DoesNotAlsoFireATap()
    {
        var taps = 0;
        var log = new List<DragInfo>();
        var (_, router) = Host(new DragTarget(log, () => taps++));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(150, 100), 0.05);
        router.Up(new SKPoint(150, 100), 0.10);

        Assert.NotEmpty(log);
        Assert.Equal(0, taps);
    }

    [Fact]
    public void Cancel_EndsALiveDragInPlace()
    {
        var log = new List<DragInfo>();
        var (_, router) = Host(new DragTarget(log));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(150, 100), 0.05);
        router.Cancel();

        Assert.Equal(GesturePhase.Ended, log[^1].Phase);
    }

    // ---- tap / long-press / swipe -------------------------------------------

    [Fact]
    public void PressAndRelease_WithoutMoving_IsATap()
    {
        var taps = 0;
        var (_, router) = Host(new TapTarget(() => taps++));

        router.Down(new SKPoint(100, 100), 0);
        router.Up(new SKPoint(102, 101), 0.05);   // inside TapSlop

        Assert.Equal(1, taps);
    }

    [Fact]
    public void PressAndRelease_AfterMovingFar_IsNotATap()
    {
        var taps = 0;
        var (_, router) = Host(new TapTarget(() => taps++));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(180, 100), 0.30);   // slow, so it isn't a swipe either
        router.Up(new SKPoint(180, 100), 0.60);

        Assert.Equal(0, taps);
    }

    [Fact]
    public void HoldingStill_FiresLongPress_AndSuppressesTheTap()
    {
        int taps = 0, longPresses = 0;
        var (_, router) = Host(new TapTarget(() => taps++, () => longPresses++));

        router.Down(new SKPoint(100, 100), 0);
        router.Poll(0.20);                        // not yet
        Assert.Equal(0, longPresses);
        router.Poll(0.60);                        // past LongPressSeconds
        router.Poll(0.90);                        // must not fire twice
        router.Up(new SKPoint(100, 100), 1.0);

        Assert.Equal(1, longPresses);
        Assert.Equal(0, taps);
    }

    [Fact]
    public void MovingBeforeTheHoldElapses_CancelsTheLongPress()
    {
        var longPresses = 0;
        var (_, router) = Host(new TapTarget(null, () => longPresses++));

        router.Down(new SKPoint(100, 100), 0);
        router.Move(new SKPoint(140, 100), 0.10);
        router.Poll(0.80);

        Assert.Equal(0, longPresses);
    }

    [Fact]
    public void AFastFlick_IsASwipe()
    {
        var swipes = new List<string>();
        var (_, router) = Host(new SwipeTarget(swipes));

        router.Down(new SKPoint(300, 100), 0);
        router.Move(new SKPoint(250, 100), 0.02);
        router.Up(new SKPoint(200, 100), 0.04);   // 50px in 20ms → 2500 px/s, leftwards

        Assert.Equal(new[] { "left" }, swipes);
    }

    [Fact]
    public void ASlowDrag_IsNotASwipe()
    {
        var swipes = new List<string>();
        var (_, router) = Host(new SwipeTarget(swipes));

        router.Down(new SKPoint(300, 100), 0);
        router.Move(new SKPoint(250, 100), 0.50);
        router.Up(new SKPoint(200, 100), 1.00);   // 50px in 500ms → 100 px/s

        Assert.Empty(swipes);
    }

    // ---- pinch ---------------------------------------------------------------

    [Fact]
    public void PinchDeltas_AccumulateIntoACumulativeScale()
    {
        var scales = new List<double>();
        var (_, router) = Host(new MagnifyTarget(scales));

        router.PinchDelta(new SKPoint(200, 200), 1.5f);
        router.PinchDelta(new SKPoint(200, 200), 2.0f);
        router.EndPinch(new SKPoint(200, 200));

        // Began(1.0), then the running product 1.5 and 3.0, then the same 3.0 on end.
        Assert.Equal(new[] { 1.0, 1.5, 3.0, 3.0 }, scales);
    }

    // ---- harness -------------------------------------------------------------

    static (SkiaBridge, SkiaPointerRouter) Host(View root)
    {
        var bridge = new SkiaBridge();
        var image = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        image.RenderPng(W, H);          // gestures hit-test against laid-out frames
        return (bridge, new SkiaPointerRouter(bridge));
    }
}

file sealed class DragTarget(List<DragInfo> log, Action? onTap = null) : View
{
    public override View? Body
    {
        get
        {
            var rect = new Rectangle().Frame(300, 300).OnDrag(log.Add);
            return onTap is null ? rect : rect.OnTapGesture(onTap);
        }
    }
}

file sealed class TapTarget(Action? onTap = null, Action? onLongPress = null) : View
{
    public override View? Body
    {
        get
        {
            var rect = new Rectangle().Frame(300, 300);
            if (onTap is not null) rect = (Rectangle)rect.OnTapGesture(onTap);
            if (onLongPress is not null) rect = (Rectangle)rect.OnLongPress(onLongPress);
            return rect;
        }
    }
}

file sealed class SwipeTarget(List<string> log) : View
{
    public override View? Body =>
        new Rectangle().Frame(300, 300)
            .OnSwipe(SwipeDirection.Left, () => log.Add("left"))
            .OnSwipe(SwipeDirection.Right, () => log.Add("right"));
}

file sealed class MagnifyTarget(List<double> log) : View
{
    public override View? Body => new Rectangle().Frame(300, 300).OnMagnify(log.Add);
}
