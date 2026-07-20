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

    /// <summary>
    /// Applies an <c>Alignment.Token()</c> (topLeading…bottomTrailing) to a widget, constraining
    /// <em>only the axes the token actually names</em>: <c>bottom</c> pins to the bottom edge but leaves
    /// the horizontal axis at GTK's <c>Fill</c> default, which is what a full-width toast/banner needs;
    /// <c>leading</c> pins horizontally and leaves the vertical axis filling. <c>center</c>/null touch
    /// neither axis (Fill/Fill), preserving the pre-existing ZStack behaviour where an overlay child
    /// stretches to the base child's size. Note this wins over a child's own <c>.Frame(alignment:)</c> on
    /// the named axes.
    /// </summary>
    public static void ApplyAlignment(Gtk.Widget w, string? token)
    {
        switch (token)
        {
            case "leading" or "topLeading" or "bottomLeading": w.Halign = Gtk.Align.Start; break;
            case "trailing" or "topTrailing" or "bottomTrailing": w.Halign = Gtk.Align.End; break;
        }
        switch (token)
        {
            case "top" or "topLeading" or "topTrailing": w.Valign = Gtk.Align.Start; break;
            case "bottom" or "bottomLeading" or "bottomTrailing": w.Valign = Gtk.Align.End; break;
        }
    }

    /// <summary>The pivot point (in child coordinates) an <c>Alignment.Token()</c> anchor names, for a
    /// child of <paramref name="width"/>×<paramref name="height"/> — used to centre a Gsk scale/rotate.</summary>
    public static (double x, double y) AnchorPoint(string? token, double width, double height) => (
        token switch
        {
            "leading" or "topLeading" or "bottomLeading" => 0,
            "trailing" or "topTrailing" or "bottomTrailing" => width,
            _ => width / 2,
        },
        token switch
        {
            "top" or "topLeading" or "topTrailing" => 0,
            "bottom" or "bottomLeading" or "bottomTrailing" => height,
            _ => height / 2,
        });

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

    static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    /// <summary>Builds the CSS declaration body for a node's visual modifiers (shape = fill via foreground).
    /// <paramref name="loopName"/> is the node-unique <c>@keyframes</c> name a repeating <c>animation</c>
    /// modifier references — see <see cref="BuildKeyframes"/>.</summary>
    public static string BuildCss(List<Dictionary<string, object?>> modifiers, bool shapeFill, string loopName = "sdn-loop")
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
                    // GTK4 has no declarative animation engine, but its CSS engine DOES implement both
                    // `transition` and `@keyframes` + the `animation` shorthand, over the properties GTK
                    // exposes to CSS (color/background/border/border-radius/box-shadow/opacity). Non-CSS
                    // props (frame size, Gsk transforms) still snap — a documented degradation, and spring
                    // degrades to ease-in-out.
                    var dur = Num(N(m, "duration", 0.3));
                    var delay = Num(N(m, "delay", 0));
                    if (m.GetValueOrDefault("repeatCount") is double rc)
                    {
                        // Self-playing loop (shimmer/pulse): -1 = forever, autoreverse = `alternate`.
                        var iter = rc < 0 ? "infinite" : Num(Math.Max(1, (int)rc));
                        var dir = (m.GetValueOrDefault("autoreverse") as string) == "true" ? "alternate" : "normal";
                        sb.Append($"animation:{loopName} {dur}s {Timing(m.GetValueOrDefault("curve") as string)} {delay}s {iter} {dir};");
                    }
                    else
                        sb.Append($"transition:all {dur}s {Timing(m.GetValueOrDefault("curve") as string)} {delay}s;");
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// The <c>@keyframes</c> block a repeating <c>animation</c> modifier needs, or <c>""</c> when the node
    /// has no repeating animation. The wire carries no from/to pair — a repeating animation just says
    /// "play forever" — so, exactly like the Web backend's shared <c>sdn-pulse</c> keyframes, we loop
    /// <c>opacity</c> 1 → 0.4. That is the one animatable property that reads correctly for both callers:
    /// SkeletonView's shimmer and BadgeView's pulse (whose <c>.ScaleEffect(1.0)</c> is identity anyway).
    /// LIMIT: only opacity loops. GTK CSS has no <c>transform</c>, so a looping scale/rotate is not
    /// expressible here even though one-shot scale/rotate now works via a Gsk transform (see GtkNode).
    /// </summary>
    public static string BuildKeyframes(List<Dictionary<string, object?>> modifiers, string loopName)
        => modifiers.Any(m => m["type"] as string == "animation" && m.ContainsKey("repeatCount"))
            ? $"@keyframes {loopName} {{ from {{ opacity:1; }} to {{ opacity:0.4; }} }}"
            : "";

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
