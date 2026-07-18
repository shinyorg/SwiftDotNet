using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SwiftDotNet;

/// <summary>Maps SwiftDotNet tokens to WinUI colors/fonts/brushes.</summary>
static class WinStyle
{
    public static Color? Color(string? token)
    {
        if (token is null || token == "primary") return null;
        if (token.StartsWith('#')) return FromHex(token);
        return token switch
        {
            "secondary" => FromRgb(0x8E, 0x8E, 0x93),
            "red" => FromRgb(0xFF, 0x3B, 0x30),
            "green" => FromRgb(0x34, 0xC7, 0x59),
            "blue" => FromRgb(0x00, 0x7A, 0xFF),
            "accentColor" => FromRgb(0x7C, 0x4D, 0xFF),
            _ => null,
        };
    }

    public static SolidColorBrush? Brush(string? token) => Color(token) is { } c ? new SolidColorBrush(c) : null;

    static Color FromRgb(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    static Color? FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return null;
        return Windows.UI.Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static (double size, FontWeight weight)? Font(string? token) => token switch
    {
        "largeTitle" => (30, FontWeights.Bold),
        "title" => (24, FontWeights.Normal),
        "headline" => (17, FontWeights.SemiBold),
        "body" => (16, FontWeights.Normal),
        "caption" => (12, FontWeights.Normal),
        _ => null,
    };

    public static string Emoji(string name) => name switch
    {
        "star.fill" or "star" => "⭐",
        "heart.fill" or "heart" => "❤️",
        "bell" or "bell.fill" => "🔔",
        "checkmark" => "✅",
        "slider.horizontal.3" => "🎚️",
        "square.grid.2x2" => "▦",
        "rectangle.stack" => "🗂️",
        "list.bullet" => "☰",
        "arrow.forward.circle" => "➡️",
        _ => "•",
    };
}
