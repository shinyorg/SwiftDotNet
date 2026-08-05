using System.Text;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Rendering;

using TColor = XenoAtom.Terminal.UI.Color;
using TStyle = XenoAtom.Terminal.UI.Style;

namespace SwiftDotNet;

/// <summary>How an image is turned into characters. See <see cref="TuiAsciiArt"/> for what each costs.</summary>
public enum TuiImageMode
{
    /// <summary>Pick from the terminal's colour support: block modes when it has colour, ramp when it doesn't.</summary>
    Auto,

    /// <summary>
    /// One <c>▀</c> per cell, foreground = upper half-pixel, background = lower. Doubles vertical
    /// resolution at no cost and keeps full colour — the best-looking option, and the default.
    /// </summary>
    HalfBlock,

    /// <summary>
    /// 2×2 sub-cell sampling onto the quadrant glyphs (U+2596–U+259F). Doubles horizontal resolution too,
    /// but each cell still has only two colours, so it trades colour accuracy for shape accuracy — better
    /// for line art and logos, worse for photographs.
    /// </summary>
    Quadrant,

    /// <summary>Luminance onto a character ramp. The only mode that survives a monochrome terminal.</summary>
    Ascii,
}

/// <summary>
/// Renders decoded pixels as terminal characters — the fallback that makes <c>Image</c> work on every
/// terminal, and the whole image story on terminals without a graphics protocol.
///
/// <para>Downsampling is a box average rather than nearest-neighbour: at these scales (an image is
/// typically squeezed into 20-60 columns) nearest-neighbour drops most of the source and aliases badly,
/// while a box filter keeps the image recognisable. Alpha is composited against the cell background
/// before quantising, since a cell cannot be partly transparent.</para>
/// </summary>
public static class TuiAsciiArt
{
    /// <summary>Darkest → lightest. Chosen for even perceived-density steps in a monospace font.</summary>
    public const string Ramp = " .:-=+*#%@";

    static readonly Rune UpperHalf = new(0x2580);   // ▀

    /// <summary>
    /// Quadrant glyphs indexed by a 4-bit mask of which sub-cells are foreground
    /// (bit 0 = top-left, 1 = top-right, 2 = bottom-left, 3 = bottom-right).
    /// </summary>
    static readonly char[] Quadrants =
    [
        ' ',   // ....  none
        '▘',   // TL
        '▝',   // TR
        '▀',   // TL TR
        '▖',   // BL
        '▌',   // TL BL
        '▞',   // TR BL
        '▛',   // TL TR BL
        '▗',   // BR
        '▚',   // TL BR
        '▐',   // TR BR
        '▜',   // TL TR BR
        '▄',   // BL BR
        '▙',   // TL BL BR
        '▟',   // TR BL BR
        '█',   // all
    ];

    /// <summary>
    /// Resolves <see cref="TuiImageMode.Auto"/> against what the terminal can actually do. A 16-colour
    /// terminal still gets block glyphs — they carry shape even when the palette flattens the colour —
    /// but a terminal with no colour at all only has luminance to work with, so it gets the ramp.
    /// </summary>
    public static TuiImageMode Resolve(TuiImageMode mode)
    {
        if (mode != TuiImageMode.Auto) return mode;
        try
        {
            return Terminal.Capabilities.ColorLevel == TerminalColorLevel.None
                ? TuiImageMode.Ascii
                : TuiImageMode.HalfBlock;
        }
        catch
        {
            // No terminal attached (tests, redirected output): the ramp is the safe answer.
            return TuiImageMode.Ascii;
        }
    }

    /// <summary>
    /// The cell size an image of <paramref name="pixels"/> wants, honouring any explicit
    /// <paramref name="cols"/>/<paramref name="rows"/> and correcting for the ~2:1 aspect of a terminal
    /// cell so a square image doesn't come out twice as tall as it is wide.
    /// </summary>
    public static (int Cols, int Rows) Fit(TuiPixels pixels, int? cols, int? rows, int defaultCols = 32)
    {
        // A cell is about twice as tall as it is wide, so the row count for a given column count is the
        // pixel aspect halved.
        if (cols is { } c && rows is { } r) return (Math.Max(1, c), Math.Max(1, r));
        if (cols is { } cw)
            return (Math.Max(1, cw), Math.Max(1, (int)Math.Round(cw * (double)pixels.Height / pixels.Width / 2)));
        if (rows is { } rh)
            return (Math.Max(1, (int)Math.Round(rh * 2 * (double)pixels.Width / pixels.Height)), Math.Max(1, rh));

        var width = Math.Min(defaultCols, Math.Max(1, pixels.Width));
        return (width, Math.Max(1, (int)Math.Round(width * (double)pixels.Height / pixels.Width / 2)));
    }

