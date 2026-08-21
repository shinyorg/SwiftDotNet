namespace SwiftDotNet;

/// <summary>
/// Lowers a live tree into an ordinary core <see cref="Node"/> tree.
///
/// This exists to make <see cref="LiveRenderMode.Bitmap"/> nearly free. The live vocabulary was defined as
/// a strict *subset* of the main DSL, so every live node has a core counterpart already implemented by
/// every backend: <c>LText</c> is a <c>Text</c>, <c>LVStack</c> is a <c>VStack</c>, and so on. Rewriting
/// the type names is therefore enough to hand the tree to the existing headless renderer
/// (<c>VisualBridge.Render</c> then <c>Draw</c>) and get pixels back, with the whole Skia layout and paint
/// engine doing the work. No second rasterizer, no second layout pass.
///
/// It is also what the preview host and the Skia backends can use to show a live surface on the desktop,
/// which is the only way to iterate on a lock-screen design without a device.
///
/// <para><b>The one thing lowering cannot preserve is time.</b> A <see cref="LiveTimer"/> ticks by itself
/// on a real host because the host owns a clock; a bitmap is a still frame. Lowering therefore renders a
/// timer as the text it would show *at that instant*, and the caller is told so via <c>SDNL030</c>. Any
/// surface whose whole job is a live countdown should stay on <see cref="LiveRenderMode.Native"/>.</para>
/// </summary>
public static class LiveLowering
{
    /// <summary>Rewrites a live tree into a core tree the standard renderers understand.</summary>
    /// <param name="live">The live node tree, as built by <see cref="LiveWire.Build"/>.</param>
    /// <param name="now">The instant timers and dates are frozen at, as unix seconds.</param>
    public static Node ToCoreNode(Node live, double now)
    {
        var node = new Node { Id = live.Id, Type = CoreType(live.Type) };

        CopyProps(live, node, now);
        CopyModifiers(live, node);

        foreach (var child in live.Children)
            node.Children.Add(ToCoreNode(child, now));

        return node;
    }

    /// <summary>Reports what a lowering pass will lose, so a caller can refuse the bitmap route.</summary>
    public static IReadOnlyList<LiveDiagnostic> Diagnose(Node live)
    {
        var found = new List<LiveDiagnostic>();
        Walk(live);
        return found;

        void Walk(Node n)
        {
            if (n.Type == "LTimer")
                found.Add(new("SDNL030", LiveSeverity.Warning,
                    "A LiveTimer cannot tick inside a rendered bitmap; it freezes at the instant of render. " +
                    "Use LiveRenderMode.Native for a live countdown, or re-render on a schedule.", n.Id));

            foreach (var c in n.Children) Walk(c);
        }
    }

    static string CoreType(string liveType) => liveType switch
    {
        "LText" or "LDate" or "LTimer" => "Text",
        "LVStack" => "VStack",
        "LHStack" => "HStack",
        // A link has no core analog that draws; it is a pass-through wrapper, and a ZStack of one child
        // lays out identically to the child alone.
        "LZStack" or "LLink" => "ZStack",
        "LSpacer" => "Spacer",
        "LDivider" => "Divider",
        "LImage" or "LBitmap" => "Image",
        "LProgress" => "ProgressView",
        "LGauge" => "Gauge",
        "LButton" => "Button",
        "LShape" => "Rectangle",
        _ => "Text",
    };

    static void CopyProps(Node live, Node core, double now)
    {
        switch (live.Type)
        {
            case "LText":
                core.Props["text"] = Str(live, "text");
                break;

            case "LTimer":
                core.Props["text"] = FreezeTimer(live, now);
                break;

            case "LDate":
                core.Props["text"] = FreezeDate(live);
                break;

            case "LImage":
                // Live image names are platform symbols, which is exactly what the core "system" kind is.
                core.Props["system"] = Str(live, "name");
                break;

            case "LBitmap":
                core.Props["bytes"] = Str(live, "png");
                core.Props["contentMode"] = "fit";
                break;

            case "LButton":
                core.Props["title"] = Str(live, "title");
                break;

            case "LShape":
                // A shape's fill comes from a background modifier in both vocabularies, so only the
                // geometry needs translating; the type was already chosen by CoreType.
                core.Type = Str(live, "shape") switch
                {
                    "rounded" => "RoundedRectangle",
                    "capsule" => "Capsule",
                    "circle" => "Circle",
                    _ => "Rectangle",
                };
                if (live.Props.TryGetValue("radius", out var radius)) core.Props["radius"] = radius;
                break;

            default:
                foreach (var kv in live.Props) core.Props[kv.Key] = kv.Value;
                return;
        }

        // Layout props travel unchanged; only the content props above needed translating.
        if (live.Props.TryGetValue("spacing", out var spacing)) core.Props["spacing"] = spacing;
        if (live.Props.TryGetValue("alignment", out var alignment)) core.Props["alignment"] = alignment;
        if (live.Props.TryGetValue("value", out var value)) core.Props["value"] = value;
        if (live.Props.TryGetValue("min", out var min)) core.Props["min"] = min;
        if (live.Props.TryGetValue("max", out var max)) core.Props["max"] = max;
    }

    static void CopyModifiers(Node live, Node core)
    {
        foreach (var mod in live.Modifiers)
        {
            if (!mod.TryGetValue("type", out var raw) || raw is not string type) continue;

            // Most live modifiers were named to match the core wire exactly. The two that differ do so
            // because the core names predate this vocabulary, and renaming them there would be a
            // breaking change to every backend for no gain.
            var copy = new Dictionary<string, object>(mod);
            switch (type)
            {
                case "cornerRadius":
                    Rename(copy, "value", "radius");
                    break;
                case "opacity":
                    Rename(copy, "value", "amount");
                    break;
                case "tint":
                    // No core "tint": the nearest equivalent that every backend honours is a foreground
                    // color, which is what a tinted progress bar or symbol actually paints with.
                    copy["type"] = "foregroundColor";
                    break;
                case "a11yLabel":
                case "tapUrl":
                case "lineLimit":
                case "bold":
                    // No core wire form. Dropping them changes nothing visible in a raster render, which
                    // is the only consumer of a lowered tree.
                    continue;
            }

            core.Modifiers.Add(copy);
        }
    }

    static void Rename(Dictionary<string, object> d, string from, string to)
    {
        if (d.Remove(from, out var v)) d[to] = v;
    }

    static string FreezeTimer(Node live, double now)
    {
        var target = Num(live, "target");
        var countsDown = live.Props.TryGetValue("countsDown", out var cd) && cd is bool b && b;
        var seconds = countsDown ? target - now : now - target;
        if (seconds < 0) seconds = 0;

        var span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}"
            : $"{span.Minutes}:{span.Seconds:00}";
    }

    static string FreezeDate(Node live)
    {
        var when = DateTimeOffset.FromUnixTimeMilliseconds((long)(Num(live, "date") * 1000)).ToLocalTime();
        return Str(live, "style") switch
        {
            "date" => when.ToString("d"),
            "relative" or "offset" => when.ToString("g"),
            _ => when.ToString("t"),
        };
    }

    static string Str(Node n, string key) => n.Props.TryGetValue(key, out var v) && v is string s ? s : "";
    static double Num(Node n, string key) => n.Props.TryGetValue(key, out var v) && v is double d ? d : 0;
}
