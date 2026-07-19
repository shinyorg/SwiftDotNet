using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// End-to-end selection on Skia: tapping a row in a <c>.Selection(...)</c> list emits the row's key,
/// which flows back to the bound state; tapping again clears it (single) and the tapped row carries a
/// <c>selected</c> prop. See <c>List.Selection</c>, <c>SkiaNode.HitTest</c> (List branch).
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaSelectionTests
{
    const int W = 400, H = 800;

    [Fact]
    public void SingleSelection_TapSelectsThenDeselects_AndMarksRow()
    {
        var selected = new State<string?>(null);
        var view = new SelectableView(new List<string> { "a", "b", "c" }, selected);
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        // Tap row "b" (index 1).
        Assert.True(bridge.TryGetFrame("0.1", out var b));
        host.Tap(b.MidX, b.MidY);
        Assert.Equal("b", selected.Value);

        // Tapping "b" again clears the selection (single-select toggle).
        host.RenderPng(W, H);
        Assert.True(bridge.TryGetFrame("0.1", out var b2));
        host.Tap(b2.MidX, b2.MidY);
        Assert.Null(selected.Value);
    }

    [Fact]
    public void MultipleSelection_AccumulatesKeys()
    {
        var selected = new State<HashSet<string>>(new HashSet<string>());
        var view = new MultiSelectableView(new List<string> { "a", "b", "c" }, selected);
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        Assert.True(bridge.TryGetFrame("0.0", out var a));
        host.Tap(a.MidX, a.MidY);
        host.RenderPng(W, H);
        Assert.True(bridge.TryGetFrame("0.2", out var c));
        host.Tap(c.MidX, c.MidY);

        Assert.Equal(new HashSet<string> { "a", "c" }, selected.Value);
    }
}

file sealed class SelectableView(List<string> items, State<string?> selected) : View
{
    public override View? Body => List.ForEach(items, x => x, x => new Text(x)).Selection(selected);
}

file sealed class MultiSelectableView(List<string> items, State<HashSet<string>> selected) : View
{
    public override View? Body => List.ForEach(items, x => x, x => new Text(x)).Selection(selected);
}
