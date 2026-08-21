using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// Parses the compact wire back into a <see cref="Node"/> tree.
///
/// Two callers need this and they are not obvious. First, the Android drivers: a widget provider can be
/// woken by a broadcast long after the app process died, and re-reading the published snapshot is cheaper
/// and more faithful than re-running the app's timeline code. Second, the tests — a writer with no reader
/// can only be asserted against a hand-written expected string, which tests the string and not the
/// contract. With both halves the round trip itself is the assertion.
///
/// Hand-rolled and reflection-free, like <see cref="NodeJson"/> and for the same reason: everything on
/// this path must survive trimming and Native AOT. It is a *subset* reader — it accepts what
/// <see cref="LiveWire"/> emits, not arbitrary JSON — which is why it is small enough to be obviously
/// correct.
/// </summary>
public static class LiveWireReader
{
    /// <summary>Parses a compact tree. Throws <see cref="FormatException"/> on malformed input.</summary>
    public static Node Parse(string json)
    {
        var pos = 0;
        var node = ParseNode(json, ref pos);
        return node;
    }

    /// <summary>Parses without throwing — for reading a snapshot that another process may have half-written.</summary>
    public static bool TryParse(string json, out Node? node)
    {
        try
        {
            node = Parse(json);
            return true;
        }
        catch (FormatException)
        {
            node = null;
            return false;
        }
    }

    static Node ParseNode(string s, ref int i)
    {
        Expect(s, ref i, '{');
        var node = new Node();

        while (true)
        {
            SkipWs(s, ref i);
            if (Peek(s, i) == '}') { i++; break; }

            var key = ParseString(s, ref i);
            SkipWs(s, ref i);
            Expect(s, ref i, ':');
            SkipWs(s, ref i);

            switch (key)
            {
                case "t":
                    // The writer drops the 'L' prefix; restore it so the tree matches what the DSL built.
                    node.Type = LongType(ParseString(s, ref i));
                    break;
                case "i":
                    node.Id = ParseString(s, ref i);
                    break;
                case "p":
                    ParseDict(s, ref i, node.Props);
                    break;
                case "m":
                    ParseModifiers(s, ref i, node.Modifiers);
                    break;
                case "c":
                    ParseChildren(s, ref i, node.Children);
                    break;
                default:
                    SkipValue(s, ref i);
                    break;
            }

            SkipWs(s, ref i);
            if (Peek(s, i) == ',') { i++; continue; }
        }

        return node;
    }

    static void ParseChildren(string s, ref int i, List<Node> into)
    {
        Expect(s, ref i, '[');
        SkipWs(s, ref i);
        if (Peek(s, i) == ']') { i++; return; }

        while (true)
        {
            SkipWs(s, ref i);
            into.Add(ParseNode(s, ref i));
            SkipWs(s, ref i);
            if (Peek(s, i) == ',') { i++; continue; }
            Expect(s, ref i, ']');
            return;
        }
    }

    static void ParseModifiers(string s, ref int i, List<Dictionary<string, object>> into)
    {
        Expect(s, ref i, '[');
        SkipWs(s, ref i);
        if (Peek(s, i) == ']') { i++; return; }

        while (true)
        {
            SkipWs(s, ref i);
            var dict = new Dictionary<string, object>();
            ParseDict(s, ref i, dict);
            // The writer shortens the discriminator to "t"; the rest of the framework reads "type".
            if (dict.Remove("t", out var type)) dict["type"] = type;
            into.Add(dict);
            SkipWs(s, ref i);
            if (Peek(s, i) == ',') { i++; continue; }
            Expect(s, ref i, ']');
            return;
        }
    }

    static void ParseDict(string s, ref int i, Dictionary<string, object> into)
    {
        Expect(s, ref i, '{');
        SkipWs(s, ref i);
        if (Peek(s, i) == '}') { i++; return; }

        while (true)
        {
            SkipWs(s, ref i);
            var key = ParseString(s, ref i);
            SkipWs(s, ref i);
            Expect(s, ref i, ':');
            SkipWs(s, ref i);
            into[key] = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (Peek(s, i) == ',') { i++; continue; }
            Expect(s, ref i, '}');
            return;
        }
    }

    static object ParseValue(string s, ref int i)
    {
        var c = Peek(s, i);
        switch (c)
        {
            case '"': return ParseString(s, ref i);
            case 't': Expect(s, ref i, 't', 'r', 'u', 'e'); return true;
            case 'f': Expect(s, ref i, 'f', 'a', 'l', 's', 'e'); return false;
            case 'n': Expect(s, ref i, 'n', 'u', 'l', 'l'); return "";
            default: return ParseNumber(s, ref i);
        }
    }

    static double ParseNumber(string s, ref int i)
    {
        var start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '.' or 'e' or 'E' or '-' or '+')) i++;

        var span = s.AsSpan(start, i - start);
        if (!double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            throw new FormatException($"Malformed number at {start}: '{span.ToString()}'.");
        return value;
    }

    static string ParseString(string s, ref int i)
    {
        Expect(s, ref i, '"');
        var sb = new StringBuilder();
        while (true)
        {
            if (i >= s.Length) throw new FormatException("Unterminated string.");
            var c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }

            if (i >= s.Length) throw new FormatException("Truncated escape.");
            var e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw new FormatException("Truncated \\u escape.");
                    sb.Append((char)ushort.Parse(s.AsSpan(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break;
            }
        }
    }

    static void SkipValue(string s, ref int i)
    {
        SkipWs(s, ref i);
        var c = Peek(s, i);
        if (c == '"') { ParseString(s, ref i); return; }
        if (c == '{') { ParseDict(s, ref i, new Dictionary<string, object>()); return; }
        if (c == '[')
        {
            var depth = 0;
            do
            {
                if (i >= s.Length) throw new FormatException("Unterminated array.");
                if (s[i] == '[') depth++;
                else if (s[i] == ']') depth--;
                else if (s[i] == '"') { ParseString(s, ref i); continue; }
                i++;
            } while (depth > 0);
            return;
        }
        ParseValue(s, ref i);
    }

    static void SkipWs(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static char Peek(string s, int i) => i < s.Length ? s[i] : '\0';

    static void Expect(string s, ref int i, params char[] expected)
    {
        foreach (var e in expected)
        {
            if (i >= s.Length || s[i] != e)
                throw new FormatException($"Expected '{e}' at {i}, found '{(i < s.Length ? s[i] : '\0')}'.");
            i++;
        }
    }

    internal static string LongType(string shortType) =>
        shortType.Length > 0 && shortType[0] != 'L' ? "L" + shortType : shortType;
}
