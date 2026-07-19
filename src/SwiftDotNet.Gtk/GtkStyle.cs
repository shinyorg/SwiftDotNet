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
                    if (m.GetValueOrDefault("gradient") is string grad && Gradient(grad) is { } g) sb.Append($"background-image:{g};");
                    else if (Hex(m.GetValueOrDefault("value") as string) is { } bg) sb.Append($"background-color:{bg};");
                    break;
                case "material":
                    // GTK4 has no widget backdrop-blur → translucent tint fallback (documented degradation).
                    var mtint = (m.GetValueOrDefault("value") as string) switch
                    { "ultraThin" => 0.55, "thin" => 0.65, "thick" => 0.85, _ => 0.75 };
                    var mrgb = (m.GetValueOrDefault("dark") as string) == "true" ? "20,20,22" : "255,255,255";
                    sb.Append($"background-color:rgba({mrgb},{Num(mtint)});");
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
                case "offset":
                    // F4 translate: GTK4 has no widget transform, but CSS margins (incl. negative) shift a
                    // widget's allocation — translating start/top-aligned widgets (slider/picker thumbs,
                    // badges, dragged rows). A fill-aligned widget shifts approximately. Rotation/scale stay no-ops.
                    var ox = N(m, "x"); var oy = N(m, "y");
                    if (ox != 0) sb.Append($"margin-left:{Num(ox)}px;margin-right:{Num(-ox)}px;");
                    if (oy != 0) sb.Append($"margin-top:{Num(oy)}px;margin-bottom:{Num(-oy)}px;");
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

    /// <summary>Parses a <see cref="Brush"/> wire string into a GTK CSS gradient (GTK4 supports these), or null.</summary>
    static string? Gradient(string spec)
    {
        var firstColon = spec.IndexOf(':');
        if (firstColon < 0) return null;
        var kind = spec[..firstColon];
        var rest = spec[(firstColon + 1)..];
        if (kind == "linear")
        {
            var secondColon = rest.IndexOf(':');
            if (secondColon < 0) return null;
            var angle = double.TryParse(rest[..secondColon], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 90;
            var stops = Stops(rest[(secondColon + 1)..]);
            return stops is null ? null : $"linear-gradient({Num(angle)}deg,{stops})";
        }
        if (kind == "radial")
        {
            var stops = Stops(rest);
            return stops is null ? null : $"radial-gradient(circle,{stops})";
        }
        return null;
    }

    static string? Stops(string spec)
    {
        var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < parts.Length; i++)
        {
            var at = parts[i].LastIndexOf('@');
            if (at < 0) return null;
            var color = Hex(parts[i][..at]) ?? "#000000";
            var loc = double.TryParse(parts[i][(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
            if (i > 0) sb.Append(',');
            sb.Append($"{color} {Num(loc * 100)}%");
        }
        return sb.ToString();
    }

    static double N(Dictionary<string, object?> m, string key, double fallback = 0)
        => m.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
