using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Keyframe timelines on the Graphics engine (shared by Skia, WebGPU and the TUI). Unlike the canned
/// <c>.Repeating()</c> pulse in <see cref="SkiaRepeatAnimationTests"/>, a timeline carries real per-property
/// values on the wire, so these assert the painted result — the sampled opacity, and a height track
/// actually driving layout.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaKeyframeTests
{
    const int W = 200, H = 200;

    [Fact]
    public void RepeatingOpacityTrack_OscillatesToTheTracksOwnFloor_NotTheCannedOne()
    {
        // The point of keyframes: the trough is 0.2 because the track says so, where `.Repeating()` would
        // have pulsed to the hardcoded 0.4 no matter what.
        var (bridge, host) = Host(new Fading());

        var samples = new List<float>();
        for (var i = 0; i < 40; i++) { bridge.Tick(0.05); samples.Add(Alpha(host)); }

        Assert.Equal(1.0f, samples.Max(), 1);
        Assert.Equal(0.2f, samples.Min(), 1);
    }

    [Fact]
    public void RepeatForever_KeepsTheClockRunning()
    {
        var (bridge, _) = Host(new Fading());
        for (var i = 0; i < 200; i++) Assert.True(bridge.Tick(0.05), $"still animating at frame {i}");
    }

    [Fact]
    public void PlayOnce_SettlesOnTheFinalStopAndStopsRequestingFrames()
    {
        var (bridge, host) = Host(new FadingOnce());

        for (var i = 0; i < 100; i++) bridge.Tick(0.05);

        Assert.False(bridge.Tick(0.05), "a play-once timeline must stop requesting frames");
        Assert.Equal(0.5f, Alpha(host), 1);   // the last stop, held
    }

    [Fact]
    public void HeightTrack_DrivesLayoutNotJustPaint()
    {
        var (bridge, host) = Host(new Growing());

        // At t=0 the track holds 40pt; after the timeline runs out it holds 160pt.
        Assert.InRange(FilledRows(host), 38, 42);

        for (var i = 0; i < 100; i++) bridge.Tick(0.05);
        Assert.InRange(FilledRows(host), 158, 162);
    }

    [Fact]
    public void TrackOverridesTheStaticModifierItDrives()
    {
        // `.Opacity(1)` is on the view, but the opacity track owns the property while it plays.
        var (bridge, host) = Host(new FadingOnce());
        bridge.Tick(0.5);   // half-way through a 1s track running 1 → 0.5
        Assert.Equal(0.75f, Alpha(host), 1);
    }

    /// <summary>Height of the painted square, in scanlines, measured down the middle of the canvas.</summary>
    static int FilledRows(SkiaImageHost host)
    {
        using var image = SKImage.FromEncodedData(host.RenderPng(W, H));
        using var bitmap = SKBitmap.FromImage(image);
        var rows = 0;
        for (var y = 0; y < H; y++)
            if (Math.Abs(bitmap.GetPixel(W / 2, y).Green - Backdrop) > 20) rows++;
        return rows;
    }

    /// <summary>
    /// The composited opacity of the square, recovered from the painted surface — the same inversion
    /// <see cref="SkiaRepeatAnimationTests"/> uses: the view fills the canvas with the theme's red over the
    /// theme's background, so the green channel interpolates linearly between the two.
    /// </summary>
    static float Alpha(SkiaImageHost host)
    {
        using var image = SKImage.FromEncodedData(host.RenderPng(W, H));
        using var bitmap = SKBitmap.FromImage(image);
        return (Backdrop - bitmap.GetPixel(W / 2, H / 2).Green) / (float)(Backdrop - Opaque);
    }

    static readonly int Backdrop = Sample(new KeyframeBlank());
    static readonly int Opaque = Sample(new KeyframeSolid());

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

/// <summary>A forever-looping opacity track whose trough (0.2) differs from the canned pulse floor (0.4).</summary>
file sealed class Fading : View
{
    public override View? Body =>
        new Rectangle().ForegroundColor(Color.Red).Frame(200, 200)
            .Keyframes(k => k
                .Track(Prop.Opacity, t => t.At(0, 1).At(1, 0.2))
                .Duration(0.5)
                .Curve(Anim.Linear())
                .Repeating(autoreverse: true));
}

/// <summary>A one-shot 1 → 0.5 opacity track over a second, on a view that also sets a static opacity.</summary>
file sealed class FadingOnce : View
{
    public override View? Body =>
        new Rectangle().ForegroundColor(Color.Red).Frame(200, 200).Opacity(1)
            .Keyframes(k => k
                .Track(Prop.Opacity, t => t.At(0, 1).At(1, 0.5))
                .Duration(1)
                .Curve(Anim.Linear()));
}

/// <summary>A height track: 40pt → 160pt, which has to move layout rather than just the paint transform.</summary>
file sealed class Growing : View
{
    // Nested in a stack on purpose: the root node is arranged to fill the canvas, so a height track on the
    // root would be overridden by the host's own sizing and prove nothing.
    public override View? Body =>
        new VStack(
            new Rectangle().ForegroundColor(Color.Red).Frame(200, 200)
                .Keyframes(k => k
                    .Track(Prop.Height, t => t.At(0, 40).At(1, 160))
                    .Duration(1)
                    .Curve(Anim.Linear())));
}

file sealed class KeyframeSolid : View
{
    public override View? Body => new Rectangle().ForegroundColor(Color.Red).Frame(200, 200);
}

file sealed class KeyframeBlank : View
{
    public override View? Body => new VStack();
}
