using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Hit-testing for <c>.OnDrag</c> in the shapes the sample and the Controls library actually build —
/// nested inside a ScrollView and a sized/aligned ZStack, not as a bare root child. Found by running the
/// sample on an iOS simulator: touches arrived correctly but no drag fired, so the gap was in the engine's
/// hit-test, not the host wiring.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaDragHitTestTests
{
    const int W = 420, H = 850;

    [Fact]
    public void Drag_HitsATargetNestedInAScrollViewAndSizedZStack()
    {
        var fired = new List<DragInfo>();
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new NestedDragPage(fired), bridge);
        host.RenderPng(W, H);

        // Aim at the middle of the tile, wherever layout put it.
        Assert.True(bridge.TryGetFrame("0.1.0", out var tile), "expected the drag tile to be laid out");
        var centre = new SKPoint(tile.MidX, tile.MidY);

        var router = new SkiaPointerRouter(bridge);
        router.Down(centre, 0);
        router.Move(new SKPoint(centre.X + 40, centre.Y), 0.1);
        router.Up(new SKPoint(centre.X + 40, centre.Y), 0.2);

        Assert.NotEmpty(fired);
        Assert.Equal(GesturePhase.Began, fired[0].Phase);
        Assert.Equal(40.0, fired[^1].Translation.X, 1);
    }

    [Fact]
    public void Drag_WorksOnAPushedNavigationDestination()
    {
        // The bug this pins: an overlay's content (a pushed nav destination, a Sheet body) is a separate
        // tree hanging off the overlay node, not part of _root's children. Taps were routed into it via
        // HitTestOverlay, but drag/pinch/long-press/swipe walked _root only — so every continuous gesture
        // was dead on any pushed page. Found by running the sample on an iOS simulator.
        var fired = new List<DragInfo>();
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new PushedGestureApp(fired, null), bridge);
        host.RenderPng(W, H);

        Assert.True(host.Tap(210, 447), "expected the nav link to push");
        host.RenderPng(W, H);                       // the push arranges the destination

        var router = new SkiaPointerRouter(bridge);
        router.Down(new SKPoint(210, 400), 0);
        router.Move(new SKPoint(250, 400), 0.1);
        router.Up(new SKPoint(250, 400), 0.2);

        Assert.NotEmpty(fired);
        Assert.Equal(40.0, fired[^1].Translation.X, 1);
    }

    [Fact]
    public void LongPress_WorksOnAPushedNavigationDestination()
    {
        var pressed = 0;
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new PushedGestureApp(new List<DragInfo>(), () => pressed++), bridge);
        host.RenderPng(W, H);

        Assert.True(host.Tap(210, 447));
        host.RenderPng(W, H);

        var router = new SkiaPointerRouter(bridge);
        router.Down(new SKPoint(210, 400), 0);
        router.Poll(0.9);

        Assert.Equal(1, pressed);
    }
}

/// <summary>A nav stack whose pushed destination carries the gesture target.</summary>
file sealed class PushedGestureApp(List<DragInfo> log, Action? onLongPress) : View
{
    public override View? Body =>
        new NavigationStack(
            new VStack(
                new NavigationLink("Open", new VStack(
                    onLongPress is null
                        ? new Rectangle().Frame(300, 300).OnDrag(log.Add)
                        : new Rectangle().Frame(300, 300).OnLongPress(onLongPress)
                ).Padding(20))
            ).Padding(20));
}

// Mirrors sample/SharedUI/ContentView.cs's gesture page: a ScrollView whose drag target is an offset,
// framed ZStack inside another height-framed, centre-aligned ZStack.
file sealed class NestedDragPage(List<DragInfo> log) : View
{
    public override View? Body =>
        new ScrollView(
            new Text("Continuous drag & pinch").Font(Font.Headline),
            new ZStack(
                new ZStack(new Text("✋").Font(Font.Title))
                    .Frame(70, 70).Background(Color.Hex("#FFE0B2")).CornerRadius(14)
                    .Offset(0, 0)
                    .OnDrag(log.Add)
            ).Frame(height: 180).Alignment(Alignment.Center)
        ).Padding(20);
}