    /// <summary>
    /// Box-downsamples (or nearest-upsamples) to <paramref name="width"/> × <paramref name="height"/>,
    /// averaging in premultiplied space so transparent pixels don't drag their colour into the result.
    /// </summary>
    public static TuiPixels Resample(TuiPixels src, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var outBytes = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var y0 = (int)((long)y * src.Height / height);
            var y1 = Math.Max(y0 + 1, (int)((long)(y + 1) * src.Height / height));
            for (var x = 0; x < width; x++)
            {
                var x0 = (int)((long)x * src.Width / width);
                var x1 = Math.Max(x0 + 1, (int)((long)(x + 1) * src.Width / width));

                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = y0; sy < y1; sy++)
                for (var sx = x0; sx < x1; sx++)
                {
                    var (pr, pg, pb, pa) = src.At(sx, sy);
                    r += pr * pa; g += pg * pa; b += pb * pa; a += pa;
                    n++;
                }

                var o = (y * width + x) * 4;
                if (a == 0)
                {
                    outBytes[o] = outBytes[o + 1] = outBytes[o + 2] = outBytes[o + 3] = 0;
                    continue;
                }
                outBytes[o] = (byte)(r / a);
                outBytes[o + 1] = (byte)(g / a);
                outBytes[o + 2] = (byte)(b / a);
                outBytes[o + 3] = (byte)(a / n);
            }
        }
        return new TuiPixels(width, height, outBytes);
    }

    /// <summary>
    /// Paints <paramref name="pixels"/> into <paramref name="buffer"/> across <paramref name="cols"/> ×
    /// <paramref name="rows"/> cells starting at (<paramref name="left"/>, <paramref name="top"/>).
    /// <paramref name="background"/> is what alpha composites against.
    /// </summary>
    public static void Paint(CellBuffer buffer, TuiPixels pixels, TuiImageMode mode,
        int left, int top, int cols, int rows, TColor background)
    {
        switch (Resolve(mode))
        {
            case TuiImageMode.Quadrant: PaintQuadrant(buffer, pixels, left, top, cols, rows, background); break;
            case TuiImageMode.Ascii: PaintRamp(buffer, pixels, left, top, cols, rows, background); break;
            default: PaintHalfBlock(buffer, pixels, left, top, cols, rows, background); break;
        }
    }

    static void PaintHalfBlock(CellBuffer buffer, TuiPixels pixels,
        int left, int top, int cols, int rows, TColor background)
    {
        var grid = Resample(pixels, cols, rows * 2);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
        {
            var upper = Composite(grid.At(x, y * 2), background);
            var lower = Composite(grid.At(x, y * 2 + 1), background);
            buffer.SetCell(left + x, top + y, UpperHalf,
                TStyle.None.WithForeground(upper).WithBackground(lower));
        }
    }

    static void PaintQuadrant(CellBuffer buffer, TuiPixels pixels,
        int left, int top, int cols, int rows, TColor background)
    {
        var grid = Resample(pixels, cols * 2, rows * 2);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
        {
            Span<TColor> quad =
            [
                Composite(grid.At(x * 2, y * 2), background),
                Composite(grid.At(x * 2 + 1, y * 2), background),
                Composite(grid.At(x * 2, y * 2 + 1), background),
                Composite(grid.At(x * 2 + 1, y * 2 + 1), background),
            ];

            // Split the four sub-cells into a light and a dark group around their mean luminance: that is
            // the two-colour partition a single cell can actually represent, and the glyph is just which
            // sub-cells landed in the light group.
            Span<double> luma = [Luma(quad[0]), Luma(quad[1]), Luma(quad[2]), Luma(quad[3])];
            var mean = (luma[0] + luma[1] + luma[2] + luma[3]) / 4;

            var mask = 0;
            long fr = 0, fg = 0, fb = 0, fn = 0, br = 0, bg = 0, bb = 0, bn = 0;
            for (var i = 0; i < 4; i++)
            {
                if (luma[i] >= mean)
                {
                    mask |= 1 << i;
                    fr += quad[i].R; fg += quad[i].G; fb += quad[i].B; fn++;
                }
                else
                {
                    br += quad[i].R; bg += quad[i].G; bb += quad[i].B; bn++;
                }
            }

            var fore = fn > 0 ? TColor.Rgb((byte)(fr / fn), (byte)(fg / fn), (byte)(fb / fn)) : background;
            var back = bn > 0 ? TColor.Rgb((byte)(br / bn), (byte)(bg / bn), (byte)(bb / bn)) : fore;
            buffer.SetCell(left + x, top + y, new Rune(Quadrants[mask]),
                TStyle.None.WithForeground(fore).WithBackground(back));
        }
    }

    static void PaintRamp(CellBuffer buffer, TuiPixels pixels,
        int left, int top, int cols, int rows, TColor background)
    {
        var grid = Resample(pixels, cols, rows);
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < cols; x++)
        {
            var color = Composite(grid.At(x, y), background);
            var index = (int)Math.Round(Luma(color) / 255 * (Ramp.Length - 1));
            buffer.SetCell(left + x, top + y, new Rune(Ramp[Math.Clamp(index, 0, Ramp.Length - 1)]),
                TStyle.None.WithForeground(color));
        }
    }

    /// <summary>Flattens a straight-alpha pixel onto <paramref name="background"/>.</summary>
    static TColor Composite((byte R, byte G, byte B, byte A) pixel, TColor background)
    {
        if (pixel.A == 255) return TColor.Rgb(pixel.R, pixel.G, pixel.B);
        var a = pixel.A / 255.0;
        return TColor.Rgb(
            (byte)(pixel.R * a + background.R * (1 - a)),
            (byte)(pixel.G * a + background.G * (1 - a)),
            (byte)(pixel.B * a + background.B * (1 - a)));
    }

    /// <summary>Rec. 601 luma — the perceptual weighting that makes a ramp read correctly.</summary>
    static double Luma(TColor c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
}
