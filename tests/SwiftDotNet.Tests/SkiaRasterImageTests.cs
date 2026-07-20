using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Skia used to measure every <c>Image</c> as an SF-Symbol glyph, so a raster image with no explicit
/// <c>.Frame</c> collapsed to nothing — which is exactly the shape the Controls library's
/// <c>ImageViewer</c> builds (a full-screen image inside a ZStack). Raster images are now greedy, filling
/// the space offered like a shape does, while SF Symbols keep their glyph metrics.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaRasterImageTests
{
    const int W = 400, H = 800;

    [Fact]
    public void UnframedRasterImage_FillsTheSpaceOffered()
    {
        var bridge = Render(new UnframedRaster());
        Assert.True(bridge.TryGetFrame("0.0", out var frame));
        Assert.True(frame.Width > W / 2, $"raster image collapsed to {frame.Width}px wide");
        Assert.True(frame.Height > H / 2, $"raster image collapsed to {frame.Height}px tall");
    }

    [Fact]
    public void FramedRasterImage_StillHonorsItsFrame()
    {
        var bridge = Render(new FramedRaster());
        Assert.True(bridge.TryGetFrame("0.0", out var frame));
        Assert.Equal(120, frame.Width, 1);
        Assert.Equal(80, frame.Height, 1);
    }

    [Fact]
    public void SystemImage_KeepsGlyphMetrics_NotGreedy()
    {
        var bridge = Render(new SystemIcon());
        Assert.True(bridge.TryGetFrame("0.0", out var frame));
        Assert.True(frame.Width < W / 2, $"SF Symbol should measure as a glyph, got {frame.Width}px");
    }

    static SkiaBridge Render(View root)
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        host.RenderPng(W, H);
        return bridge;
    }
}

// A 1x1 PNG — enough to exercise the raster path without shipping a fixture file.
file static class Png
{
    public const string OnePixel =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
}

file sealed class UnframedRaster : View
{
    public override View? Body => new ZStack(Image.FromBytesBase64(Png.OnePixel));
}

file sealed class FramedRaster : View
{
    public override View? Body => new ZStack(Image.FromBytesBase64(Png.OnePixel).Frame(120, 80));
}

file sealed class SystemIcon : View
{
    public override View? Body => new ZStack(Image.System("star.fill"));
}
