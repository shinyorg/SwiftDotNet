using System.Globalization;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Styling;

// SwiftDotNet's own DSL already owns `Color`, `Theme`, `Brush` and `State<T>`, and Terminal.UI has types
// with the same names. Alias the terminal ones with a T-prefix rather than shortening the DSL side —
// this file is the only place the two vocabularies meet, so the noise stays contained here.
using TColor = XenoAtom.Terminal.UI.Color;

namespace SwiftDotNet;

/// <summary>
/// Maps SwiftDotNet tokens and modifiers onto XenoAtom.Terminal.UI values. The terminal analogue of
/// <c>GtkStyle</c> / <c>WebStyle</c>, with one structural difference: a terminal's unit is the
/// <b>character cell</b>, not the pixel, so every geometric modifier passes through
/// <see cref="Cols"/> / <see cref="Rows"/> instead of being emitted verbatim.
/// </summary>
public static class TuiStyle
{
    /// <summary>
    /// Pixels per character cell, used to convert the DSL's pixel geometry (<c>.Frame</c>, <c>.Padding</c>)
    /// into cells. Defaults approximate a 14pt monospace font. Assign these before the first render to
    /// tune how dense a layout the terminal gets — a smaller divisor means more cells for the same
    /// <c>.Frame(width:)</c>.
    /// </summary>
    public static double CellWidthPx { get; set; } = 8;

    /// <inheritdoc cref="CellWidthPx"/>
    public static double CellHeightPx { get; set; } = 16;

    /// <summary>Horizontal pixels → columns, never rounding a non-zero size away to nothing.</summary>
    public static int Cols(double px) => px <= 0 ? 0 : Math.Max(1, (int)Math.Round(px / CellWidthPx));

    /// <summary>Vertical pixels → rows, never rounding a non-zero size away to nothing.</summary>
    public static int Rows(double px) => px <= 0 ? 0 : Math.Max(1, (int)Math.Round(px / CellHeightPx));

    // ---- colors --------------------------------------------------------------

    /// <summary>
    /// A DSL colour token (a semantic name or <c>#RRGGBB</c>) as a terminal <see cref="TColor"/>.
    /// <c>null</c> means "leave the theme's colour alone" — the same contract as <c>GtkStyle.Hex</c>,
    /// which is why <c>primary</c> maps to null rather than to black: the terminal's own foreground is
    /// already the right answer, and hard-coding one would break light-background terminals.
    /// </summary>
    public static TColor? Parse(string? token) => token switch
    {
        null or "" => null,
        "primary" => null,
        "secondary" => TColor.Rgb(0x8E, 0x8E, 0x93),
        "red" => TColor.Rgb(0xFF, 0x3B, 0x30),
        "green" => TColor.Rgb(0x34, 0xC7, 0x59),
        "blue" => TColor.Rgb(0x00, 0x7A, 0xFF),
        "accentColor" => TColor.Rgb(0x7C, 0x4D, 0xFF),
        "white" => TColor.Rgb(0xFF, 0xFF, 0xFF),
        "black" => TColor.Rgb(0, 0, 0),
        "gray" or "grey" => TColor.Rgb(0x8E, 0x8E, 0x93),
        "orange" => TColor.Rgb(0xFF, 0x95, 0x00),
        "yellow" => TColor.Rgb(0xFF, 0xCC, 0x00),
        "purple" => TColor.Rgb(0xAF, 0x52, 0xDE),
        "pink" => TColor.Rgb(0xFF, 0x2D, 0x55),
        "clear" => null,
        _ => Hex(token),
    };

