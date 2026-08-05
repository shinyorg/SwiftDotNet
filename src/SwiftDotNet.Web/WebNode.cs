using System.Text.Json;

namespace SwiftDotNet;

/// <summary>A node in the render tree for the web backend (plain data; Blazor renders it to HTML).</summary>
sealed class WebNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; set; } = new();
    public List<WebNode> Children { get; set; } = new();

    public string S(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    public string? SN(string key) => Props.TryGetValue(key, out var v) ? v as string : null;
    public double? N(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    public bool B(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;

    /// <summary>The first modifier of <paramref name="type"/>, or null. Matches the "first wins" rule the
    /// other backends use, so a doubled modifier degrades the same way everywhere.</summary>
    public Dictionary<string, object?>? Mod(string type)
    {
        foreach (var m in Modifiers)
            if (m.GetValueOrDefault("type") as string == type) return m;
        return null;
    }

    public static double? MN(Dictionary<string, object?>? m, string key) =>
        m is not null && m.TryGetValue(key, out var v) && v is double d ? d : null;

    /// <summary>This node's <c>gridCell</c> placement request (nulls mean "flow me").</summary>
    public (int? Column, int? Row, int ColumnSpan, int RowSpan) GridCellSpec()
    {
        var m = Mod("gridCell");
        if (m is null) return (null, null, 1, 1);
        return (
            MN(m, "column") is { } c ? (int)c : null,
            MN(m, "row") is { } r ? (int)r : null,
            MN(m, "columnSpan") is { } cs ? Math.Max(1, (int)cs) : 1,
            MN(m, "rowSpan") is { } rs ? Math.Max(1, (int)rs) : 1);
    }

    public static WebNode Parse(JsonElement e)
    {
        var node = new WebNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        foreach (var c in e.GetProperty("children").EnumerateArray())
            node.Children.Add(Parse(c));
        return node;
    }

    public WebNode? Find(string id)
    {
        var parts = id.Split('.');
        if (parts[0] != Id) return null;
        var node = this;
        for (var i = 1; i < parts.Length; i++)
        {
            var idx = int.Parse(parts[i]);
            if (idx < 0 || idx >= node.Children.Count) return null;
            node = node.Children[idx];
        }
        return node;
    }

    static Dictionary<string, object?> ReadDict(JsonElement e)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in e.EnumerateObject())
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        return d;
    }

    static List<Dictionary<string, object?>> ReadDictArray(JsonElement e)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var item in e.EnumerateArray()) list.Add(ReadDict(item));
        return list;
    }
}
