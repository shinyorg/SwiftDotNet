using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The hand-rolled PNG decoder that lets the terminal backend show images without taking a SkiaSharp
/// dependency. Fixtures are encoded here rather than committed as binary files, so each test states
/// exactly which PNG feature it exercises — colour type, bit depth, scanline filter.
/// </summary>
public class TuiPngDecoderTests
{
    [Fact]
    public void DecodesTruecolorWithAlpha()
    {
        // 2×2 RGBA: red, green / blue, transparent.
        var png = TuiTestPng.Encode(2, 2, colorType: 6, bitDepth: 8,
        [
            255, 0, 0, 255, 0, 255, 0, 255,
            0, 0, 255, 255, 0, 0, 0, 0,
        ]);

        var pixels = Decode(png);
        Assert.Equal(2, pixels.Width);
        Assert.Equal(2, pixels.Height);
        Assert.Equal((255, 0, 0, 255), pixels.At(0, 0));
        Assert.Equal((0, 255, 0, 255), pixels.At(1, 0));
        Assert.Equal((0, 0, 255, 255), pixels.At(0, 1));
        Assert.Equal((0, 0, 0, 0), pixels.At(1, 1));
    }

    [Fact]
    public void DecodesTruecolorWithoutAlpha_AsFullyOpaque()
    {
        var png = TuiTestPng.Encode(2, 1, colorType: 2, bitDepth: 8, [10, 20, 30, 40, 50, 60]);

        var pixels = Decode(png);
        Assert.Equal((10, 20, 30, 255), pixels.At(0, 0));
        Assert.Equal((40, 50, 60, 255), pixels.At(1, 0));
    }

    [Fact]
    public void DecodesGrayscale_AsEqualRgbChannels()
    {
        var png = TuiTestPng.Encode(3, 1, colorType: 0, bitDepth: 8, [0, 128, 255]);

        var pixels = Decode(png);
        Assert.Equal((0, 0, 0, 255), pixels.At(0, 0));
        Assert.Equal((128, 128, 128, 255), pixels.At(1, 0));
        Assert.Equal((255, 255, 255, 255), pixels.At(2, 0));
    }

    [Fact]
    public void ReconstructsUpAndSubFilteredScanlines()
    {
        // Rows 2 and 3 use the Up (2) and Sub (1) filters, so a broken unfilter shows up as wrong colour
        // rather than as a decode failure — which is exactly the bug class worth pinning down.
        var png = TuiTestPng.Encode(2, 3, colorType: 2, bitDepth: 8,
        [
            10, 20, 30, 40, 50, 60,
            10, 20, 30, 40, 50, 60,
            10, 20, 30, 40, 50, 60,
        ], filters: [0, 2, 1]);

        var pixels = Decode(png);
        for (var y = 0; y < 3; y++)
        {
            Assert.Equal((10, 20, 30, 255), pixels.At(0, y));
            Assert.Equal((40, 50, 60, 255), pixels.At(1, y));
        }
    }

    [Fact]
    public void RejectsNonPngPayloads()
    {
        Assert.False(new TuiPngDecoder().TryDecode("not a png at all"u8, out _));
    }

    [Fact]
    public void RejectsInterlacedPngRatherThanDecodingItWrong()
    {
        // Adam7 is deliberately unsupported: the caller falls back to alt text, which is honest, whereas
        // decoding the first pass as if it were the whole image would render visible garbage.
        var png = TuiTestPng.Encode(2, 2, colorType: 6, bitDepth: 8,
            [255, 0, 0, 255, 0, 255, 0, 255, 0, 0, 255, 255, 0, 0, 0, 0], interlace: 1);

        Assert.False(new TuiPngDecoder().TryDecode(png, out _));
    }

    static TuiPixels Decode(byte[] png)
    {
        Assert.True(new TuiPngDecoder().TryDecode(png, out var pixels));
        return Assert.IsType<TuiPixels>(pixels);
    }
}
