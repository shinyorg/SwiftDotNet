using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>Maps SwiftDotNet tokens/modifiers to CSS for the Blazor/DOM backend.</summary>
static class WebStyle
{
    public static string? Color(string? token) => token switch
    {
        null or "primary" => null,
        "secondary" => "#8E8E93",
        "red" => "#FF3B30",
        "green" => "#34C759",
        "blue" => "#007AFF",
        "accentColor" => "#7C4DFF",
        _ when token.StartsWith('#') => token,
        _ => null,
    };

    static (int size, int weight)? Font(string? token) => token switch
    {
        "largeTitle" => (30, 700),
        "title" => (24, 400),
        "headline" => (17, 600),
        "body" => (16, 400),
        "caption" => (12, 400),
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

    static string Px(double v) => v.ToString(CultureInfo.InvariantCulture) + "px";

    /// <summary>Inline CSS for a node's modifiers. shapeFill routes foregroundColor to background.</summary>
    public static string Modifiers(List<Dictionary<string, object?>> modifiers, bool shapeFill)
    {
        var sb = new StringBuilder();
        foreach (var m in modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    sb.Append($"padding:{Px(N(m, "top"))} {Px(N(m, "trailing"))} {Px(N(m, "bottom"))} {Px(N(m, "leading"))};");
                    break;
                case "background":
                    if (Color(m.GetValueOrDefault("value") as string) is { } bg) sb.Append($"background:{bg};");
                    break;
                case "cornerRadius":
                    sb.Append($"border-radius:{Px(N(m, "radius"))};");
                    break;
                case "border":
                    if (Color(m.GetValueOrDefault("color") as string) is { } bc)
                        sb.Append($"border:{Px(N(m, "width", 1))} solid {bc};");
                    if (N(m, "cornerRadius") > 0) sb.Append($"border-radius:{Px(N(m, "cornerRadius"))};");
                    break;
                case "shadow":
                    var sc = Color(m.GetValueOrDefault("color") as string) ?? "rgba(0,0,0,0.35)";
                    sb.Append($"box-shadow:{Px(N(m, "x"))} {Px(N(m, "y"))} {Px(N(m, "radius", 4))} {sc};");
                    break;
                case "opacity":
                    sb.Append($"opacity:{N(m, "amount", 1).ToString(CultureInfo.InvariantCulture)};");
                    break;
                case "disabled":
                    if ((m.GetValueOrDefault("value") as string) == "true")
                        sb.Append("pointer-events:none;opacity:0.5;");
                    break;
                case "scaleEffect":
                    sb.Append($"transform:scale({N(m, "x", 1).ToString(CultureInfo.InvariantCulture)},{N(m, "y", 1).ToString(CultureInfo.InvariantCulture)});");
                    if (m.GetValueOrDefault("value") is string sanchor) sb.Append($"transform-origin:{OriginCss(sanchor)};");
                    break;
                case "animation":
                    // CSS animates whenever a transitioned property changes, so the `trigger` isn't needed here.
                    var dur = N(m, "duration", 0.3).ToString(CultureInfo.InvariantCulture);
                    var delay = N(m, "delay", 0).ToString(CultureInfo.InvariantCulture);
                    sb.Append($"transition:all {dur}s {TimingFunction(m.GetValueOrDefault("curve") as string)} {delay}s;");
                    break;
                case "foregroundColor":
                    if (Color(m.GetValueOrDefault("value") as string) is { } fc)
                        sb.Append(shapeFill ? $"background:{fc};" : $"color:{fc};");
                    break;
                case "font":
                    if (Font(m.GetValueOrDefault("value") as string) is { } f)
                        sb.Append($"font-size:{f.size}px;font-weight:{f.weight};");
                    break;
                case "frame":
                    if (Num(m, "width") is { } w) sb.Append($"width:{Px(w)};");
                    if (Num(m, "height") is { } h) sb.Append($"height:{Px(h)};");
                    if (m.GetValueOrDefault("alignment") is string fa) sb.Append(AlignSelf(fa));
                    break;
                case "align":
                    sb.Append("width:100%;");
                    sb.Append(m.GetValueOrDefault("value") is string av ? TextAlign(av) : "");
                    break;
            }
        }
        return sb.ToString();
    }

    static string AlignSelf(string token) => token switch
    {
        "leading" or "topLeading" or "bottomLeading" => "align-self:flex-start;",
        "trailing" or "topTrailing" or "bottomTrailing" => "align-self:flex-end;",
        _ => "align-self:center;",
    };

    static string TextAlign(string token) => token switch
    {
        "leading" => "text-align:left;",
        "trailing" => "text-align:right;",
        _ => "text-align:center;",
    };

    static string OriginCss(string token) => token switch
    {
        "topLeading" => "left top", "top" => "center top", "topTrailing" => "right top",
        "leading" => "left center", "trailing" => "right center",
        "bottomLeading" => "left bottom", "bottom" => "center bottom", "bottomTrailing" => "right bottom",
        _ => "center",
    };

    // Spring has no CSS equivalent — approximate with a slight-overshoot bezier.
    static string TimingFunction(string? curve) => curve switch
    {
        "linear" => "linear",
        "easeIn" => "ease-in",
        "easeOut" => "ease-out",
        "spring" => "cubic-bezier(0.34, 1.56, 0.64, 1)",
        _ => "ease-in-out",
    };

    static double? Num(Dictionary<string, object?> m, string key) => m.TryGetValue(key, out var v) && v is double d ? d : null;
    static double N(Dictionary<string, object?> m, string key, double fallback = 0) => m.TryGetValue(key, out var v) && v is double d ? d : fallback;
}
