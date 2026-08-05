using System.Text.RegularExpressions;
using SwiftDotNet;
using Xunit;
using XenoAtom.Terminal.UI.Rendering;

using TTheme = XenoAtom.Terminal.UI.Styling.Theme;

namespace SwiftDotNet.Tests;

/// <summary>
/// An <c>Image</c> node end to end on the terminal backend: DSL → PNG decode → character art → real
/// cells. <see cref="VisualSnapshotRenderer"/> runs the whole Terminal.UI layout and render pipeline
/// without a TTY, so these assert what a terminal would actually receive.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class TuiImageNodeTests
{
    [Fact]
    public void ImageFromBytes_RendersAsHalfBlockArt()
    {
        using var _ = ImageMode(TuiImageMode.HalfBlock);
        var markup = RenderMarkup(new ImageView(Checkerboard(8, 8)), width: 10, height: 6);

        // Half-block draws one ▀ per cell with the upper pixel as foreground and the lower as background.
        Assert.Contains('▀', markup);
        Assert.Matches(new Regex(@"\[#[0-9a-f]{6} on #[0-9a-f]{6}\]"), markup);
    }

    [Fact]
    public void ImageFromBytes_RendersAsRampArtWhenAsciiModeIsForced()
    {
        using var _ = ImageMode(TuiImageMode.Ascii);
        // A left-to-right black→white ramp, so the rendered glyphs must span the character ramp too. A
        // checkerboard would not: box-averaging collapses it to uniform mid-grey, which is correct but
        // proves nothing about the mapping.
        var markup = RenderMarkup(new ImageView(HorizontalGradient(32, 8)), width: 12, height: 6);

        Assert.DoesNotContain('▀', markup);
        Assert.Contains(TuiAsciiArt.Ramp[0], markup);      // the black end
        Assert.Contains(TuiAsciiArt.Ramp[^1], markup);     // the white end
    }

    [Fact]
    public void ImagePreservesAspectRatio_CorrectedForTheCellShape()
    {
        using var _ = ImageMode(TuiImageMode.HalfBlock);
        // A 2:1 image in a 20-column slot wants 20 × (1/2) / 2 = 5 rows.
        var lines = RenderLines(new ImageView(Checkerboard(40, 20)), width: 20, height: 12);
        var painted = lines.Count(l => l.Contains('▀'));
        Assert.Equal(5, painted);
    }

    [Fact]
    public void UndecodableBytes_RenderNothingRatherThanThrowing()
    {
        // A raster image that cannot be decoded stays empty so the caller's own placeholder shows through
        // — the same rule the GTK backend follows. It must not throw and must not print a marker.
        using var _ = ImageMode(TuiImageMode.HalfBlock);
        var markup = RenderMarkup(new ImageView("not a png"u8.ToArray()), width: 10, height: 4);
        Assert.DoesNotContain('▀', markup);
    }

    [Fact]
    public void SystemSymbol_FallsBackToAGlyph()
    {
        // No raster source at all, but an SF Symbol was named: show the mapped glyph rather than nothing.
        var markup = RenderMarkup(new SymbolView(), width: 10, height: 3);
        Assert.Contains('★', markup);
    }

    // ---- helpers -------------------------------------------------------------

    static string RenderMarkup(View view, int width, int height)
        => string.Join('\n', RenderLines(view, width, height));

    static IReadOnlyList<string> RenderLines(View view, int width, int height)
    {
        var bridge = new TuiBridge();
        SwiftApp.Run(view, bridge);
        return VisualSnapshotRenderer.Render(bridge.Host, width, height, TTheme.Default).ToMarkupLines();
    }

    /// <summary>Pins the art mode for one test, restoring the global default afterwards.</summary>
    static IDisposable ImageMode(TuiImageMode mode)
    {
        var previous = TuiImageOptions.Mode;
        TuiImageOptions.Mode = mode;
        return new Restore(() => TuiImageOptions.Mode = previous);
    }

    sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    /// <summary>An 8-bit RGB PNG that ramps from black on the left to white on the right.</summary>
    static byte[] HorizontalGradient(int width, int height)
    {
        var samples = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = (byte)(x * 255 / (width - 1));
            var i = (y * width + x) * 3;
            samples[i] = samples[i + 1] = samples[i + 2] = value;
        }
        return TuiTestPng.Encode(width, height, colorType: 2, bitDepth: 8, samples);
    }

    /// <summary>An 8-bit RGB PNG of alternating black and white pixels.</summary>
    static byte[] Checkerboard(int width, int height)
    {
        var samples = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var value = (x + y) % 2 == 0 ? (byte)255 : (byte)0;
            var i = (y * width + x) * 3;
            samples[i] = samples[i + 1] = samples[i + 2] = value;
        }
        return TuiTestPng.Encode(width, height, colorType: 2, bitDepth: 8, samples);
    }
}

file sealed class ImageView(byte[] png) : View
{
    public override View Body => new VStack(Image.FromBytes(png));
}

file sealed class SymbolView : View
{
    public override View Body => new VStack(Image.System("star.fill"));
}
