using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Maps SwiftDotNet's semantic tokens (colors, fonts, SF-Symbol-ish icon names) to concrete Skia
/// values. Because the Skia backend draws every pixel itself, there is no OS theme to defer to — this
/// class <em>is</em> the theme, resolving tokens against a light/dark flag the host supplies.
/// </summary>
static class SkiaTheme
{
    // Cache the two faces we need; creating typefaces is relatively expensive.
    static readonly SKTypeface Regular = SKTypeface.FromFamilyName(null, SKFontStyle.Normal) ?? SKTypeface.Default;
    static readonly SKTypeface Bold = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;

    /// <summary>System background (window fill).</summary>
    public static SKColor Background(bool dark) => dark ? new SKColor(0x1C, 0x1C, 0x1E) : new SKColor(0xFF, 0xFF, 0xFF);

    /// <summary>A subtle filled surface (grouped rows, plain-button chrome) — SwiftUI systemGray6-ish.</summary>
    public static SKColor Surface(bool dark) => dark ? new SKColor(0x2C, 0x2C, 0x2E) : new SKColor(0xF2, 0xF2, 0xF7);

    public static SKColor Separator(bool dark) => dark ? new SKColor(0x38, 0x38, 0x3A) : new SKColor(0xC6, 0xC6, 0xC8);

    /// <summary>Resolves a color token (or <c>#hex</c>) to a concrete color; <c>null</c>/<c>primary</c> = default label color.</summary>
    public static SKColor Color(string? token, bool dark) => token switch
    {
        null or "primary" => dark ? SKColors.White : SKColors.Black,
        "secondary" => new SKColor(0x8E, 0x8E, 0x93),
        "red" => new SKColor(0xFF, 0x3B, 0x30),
        "green" => new SKColor(0x34, 0xC7, 0x59),
        "blue" => new SKColor(0x00, 0x7A, 0xFF),
        "accentColor" => new SKColor(0x7C, 0x4D, 0xFF),
        _ when token.StartsWith('#') && SKColor.TryParse(token, out var c) => c,
        _ => dark ? SKColors.White : SKColors.Black,
    };

    public static SKColor Accent => new(0x7C, 0x4D, 0xFF);

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

    public static SKFont MakeFont(string? token)
    {
        var (size, bold) = Font(token);
        return new SKFont(bold ? Bold : Regular, size);
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
        _ => "•",
    };
}
