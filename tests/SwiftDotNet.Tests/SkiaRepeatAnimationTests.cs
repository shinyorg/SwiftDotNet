using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// F4 repeating animation on Skia — the self-playing loop behind the Controls library's
/// <c>SkeletonView</c> shimmer and <c>BadgeView</c> pulse. The wire carries no from/to pair (a repeating
/// animation only says "play forever"), so every backend pulses opacity between the resting value and
/// 0.4× — matching the Web backend's shared <c>sdn-pulse</c> keyframes and the Compose bridge. These
/// tests pin that contract and the cycle bookkeeping.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaRepeatAnimationTests
{
    const int W = 200, H = 200;

    [Fact]
    public void RepeatForever_KeepsTheClockRunning()
    {
        var (bridge, _) = Host(new Pulsing(-1));

        // A one-shot animation settles and stops asking for frames; a forever-loop never does.
        for (var i = 0; i < 200; i++) Assert.True(bridge.Tick(0.05), $"still animating at frame {i}");
    }

    [Fact]
    public void RepeatForever_OscillatesOpacityDownToTheFloorAndBack()
    {
        var (bridge, host) = Host(new Pulsing(-1));

        var samples = new List<float>();
        for (var i = 0; i < 40; i++) { bridge.Tick(0.05); samples.Add(Alpha(host)); }

        // Fully opaque at the peak of the cycle, PulseFloor at the trough, and it comes back up
        // (autoreverse) rather than snapping — a yo-yo, not a sawtooth.
        Assert.Equal(1.0f, samples.Max(), 1);
        Assert.Equal(0.4f, samples.Min(), 1);
        var trough = samples.IndexOf(samples.Min());
        Assert.True(samples.Skip(trough).Max() > samples.Min() + 0.3, "should rise again after the trough");
    }

    [Fact]
    public void FiniteRepeat_SettlesBackToTheRestingValue()
    {
        var (bridge, host) = Host(new Pulsing(2));

        // Two autoreversing cycles at 0.3s each = 1.2s of there-and-back; run well past it.
        for (var i = 0; i < 200; i++) bridge.Tick(0.05);

        Assert.False(bridge.Tick(0.05), "a finite repeat must stop requesting frames");
        Assert.Equal(1.0f, Alpha(host), 1);
    }

    [Fact]
    public void NoRepeat_StillSettlesAsAOneShot()
    {
        var (bridge, _) = Host(new NotPulsing());
        for (var i = 0; i < 100; i++) bridge.Tick(0.05);
        Assert.False(bridge.Tick(0.05));
    }

    /// <summary>
    /// The composited opacity of the pulsing square, recovered from the painted surface. The view fills the
    /// canvas with the theme's red over the theme's background, so the green channel interpolates linearly
    /// between the two — normalising against both endpoints (measured, not hard-coded) inverts it back to
    /// an alpha without baking in either palette value.
    /// </summary>
    static float Alpha(SkiaImageHost host)
    {
        using var image = SKImage.FromEncodedData(host.RenderPng(W, H));
        using var bitmap = SKBitmap.FromImage(image);
        return (Backdrop - bitmap.GetPixel(W / 2, H / 2).Green) / (float)(Backdrop - Opaque);
    }

    static readonly int Backdrop = Sample(new Blank());
    static readonly int Opaque = Sample(new NotPulsing());

    static int Sample(View view)
    {
        var (_, host) = Host(view);
        using var image = SKImage.FromEncodedData(host.RenderPng(W, H));
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(W / 2, H / 2).Green;
    }

    static (SkiaBridge, SkiaImageHost) Host(View root)
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        host.RenderPng(W, H);
        return (bridge, host);
    }
}

file sealed class Pulsing(int count) : View
{
    public override View? Body =>
        new Rectangle().ForegroundColor(Color.Red).Frame(W, W)
            .Animation(Anim.Linear(0.3).Repeating(count, autoreverse: true), on: true);

    const int W = 200;
}

file sealed class NotPulsing : View
{
    public override View? Body =>
        new Rectangle().ForegroundColor(Color.Red).Frame(200, 200).Animation(Anim.Linear(0.3), on: true);
}

/// <summary>Bare canvas — the backdrop the pulsing square composites against.</summary>
file sealed class Blank : View
{
    public override View? Body => new VStack();
}
