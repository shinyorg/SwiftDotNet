using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// <see cref="AbsoluteLayout"/> on Skia: point bounds, proportional size, and proportional position
/// (which anchors across the free space, so <c>x: 1</c> is flush right). See
/// <c>SkiaNode.MeasureAbsolute/ArrangeAbsolute</c> and <see cref="AbsoluteLayoutBounds"/>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaAbsoluteLayoutTests
{
    const int W = 400, H = 800;

    static SkiaBridge Render(View view)
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);
        return bridge;
    }

    [Fact]
    public void PointBounds_PlaceTheChildExactly()
    {
        var bridge = Render(new AbsoluteView());
        Assert.True(bridge.TryGetFrame("0.0", out var host));
        Assert.True(bridge.TryGetFrame("0.0.0", out var pinned));

        Assert.Equal(host.Left + 12, pinned.Left, 1);
        Assert.Equal(host.Top + 20, pinned.Top, 1);
        Assert.Equal(60, pinned.Width, 1);
        Assert.Equal(30, pinned.Height, 1);
    }

    [Fact]
    public void ProportionalSize_IsAFractionOfTheLayout()
    {
        var bridge = Render(new AbsoluteView());
        Assert.True(bridge.TryGetFrame("0.0", out var host));
        Assert.True(bridge.TryGetFrame("0.0.1", out var half));

        Assert.Equal(host.Width / 2, half.Width, 1);
        Assert.Equal(100, half.Height, 1);   // the layout is Frame(height: 200), so 0.5 → 100
    }

    [Fact]
    public void ProportionalPosition_AnchorsAcrossTheFreeSpace()
    {
        var bridge = Render(new AbsoluteView());
        Assert.True(bridge.TryGetFrame("0.0", out var host));
        Assert.True(bridge.TryGetFrame("0.0.2", out var right));

        // x: 1 with an 80pt-wide child → flush against the right edge, not off it.
        Assert.Equal(host.Right - 80, right.Left, 1);
        Assert.Equal(host.Right, right.Right, 1);
    }

    [Fact]
    public void UndeclaredChild_SitsAtTheOrigin()
    {
        var bridge = Render(new AbsoluteView());
        Assert.True(bridge.TryGetFrame("0.0", out var host));
        Assert.True(bridge.TryGetFrame("0.0.3", out var stray));

        Assert.Equal(host.Left, stray.Left, 1);
        Assert.Equal(host.Top, stray.Top, 1);
    }
}

// Wrapped in a VStack because the root node is always arranged into the whole canvas — a `.Frame(height:)`
// on the root would be measured but not honored, which is a property of the root, not of AbsoluteLayout.
file sealed class AbsoluteView : View
{
    public override View? Body =>
        new VStack(
            new AbsoluteLayout(
                    new Rectangle().LayoutBounds(12, 20, 60, 30),
                    new Rectangle().LayoutBounds(0, 0, 0.5, 0.5, LayoutFlags.SizeProportional),
                    new Rectangle().LayoutBounds(1, 0, 80, 24, LayoutFlags.XProportional),
                    new Text("stray"))
                .Frame(height: 200));
}
