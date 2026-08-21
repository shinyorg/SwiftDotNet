using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// The **compact** wire for live surfaces — the first place in this framework where the serialized tree
/// has a hard byte budget.
///
/// <see cref="NodeJson"/> is the right shape for the main pipeline: it crosses a C ABI or a JNI call in
/// the same process, so a few hundred wasted bytes per render cost nothing. A Live Activity is different.
/// Its entire state travels inside an APNs payload capped at <see cref="LiveBudget.ActivityHardBytes"/>,
/// and blowing that ceiling does not throw — the update is simply rejected and the activity silently
/// keeps showing stale content on a tester's lock screen.
///
/// So this writer trades readability for bytes:
/// <list type="bullet">
///   <item><description>single-letter keys — <c>t</c>ype, <c>p</c>rops, <c>m</c>odifiers, <c>c</c>hildren, <c>i</c>d</description></item>
///   <item><description>the <c>L</c> prefix is dropped from node types (<c>LText</c> → <c>Text</c>)</description></item>
///   <item><description>empty props / modifiers / children are omitted entirely, not written as <c>{}</c> or <c>[]</c></description></item>
///   <item><description>ids ride only on nodes that need one — the interpreters address buttons by id and nothing else</description></item>
/// </list>
///
/// It stays hand-rolled and reflection-free for the same reason <see cref="NodeJson"/> is: everything
/// under <c>Core</c> must survive trimming and AOT.
///
/// Deltas were considered and rejected. <see cref="TreeDiffer"/> cannot help here because the renderer is
/// stateless between system-driven renders — a widget extension is launched, draws, and dies, so there is
/// never a prior tree on the far side to patch against. Whole trees only.
/// </summary>
public static class LiveWire
{
    /// <summary>
    /// Builds a tree and serializes it, returning the JSON and the actions found along the way.
    ///
    /// <paramref name="rootId"/> seeds the structural ids, and it matters: node ids are how an inbound
    /// action finds its handler, so a tree republished under a different root id orphans every handler
    /// registered against the old one. Callers that publish several trees for one surface — an activity's
    /// slots, a widget's family × entry fan-out — pass the variant key so the ids stay stable and unique.
    /// </summary>
    public static LivePayload Build(LiveView view, LiveSurface surface = LiveSurface.Activity, string rootId = "l")
    {
        var ctx = new LiveContext { Surface = surface };
        var node = view.ToNode(ctx, rootId);
        return new LivePayload(Serialize(node), node, ctx.Actions);
    }

    /// <summary>Serializes an already-built live node tree to the compact wire.</summary>
    public static string Serialize(Node node)
    {
        var sb = new StringBuilder(256);
        Append(sb, node);
        return sb.ToString();
    }

    /// <summary>UTF-8 byte length of a payload — what every platform budget is actually measured in.</summary>
    public static int ByteCount(string json) => Encoding.UTF8.GetByteCount(json);

    static void Append(StringBuilder sb, Node node)
    {
        sb.Append("{\"t\":");
        NodeJson.AppendString(sb, ShortType(node.Type));

        // An id is only ever addressed for a button, so writing one on every node would spend bytes on
        // structure the interpreters never look at.
        if (NeedsId(node.Type))
        {
            sb.Append(",\"i\":");
            NodeJson.AppendString(sb, node.Id);
        }

        if (node.Props.Count > 0)
        {
            sb.Append(",\"p\":");
            AppendDict(sb, node.Props, shortenType: false);
        }

        if (node.Modifiers.Count > 0)
        {
            sb.Append(",\"m\":[");
            for (var i = 0; i < node.Modifiers.Count; i++)
            {
                if (i > 0) sb.Append(',');
                AppendModifier(sb, node.Modifiers[i]);
            }
            sb.Append(']');
        }

        if (node.Children.Count > 0)
        {
            sb.Append(",\"c\":[");
            for (var i = 0; i < node.Children.Count; i++)
            {
                if (i > 0) sb.Append(',');
                Append(sb, node.Children[i]);
            }
            sb.Append(']');
        }

        sb.Append('}');
    }

    /// <summary>Modifiers keep their value key names but shorten the discriminator <c>type</c> → <c>t</c>.</summary>
    static void AppendModifier(StringBuilder sb, Dictionary<string, object> mod)
        => AppendDict(sb, mod, shortenType: true);

    /// <summary>
    /// Writes a dictionary with this writer's own value formatting.
    ///
    /// Deliberately not <see cref="NodeJson.AppendDict"/>: that one writes doubles at full round-trip
    /// precision, which is right for the core wire and wrong here — a single 0.30000000000000004 spends
    /// 19 characters of a 4 KB budget saying "0.3".
    /// </summary>
    static void AppendDict(StringBuilder sb, Dictionary<string, object> dict, bool shortenType)
    {
        sb.Append('{');
        var first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            NodeJson.AppendString(sb, shortenType && kv.Key == "type" ? "t" : kv.Key);
            sb.Append(':');
            AppendValue(sb, kv.Value);
        }
        sb.Append('}');
    }

    static void AppendValue(StringBuilder sb, object value)
    {
        switch (value)
        {
            case string s: NodeJson.AppendString(sb, s); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case double d: sb.Append(Round(d)); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(Round(f)); break;
            default: NodeJson.AppendString(sb, value?.ToString() ?? ""); break;
        }
    }

    /// <summary>
    /// Trims doubles to three decimals. Sub-pixel precision buys nothing on a lock screen and a raw
    /// round-tripped double can otherwise spend 17 characters saying "0.30000000000000004".
    /// </summary>
    static string Round(double d)
    {
        var r = Math.Round(d, 3);
        return r == Math.Floor(r) && Math.Abs(r) < 1e15
            ? ((long)r).ToString(CultureInfo.InvariantCulture)
            : r.ToString("0.###", CultureInfo.InvariantCulture);
    }

    internal static string ShortType(string type) =>
        type.Length > 1 && type[0] == 'L' ? type.Substring(1) : type;

    internal static bool NeedsId(string type) => type == "LButton";
}

/// <summary>A serialized live tree plus the handlers the platform driver must register before it ships.</summary>
/// <param name="Json">The compact wire form.</param>
/// <param name="Root">The built tree, kept so <see cref="LiveValidator"/> can inspect structure rather than re-parse JSON.</param>
/// <param name="Actions">Button handlers keyed by node id.</param>
public readonly record struct LivePayload(string Json, Node Root, IReadOnlyDictionary<string, Action<string?>> Actions)
{
    /// <summary>UTF-8 size of <see cref="Json"/> — the number every platform budget is expressed in.</summary>
    public int Bytes => LiveWire.ByteCount(Json);
}
