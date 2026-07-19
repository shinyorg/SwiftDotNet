using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Locks in the keyed-<see cref="List"/> wire contract: keyed rows carry a stable <c>key</c> prop and a
/// <c>keyed</c> flag on the container, and mutating the collection (insert / remove / reorder) forces a
/// <c>setChildren</c> that preserves those keys — the signal every host uses to recycle rows by identity.
/// Unkeyed lists are unaffected. See <c>Core/Views/List.cs</c> and <c>Core/TreeDiffer.cs</c>.
///
/// Drives shared static runtime state, so it must not run in parallel with the other serial tests.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class ListKeyedTests
{
    [Fact]
    public void KeyedList_StampsKeyAndKeyedProps()
    {
        var view = new KeyedListView(new List<string> { "a", "b", "c" });
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);

        var root = ParseReplace(bridge.LastJson);
        var list = FindByType(root, "List");
        Assert.True(list.GetProperty("props").GetProperty("keyed").GetBoolean());
        Assert.Equal(new[] { "a", "b", "c" }, RowKeys(list));
    }

    [Fact]
    public void UnkeyedList_HasNoKeyOrKeyedProps()
    {
        var view = new UnkeyedListView(new List<string> { "a", "b" });
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);

        var root = ParseReplace(bridge.LastJson);
        var list = FindByType(root, "List");
        Assert.False(list.GetProperty("props").TryGetProperty("keyed", out _));
        foreach (var child in list.GetProperty("children").EnumerateArray())
            Assert.False(child.GetProperty("props").TryGetProperty("key", out _));
    }

    [Fact]
    public void KeyedList_Reorder_EmitsSetChildrenWithStableKeys()
    {
        var items = new List<string> { "a", "b", "c" };
        var view = new KeyedListView(items);
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);

        // Reorder to c, a, b — same count, same row type (Text), only identity/order changed.
        items.Clear();
        items.AddRange(new[] { "c", "a", "b" });
        view.Bump();

        var op = SingleOp(bridge.LastJson);
        Assert.Equal("setChildren", op.GetProperty("op").GetString());
        Assert.Equal(new[] { "c", "a", "b" }, op.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("props").GetProperty("key").GetString()).ToArray());
    }

    [Fact]
    public void KeyedList_Insert_EmitsSetChildrenIncludingNewKey()
    {
        var items = new List<string> { "a", "b" };
        var view = new KeyedListView(items);
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);

        items.Insert(0, "z");   // prepend
        view.Bump();

        var op = SingleOp(bridge.LastJson);
        Assert.Equal("setChildren", op.GetProperty("op").GetString());
        Assert.Equal(new[] { "z", "a", "b" }, op.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("props").GetProperty("key").GetString()).ToArray());
    }

    [Fact]
    public void KeyedList_ContentChangeWithStableOrder_UpdatesPropsNotSetChildren()
    {
        // Same keys/order, but a row's rendered text depends on external state that changes.
        var items = new List<Item> { new("a", 0), new("b", 0) };
        var view = new KeyedItemView(items);
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);

        items[1] = items[1] with { Count = 1 };   // key "b" stays put, only its text changes
        view.Bump();

        // Stable key sequence → the differ recurses positionally and emits a targeted updateProps,
        // NOT a whole-list setChildren.
        foreach (var op in Ops(bridge.LastJson))
            Assert.NotEqual("setChildren", op.GetProperty("op").GetString());
        Assert.Contains(Ops(bridge.LastJson), op => op.GetProperty("op").GetString() == "updateProps");
    }

    // ---- helpers -------------------------------------------------------------

    static JsonElement ParseReplace(string json)
    {
        var op = SingleOp(json);
        Assert.Equal("replace", op.GetProperty("op").GetString());
        return op.GetProperty("node");
    }

    static JsonElement SingleOp(string json)
    {
        var ops = Ops(json);
        return Assert.Single(ops);
    }

    static List<JsonElement> Ops(string json)
    {
        // Clone each op so the elements outlive the JsonDocument we dispose here.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("ops").EnumerateArray().Select(e => e.Clone()).ToList();
    }

    static string[] RowKeys(JsonElement list) => list.GetProperty("children").EnumerateArray()
        .Select(c => c.GetProperty("props").GetProperty("key").GetString()!).ToArray();

    static JsonElement FindByType(JsonElement node, string type)
    {
        if (node.GetProperty("type").GetString() == type) return node;
        foreach (var c in node.GetProperty("children").EnumerateArray())
        {
            var found = TryFind(c, type);
            if (found is { } f) return f;
        }
        throw new Xunit.Sdk.XunitException($"No node of type {type} found.");
    }

    static JsonElement? TryFind(JsonElement node, string type)
    {
        if (node.GetProperty("type").GetString() == type) return node;
        foreach (var c in node.GetProperty("children").EnumerateArray())
            if (TryFind(c, type) is { } f) return f;
        return null;
    }
}

file readonly record struct Item(string Id, int Count);

/// <summary>A keyed list over a mutable string collection; <see cref="Bump"/> forces a re-render.</summary>
file sealed class KeyedListView(List<string> items) : View
{
    readonly State<int> _tick = new(0);
    public void Bump() => SwiftApp.Transaction(() => _tick.Value++);
    public override View? Body => Touch(_tick) is var _
        ? List.ForEach(items, x => x, x => new Text(x))
        : null;
    static int Touch(State<int> s) => s.Value;
}

file sealed class UnkeyedListView(List<string> items) : View
{
    public override View? Body => List.ForEach(items, x => new Text(x));
}

file sealed class KeyedItemView(List<Item> items) : View
{
    readonly State<int> _tick = new(0);
    public void Bump() => SwiftApp.Transaction(() => _tick.Value++);
    public override View? Body => Touch(_tick) is var _
        ? List.ForEach(items, x => x.Id, x => new Text($"{x.Id}:{x.Count}"))
        : null;
    static int Touch(State<int> s) => s.Value;
}

/// <summary>An <see cref="IBridge"/> that keeps the most recent patch JSON for inspection.</summary>
file sealed class CapturingBridge : IBridge
{
    public string LastJson { get; private set; } = "";
    public void Render(string json) => LastJson = json;
    public void SetEventHandler(Action<string, string?> handler) { }
}
