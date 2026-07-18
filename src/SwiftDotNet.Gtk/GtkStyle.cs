using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>Maps SwiftDotNet tokens/modifiers to GTK values and GTK CSS.</summary>
static class GtkStyle
{
    public static string? Hex(string? token) => token switch
    {
        null => null,
        "primary" => null,           // default label color
        "secondary" => "#8E8E93",
        "red" => "#FF3B30",
        "green" => "#34C759",
        "blue" => "#007AFF",
        "accentColor" => "#7C4DFF",
        _ when token.StartsWith('#') => token,
        _ => null,
    };

    public static Gtk.Align AlignOf(string? token) => token switch
    {
        "leading" or "topLeading" or "bottomLeading" => Gtk.Align.Start,
        "trailing" or "topTrailing" or "bottomTrailing" => Gtk.Align.End,
        _ => Gtk.Align.Center,
    };

    public static Gtk.Align VAlignOf(string? token) => token switch
    {
        "top" or "topLeading" or "topTrailing" => Gtk.Align.Start,
        "bottom" or "bottomLeading" or "bottomTrailing" => Gtk.Align.End,
        _ => Gtk.Align.Center,
    };

    static (int size, int weight) Font(string? token) => token switch
    {
        "largeTitle" => (30, 700),
        "title" => (24, 400),
        "headline" => (17, 700),
        "body" => (16, 400),
        "caption" => (12, 400),
        _ => (0, 0),
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

    static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds the CSS declaration body for a node's visual modifiers (shape = fill via foreground).</summary>
    public static string BuildCss(List<Dictionary<string, object?>> modifiers, bool shapeFill)
    {
        var sb = new StringBuilder();
        foreach (var m in modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    sb.Append($"padding:{Num(N(m, "top"))}px {Num(N(m, "trailing"))}px {Num(N(m, "bottom"))}px {Num(N(m, "leading"))}px;");
                    break;
                case "background":
                    if (Hex(m.GetValueOrDefault("value") as string) is { } bg) sb.Append($"background-color:{bg};");
                    break;
                case "cornerRadius":
                    sb.Append($"border-radius:{Num(N(m, "radius"))}px;");
                    break;
                case "border":
                    if (Hex(m.GetValueOrDefault("color") as string) is { } bc)
                        sb.Append($"border:{Num(N(m, "width", 1))}px solid {bc};");
                    if (N(m, "cornerRadius") > 0) sb.Append($"border-radius:{Num(N(m, "cornerRadius"))}px;");
                    break;
                case "shadow":
                    var sc = Hex(m.GetValueOrDefault("color") as string) ?? "rgba(0,0,0,0.35)";
                    sb.Append($"box-shadow:{Num(N(m, "x"))}px {Num(N(m, "y"))}px {Num(N(m, "radius", 4))}px {sc};");
                    break;
                case "foregroundColor":
                    if (Hex(m.GetValueOrDefault("value") as string) is { } fc)
                        sb.Append(shapeFill ? $"background-color:{fc};" : $"color:{fc};");
                    break;
                case "font":
                    var (size, weight) = Font(m.GetValueOrDefault("value") as string);
                    if (size > 0) sb.Append($"font-size:{size}px;font-weight:{weight};");
                    break;
                case "animation":
                    // GTK4 has no declarative animation engine; CSS transitions cover the properties GTK
                    // exposes to CSS (color/background/border). Non-CSS props (widget opacity, frame size)
                    // still snap — a documented degradation (spring also degrades to ease-in-out).
                    var dur = Num(N(m, "duration", 0.3));
                    var delay = Num(N(m, "delay", 0));
                    sb.Append($"transition:all {dur}s {Timing(m.GetValueOrDefault("curve") as string)} {delay}s;");
                    break;
            }
        }
        return sb.ToString();
    }

    static string Timing(string? curve) => curve switch
    {
        "linear" => "linear",
        "easeIn" => "ease-in",
        "easeOut" => "ease-out",
        _ => "ease-in-out",   // spring → ease-in-out (GTK CSS has no spring)
    };

    static double N(Dictionary<string, object?> m, string key, double fallback = 0)
        => m.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
