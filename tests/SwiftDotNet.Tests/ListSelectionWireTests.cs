using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Wire contract for selection: the List node carries <c>selectionMode</c>, and exactly the selected
/// rows carry <c>selected=true</c>. See <c>List.Selection</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class ListSelectionWireTests
{
    [Fact]
    public void SelectedRow_CarriesSelectedProp_AndListCarriesMode()
    {
        var selected = new State<string?>("b");
        var view = new WireSelView(new List<string> { "a", "b", "c" }, selected);
        var bridge = new WireBridge();
        SwiftApp.Run(view, bridge);

        using var doc = JsonDocument.Parse(bridge.LastJson);
        var node = doc.RootElement.GetProperty("ops").EnumerateArray().First().GetProperty("node");
        Assert.Equal("single", node.GetProperty("props").GetProperty("selectionMode").GetString());

        var flags = node.GetProperty("children").EnumerateArray()
            .Select(c => c.GetProperty("props").TryGetProperty("selected", out var s) && s.GetBoolean())
            .ToArray();
        Assert.Equal(new[] { false, true, false }, flags);
    }
}

file sealed class WireSelView(List<string> items, State<string?> selected) : View
{
    public override View? Body => List.ForEach(items, x => x, x => new Text(x)).Selection(selected);
}

file sealed class WireBridge : IBridge
{
    public string LastJson { get; private set; } = "";
    public void Render(string json) => LastJson = json;
    public void SetEventHandler(Action<string, string?> handler) { }
}
