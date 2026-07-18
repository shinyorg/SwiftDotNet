using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// Hand-rolled JSON writer for <see cref="Node"/> trees and patch fragments. Zero reflection →
/// fully trim/AOT-safe (no IL2026), and cheap enough to run on every render.
/// </summary>
public static class NodeJson
{
    public static string Serialize(Node node)
    {
        var sb = new StringBuilder(256);
        AppendNode(sb, node);
        return sb.ToString();
    }

    internal static void AppendNode(StringBuilder sb, Node node)
    {
        sb.Append('{');
        sb.Append("\"id\":");
        AppendString(sb, node.Id);
        sb.Append(",\"type\":");
        AppendString(sb, node.Type);
        sb.Append(",\"props\":");
        AppendDict(sb, node.Props);
        sb.Append(",\"modifiers\":");
        AppendDictArray(sb, node.Modifiers);
        sb.Append(",\"children\":[");
        for (var i = 0; i < node.Children.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendNode(sb, node.Children[i]);
        }
        sb.Append("]}");
    }

    internal static void AppendDictArray(StringBuilder sb, List<Dictionary<string, object>> dicts)
    {
        sb.Append('[');
        for (var i = 0; i < dicts.Count; i++)
        {
            if (i > 0) sb.Append(',');
            AppendDict(sb, dicts[i]);
        }
        sb.Append(']');
    }

    internal static void AppendDict(StringBuilder sb, Dictionary<string, object> dict)
    {
        sb.Append('{');
        var first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append(',');
            first = false;
            AppendString(sb, kv.Key);
            sb.Append(':');
            AppendValue(sb, kv.Value);
        }
        sb.Append('}');
    }

    static void AppendValue(StringBuilder sb, object value)
    {
        switch (value)
        {
            case string s: AppendString(sb, s); break;
            case bool b: sb.Append(b ? "true" : "false"); break;
            case double d: sb.Append(d.ToString(CultureInfo.InvariantCulture)); break;
            case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
            case float f: sb.Append(((double)f).ToString(CultureInfo.InvariantCulture)); break;
            default: AppendString(sb, value?.ToString() ?? ""); break;
        }
    }

    internal static void AppendString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
