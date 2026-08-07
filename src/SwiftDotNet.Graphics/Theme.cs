namespace SwiftDotNet.Graphics;

/// <summary>
/// Maps SwiftDotNet's semantic tokens (colours, font sizes, SF-Symbol-ish icon names) to concrete values.
/// </summary>
/// <remarks>
/// Because a self-drawing backend paints every pixel itself there is no OS theme to defer to — this class
/// <em>is</em> the theme, resolving tokens against a light/dark flag the host supplies. It lives in the
/// shared layer rather than per-rasterizer so Skia, WebGPU and Unity are guaranteed to agree on what
/// "secondary" or "headline" means; only the font <em>object</em> differs, and that comes from
/// <see cref="IFontProvider"/>.
/// </remarks>
public static class Theme
{
    /// <summary>System background (window fill).</summary>
    public static Color Background(bool dark) => dark ? new Color(0x1C, 0x1C, 0x1E) : new Color(0xFF, 0xFF, 0xFF);

    /// <summary>A subtle filled surface (grouped rows, plain-button chrome) — SwiftUI systemGray6-ish.</summary>
    public static Color Surface(bool dark) => dark ? new Color(0x2C, 0x2C, 0x2E) : new Color(0xF2, 0xF2, 0xF7);

    public static Color Separator(bool dark) => dark ? new Color(0x38, 0x38, 0x3A) : new Color(0xC6, 0xC6, 0xC8);

    /// <summary>The muted grey used for secondary labels and placeholder text.</summary>
    public static Color Secondary => new(0x8E, 0x8E, 0x93);

    public static Color Accent => new(0x7C, 0x4D, 0xFF);

    /// <summary>Resolves a colour token (or <c>#hex</c>); <c>null</c>/<c>primary</c> = default label colour.</summary>
    public static Color Color(string? token, bool dark) => token switch
    {
        null or "primary" => dark ? Colors.White : Colors.Black,
        "secondary" => Secondary,
        "red" => new Color(0xFF, 0x3B, 0x30),
        "green" => new Color(0x34, 0xC7, 0x59),
        "blue" => new Color(0x00, 0x7A, 0xFF),
        "accentColor" => Accent,
        _ when token.StartsWith('#') && Graphics.Color.TryParse(token, out var c) => c,
        _ => dark ? Colors.White : Colors.Black,
    };

    /// <summary>Font token → point size + weight.</summary>
    public static (float size, bool bold) Font(string? token) => token switch
    {
        "largeTitle" => (30f, true),
        "title" => (24f, false),
        "headline" => (17f, true),
        "body" => (16f, false),
        "caption" => (12f, false),
        _ => (16f, false),
    };

    /// <summary>Resolves a font token through a backend's provider.</summary>
    public static Font MakeFont(string? token, IFontProvider fonts)
    {
        var (size, bold) = Font(token);
        return fonts.Get(size, bold);
    }

    /// <summary>SF-Symbol name → an emoji/glyph stand-in (no symbol set on a bare canvas). Mirrors GtkStyle.Emoji.</summary>
    public static string Icon(string name) => name switch
    {
        "star.fill" or "star" => "★",
        "heart.fill" or "heart" => "♥",
        "bell" or "bell.fill" => "🔔",
        "checkmark" => "✓",
        "slider.horizontal.3" => "🎚",
        "square.grid.2x2" => "▦",
        "rectangle.stack" => "🗂",
        "list.bullet" => "☰",
        "map" or "map.fill" => "🗺",
        "arrow.forward.circle" => "➡",
        "plus" => "＋",
        "camera" => "📷",
        "mic" => "🎤",
        "doc" => "📄",
        "folder" => "📁",
        "photo" => "🖼",
        "music" => "🎵",
        "calendar" => "📅",
        "gauge" => "⏲",
        "bubble.left.and.bubble.right" => "💬",
        "square.on.square" => "❐",
        "tablecells" => "▦",
        "square.stack" => "🗂",
        "textformat" => "🔤",
        "hand.tap" => "👆",
        "wand.and.stars" => "✨",
        "paintbrush" => "🖌",
        "globe" => "🌐",
        "chevron.down.circle" => "⌄",
        "rectangle.portrait" => "▭",
        "rectangle.3.offgrid" => "▤",
        "trash" => "🗑",
        "xmark" => "✕",
        _ => "•",
    };
}