    /// <summary>Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#RRGGBBAA</c>; null for anything else.</summary>
    public static TColor? Hex(string token)
    {
        if (token.Length < 4 || token[0] != '#') return null;
        var body = token[1..];
        if (body.Length == 3)
            body = $"{body[0]}{body[0]}{body[1]}{body[1]}{body[2]}{body[2]}";
        if (body.Length is not (6 or 8)) return null;
        if (!byte.TryParse(body.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(body.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(body.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return null;
        if (body.Length == 6) return TColor.Rgb(r, g, b);
        return byte.TryParse(body.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            ? TColor.RgbA(r, g, b, a)
            : TColor.Rgb(r, g, b);
    }

    /// <summary>
    /// The first stop of a <c>Brush</c> wire string (<c>linear:angle:color@loc;color@loc</c> or
    /// <c>radial:color@loc;…</c>). A cell has one background colour, so a gradient collapses to its first
    /// stop rather than being dropped — see the gradient row in <c>docs/backends/tui.md</c>.
    /// </summary>
    public static TColor? GradientStart(string spec)
    {
        var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
        // linear carries an angle between the kind and the stops; radial does not.
        var stops = parts.Length switch { >= 3 => parts[2], 2 => parts[1], _ => null };
        if (stops is null) return null;
        var first = stops.Split(';', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return null;
        var at = first.LastIndexOf('@');
        return Parse(at < 0 ? first : first[..at]);
    }

    /// <summary>
    /// Flattens <paramref name="color"/> toward <paramref name="over"/> by <paramref name="opacity"/>.
    /// A terminal cell has no alpha channel, so <c>.Opacity</c> has to be baked into the colour itself;
    /// this is what makes a faded overlay read as faded instead of as fully opaque.
    /// </summary>
    public static TColor Blend(TColor color, TColor over, double opacity)
        => TColor.Mix(over, color, (float)Math.Clamp(opacity, 0, 1), ColorMixSpace.Srgb);

    // ---- alignment -----------------------------------------------------------

    public static Align AlignOf(string? token) => token switch
    {
        "leading" or "topLeading" or "bottomLeading" => Align.Start,
        "trailing" or "topTrailing" or "bottomTrailing" => Align.End,
        _ => Align.Center,
    };

    public static Align VAlignOf(string? token) => token switch
    {
        "top" or "topLeading" or "topTrailing" => Align.Start,
        "bottom" or "bottomLeading" or "bottomTrailing" => Align.End,
        _ => Align.Center,
    };

    public static TextAlignment TextAlignOf(string? token) => token switch
    {
        "leading" or "topLeading" or "bottomLeading" => TextAlignment.Left,
        "trailing" or "topTrailing" or "bottomTrailing" => TextAlignment.Right,
        _ => TextAlignment.Center,
    };

    /// <summary>
    /// Applies an <c>Alignment.Token()</c> to a visual, constraining <em>only the axes the token names</em>
    /// — the same rule the GTK backend follows, and what makes a ZStack overlay pinned to <c>bottom</c>
    /// stay full-width instead of shrinking to its content.
    /// </summary>
    public static void ApplyAlignment(Visual v, string? token)
    {
        switch (token)
        {
            case "leading" or "topLeading" or "bottomLeading": v.HorizontalAlignment = Align.Start; break;
            case "trailing" or "topTrailing" or "bottomTrailing": v.HorizontalAlignment = Align.End; break;
        }
        switch (token)
        {
            case "top" or "topLeading" or "topTrailing": v.VerticalAlignment = Align.Start; break;
            case "bottom" or "bottomLeading" or "bottomTrailing": v.VerticalAlignment = Align.End; break;
        }
    }

    // ---- text ----------------------------------------------------------------

    /// <summary>
    /// A <c>.Font</c> token as terminal text attributes. A terminal has exactly one glyph size, so the
    /// size half of the token is unrepresentable and the <em>emphasis</em> is what survives: headings go
    /// bold, captions dim. Node types that want real large text use <c>TextFiglet</c> instead.
    /// </summary>
    public static TextStyle Font(string? token) => token switch
    {
        "largeTitle" or "title" or "title2" or "title3" or "headline" => TextStyle.Bold,
        "subheadline" => TextStyle.Italic,
        "caption" or "caption2" or "footnote" => TextStyle.Dim,
        _ => TextStyle.None,
    };

    /// <summary>True when the token names a font size big enough to be worth rendering as FIGlet banner text.</summary>
    public static bool IsBannerFont(string? token) => token is "largeTitle";

    /// <summary>SF Symbol name → a glyph a terminal can actually draw. Mirrors <c>GtkStyle.Emoji</c>.</summary>
    public static string Glyph(string name) => name switch
    {
        "star.fill" or "star" => "★",
        "heart.fill" or "heart" => "♥",
        "bell" or "bell.fill" => "🔔",
        "checkmark" => "✓",
        "slider.horizontal.3" => "⋮",
        "square.grid.2x2" => "▦",
        "rectangle.stack" => "▤",
        "list.bullet" => "☰",
        "arrow.forward.circle" => "→",
        "plus" => "+",
        "minus" => "-",
        "camera" => "📷",
        "mic" => "🎤",
        "doc" => "📄",
        "folder" => "📁",
        "photo" => "🖼",
        "music" => "♪",
        "calendar" => "📅",
        "gauge" => "◔",
        "bubble.left.and.bubble.right" => "💬",
        "square.on.square" => "❐",
        "tablecells" => "▦",
        "square.stack" => "▤",
        "textformat" => "🔤",
        "hand.tap" => "☝",
        "wand.and.stars" => "✨",
        "paintbrush" => "🖌",
        "globe" => "🌐",
        "chevron.down.circle" => "⌄",
        "chevron.right" => "›",
        "rectangle.portrait" => "▭",
        "rectangle.3.offgrid" => "▤",
        "trash" => "🗑",
        "xmark" => "✕",
        _ => "•",
    };

    // ---- modifier helpers ----------------------------------------------------

    /// <summary>The <c>padding</c> modifier's four edges as a cell <see cref="Thickness"/>.</summary>
    public static Thickness PaddingOf(Dictionary<string, object?> m) => new(
        Cols(Num(m, "leading")), Rows(Num(m, "top")),
        Cols(Num(m, "trailing")), Rows(Num(m, "bottom")));

    /// <summary>The line glyphs a <c>border</c> modifier's width implies: a thick border draws heavy lines.</summary>
    public static LineGlyphs BorderGlyphs(double width) => width >= 2 ? LineGlyphs.Heavy : LineGlyphs.Rounded;

    public static double Num(Dictionary<string, object?> m, string key, double fallback = 0)
        => m.TryGetValue(key, out var v) && v is double d ? d : fallback;

    public static string Str(Dictionary<string, object?> m, string key)
        => m.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
}
