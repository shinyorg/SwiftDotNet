using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Header / footer / empty slots on <see cref="List"/>: they are emitted as ordered child nodes carrying a
/// <c>role</c> prop, the empty view appears only when there are no rows, and (crucially) slot children do
/// NOT drop the keyed fast-path. See <c>Core/Views/List.cs</c> and <c>TreeDiffer.RowKeys</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class ListSlotsTests
{
    [Fact]
    public void HeaderAndFooter_WrapRows_InOrder()
    {
        var view = new SlotView(new List<string> { "a", "b" }, empty: false);
        var bridge = new CapturingBridge2();
        SwiftApp.Run(view, bridge);

        var roles = ChildRoles(bridge.LastJson);
        Assert.Equal(new[] { "header", null, null, "footer" }, roles);
    }

    [Fact]
    public void EmptyView_ShownOnlyWhenNoRows()
    {
        var withRows = new SlotView(new List<string> { "a" }, empty: true);
        var b1 = new CapturingBridge2();
        SwiftApp.Run(withRows, b1);
        Assert.DoesNotContain("empty", ChildRoles(b1.LastJson));

        var noRows = new SlotView(new List<string>(), empty: true);
        var b2 = new CapturingBridge2();
        SwiftApp.Run(noRows, b2);
        Assert.Contains("empty", ChildRoles(b2.LastJson));
    }

    [Fact]
    public void WithHeader_ReorderStillEmitsSetChildren()
    {
        // A header (non-keyed slot) must not force setChildren on unrelated renders, but a real row
        // reorder still must. This asserts the reorder path survives the presence of a slot child.
        var items = new List<string> { "a", "b", "c" };
        var view = new SlotView(items, empty: false);
        var bridge = new CapturingBridge2();
        SwiftApp.Run(view, bridge);

        items.Clear();
        items.AddRange(new[] { "c", "b", "a" });
        view.Bump();

        using var doc = JsonDocument.Parse(bridge.LastJson);
        var ops = doc.RootElement.GetProperty("ops").EnumerateArray().ToList();
        Assert.Contains(ops, op => op.GetProperty("op").GetString() == "setChildren");
    }

    static List<string?> ChildRoles(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var op = doc.RootElement.GetProperty("ops").EnumerateArray().First();
        var node = op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op;
        var list = FindList(node);
        return list.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("props").TryGetProperty("role", out var r) ? r.GetString() : null)
            .ToList();
    }

    static JsonElement FindList(JsonElement node)
    {
        if (node.GetProperty("type").GetString() == "List") return node.Clone();
        foreach (var c in node.GetProperty("children").EnumerateArray())
            if (c.GetProperty("type").GetString() == "List" || HasList(c)) return FindList(c);
        throw new Xunit.Sdk.XunitException("no List node");
    }

    static bool HasList(JsonElement node)
    {
        if (node.GetProperty("type").GetString() == "List") return true;
        foreach (var c in node.GetProperty("children").EnumerateArray())
            if (HasList(c)) return true;
        return false;
    }
}

file sealed class SlotView : View
{
    readonly List<string> _items;
    readonly bool _empty;
    readonly State<int> _tick = new(0);

    public SlotView(List<string> items, bool empty) { _items = items; _empty = empty; }

    public void Bump() => SwiftApp.Transaction(() => _tick.Value++);

    public override View? Body
    {
        get
        {
            _ = _tick.Value;
            var list = List.ForEach(_items, x => x, x => new Text(x))
                .Header(new Text("HEADER"))
                .Footer(new Text("FOOTER"));
            if (_empty) list = list.Empty(new Text("Nothing here"));
            return list;
        }
    }
}

file sealed class CapturingBridge2 : IBridge
{
    public string LastJson { get; private set; } = "";
    public void Render(string json) => LastJson = json;
    public void SetEventHandler(Action<string, string?> handler) { }
}
