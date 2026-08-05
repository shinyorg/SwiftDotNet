using SwiftDotNet;
using Xunit;
using XenoAtom.Terminal.UI.Rendering;

using TColor = XenoAtom.Terminal.UI.Color;

namespace SwiftDotNet.Tests;

/// <summary>
/// The image → character-art renderer. Assertions read the real <see cref="CellBuffer"/> back as markup,
/// so these check what a terminal would actually be sent — glyph and colour per cell — rather than an
/// intermediate the renderer happens to compute.
/// </summary>
public class TuiAsciiArtTests
{
    [Fact]
    public void Fit_CorrectsForTheTwoToOneCellAspect()
    {
        // A square image must not come out square in cells: a cell is about twice as tall as it is wide,
        // so 40 columns of a 100×100 image is 20 rows, not 40.
        var square = Solid(100, 100, 255, 255, 255);
        Assert.Equal((40, 20), TuiAsciiArt.Fit(square, cols: 40, rows: null));

        // A wide image keeps its proportions through the same correction.
        var wide = Solid(200, 100, 255, 255, 255);
        Assert.Equal((40, 10), TuiAsciiArt.Fit(wide, cols: 40, rows: null));
    }

    [Fact]
    public void Fit_HonoursAnExplicitSizeVerbatim()
        => Assert.Equal((12, 7), TuiAsciiArt.Fit(Solid(100, 100, 0, 0, 0), cols: 12, rows: 7));

    [Fact]
    public void Resample_BoxAveragesRatherThanDroppingPixels()
    {
        // Four quadrants of one colour each, collapsed to a single cell. Nearest-neighbour would return
        // one quadrant's colour; a box filter returns their mean, which is what keeps a downscaled image
        // recognisable at terminal sizes.
        var source = Quadrants(
            (255, 0, 0, 255), (0, 255, 0, 255),
            (0, 0, 255, 255), (255, 255, 255, 255));

        var one = TuiAsciiArt.Resample(source, 1, 1);
        var (r, g, b, a) = one.At(0, 0);
        Assert.Equal(127, r, tolerance: 2);
        Assert.Equal(127, g, tolerance: 2);
        Assert.Equal(127, b, tolerance: 2);
        Assert.Equal(255, a);
    }

    [Fact]
    public void Resample_IgnoresTheColourOfFullyTransparentPixels()
    {
        // Premultiplied averaging matters here: a transparent magenta pixel must not tint its neighbour.
        var source = Quadrants(
            (0, 0, 0, 255), (255, 0, 255, 0),
            (0, 0, 0, 255), (255, 0, 255, 0));

        var (r, _, b, a) = TuiAsciiArt.Resample(source, 1, 1).At(0, 0);
        Assert.Equal(0, r);
        Assert.Equal(0, b);
        Assert.Equal(127, a, tolerance: 2);   // half the pixels contributed alpha
    }

    [Fact]
    public void HalfBlock_PutsTheUpperPixelInForegroundAndTheLowerInBackground()
    {
        // Red on top, blue underneath: one cell, one ▀, two colours — the trick that doubles vertical
        // resolution for free.
        var image = Rows((255, 0, 0, 255), (0, 0, 255, 255));
        var buffer = new CellBuffer(1, 1, null!);

        TuiAsciiArt.Paint(buffer, image, TuiImageMode.HalfBlock, 0, 0, 1, 1, TColor.Rgb(0, 0, 0));

        Assert.Equal("[#ff0000 on #0000ff]▀[/]", Markup(buffer));
    }

    [Fact]
    public void Ascii_MapsLuminanceOntoTheRamp()
    {
        var buffer = new CellBuffer(2, 1, null!);
        var image = Columns((0, 0, 0, 255), (255, 255, 255, 255));

        TuiAsciiArt.Paint(buffer, image, TuiImageMode.Ascii, 0, 0, 2, 1, TColor.Rgb(0, 0, 0));

        var markup = Markup(buffer);
        Assert.Contains(TuiAsciiArt.Ramp[0], markup);     // black → the lightest-density glyph
        Assert.Contains(TuiAsciiArt.Ramp[^1], markup);    // white → the densest
    }

    [Fact]
    public void Quadrant_SplitsACellIntoItsTwoDominantColours()
    {
        // Left half black, right half white, in one cell: the 2×2 sample partitions cleanly and the glyph
        // is the right-half block.
        var image = Quadrants(
            (0, 0, 0, 255), (255, 255, 255, 255),
            (0, 0, 0, 255), (255, 255, 255, 255));
        var buffer = new CellBuffer(1, 1, null!);

        TuiAsciiArt.Paint(buffer, image, TuiImageMode.Quadrant, 0, 0, 1, 1, TColor.Rgb(0, 0, 0));

        Assert.Equal("[#ffffff on #000000]▐[/]", Markup(buffer));
    }

    [Fact]
    public void Transparency_CompositesAgainstTheGivenBackground()
    {
        // A half-transparent white over a red background reads as pink, not as white and not as red.
        var image = Rows((255, 255, 255, 128), (255, 255, 255, 128));
        var buffer = new CellBuffer(1, 1, null!);

        TuiAsciiArt.Paint(buffer, image, TuiImageMode.HalfBlock, 0, 0, 1, 1, TColor.Rgb(255, 0, 0));

        // 50% white over pure red: red stays saturated, green and blue come halfway up from zero.
        Assert.Equal("[#ff8080 on #ff8080]▀[/]", Markup(buffer));
    }

    [Fact]
    public void Resolve_LeavesAnExplicitModeAlone()
    {
        Assert.Equal(TuiImageMode.Quadrant, TuiAsciiArt.Resolve(TuiImageMode.Quadrant));
        Assert.Equal(TuiImageMode.Ascii, TuiAsciiArt.Resolve(TuiImageMode.Ascii));
        Assert.Equal(TuiImageMode.HalfBlock, TuiAsciiArt.Resolve(TuiImageMode.HalfBlock));
    }

    // ---- fixtures ------------------------------------------------------------

    static string Markup(CellBuffer buffer) => string.Concat(buffer.ToMarkupLines()).TrimEnd();

    static TuiPixels Solid(int width, int height, byte r, byte g, byte b)
    {
        var rgba = new byte[width * height * 4];
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = 255;
        }
        return new TuiPixels(width, height, rgba);
    }

    /// <summary>A 2×2 image, one colour per quadrant, reading left-to-right then top-to-bottom.</summary>
    static TuiPixels Quadrants(params (byte R, byte G, byte B, byte A)[] quads)
        => new(2, 2, quads.SelectMany(q => new[] { q.R, q.G, q.B, q.A }).ToArray());

    /// <summary>A 1×2 image: one colour above the other.</summary>
    static TuiPixels Rows(params (byte R, byte G, byte B, byte A)[] rows)
        => new(1, rows.Length, rows.SelectMany(c => new[] { c.R, c.G, c.B, c.A }).ToArray());

    /// <summary>A 2×1 image: one colour beside the other.</summary>
    static TuiPixels Columns(params (byte R, byte G, byte B, byte A)[] cols)
        => new(cols.Length, 1, cols.SelectMany(c => new[] { c.R, c.G, c.B, c.A }).ToArray());
}
