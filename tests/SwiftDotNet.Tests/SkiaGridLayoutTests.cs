using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// A <see cref="List"/> with <c>.Columns(n)</c> lays its rows out as an n-column grid on Skia: row 0 and
/// row 1 sit side by side (same y, increasing x), and row n starts a new line below. See
/// <c>SkiaNode.MeasureScrollableGrid/ArrangeScrollableGrid</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaGridLayoutTests
{
    const int W = 400, H = 800;

    [Fact]
    public void GridList_PlacesRowsInColumns()
    {
        var view = new GridView(Enumerable.Range(0, 6).Select(i => i.ToString()).ToList());
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        Assert.True(bridge.TryGetFrame("0.0", out var c0));
        Assert.True(bridge.TryGetFrame("0.1", out var c1));
        Assert.True(bridge.TryGetFrame("0.2", out var c2));

        // Cell 0 and cell 1 are on the same row (equal top), cell 1 is to the right of cell 0.
        Assert.Equal(c0.Top, c1.Top, 1);
        Assert.True(c1.Left > c0.Left, "second column should be right of the first");

        // With 3 columns, cell 2 stays on row 0; cell 3 wraps to row 1 (below cell 0).
        Assert.True(bridge.TryGetFrame("0.3", out var c3));
        Assert.Equal(c0.Top, c2.Top, 1);
        Assert.True(c3.Top > c0.Top, "fourth item should wrap to the next grid row");
    }
}

file sealed class GridView(List<string> items) : View
{
    public override View? Body =>
        List.ForEach(items, x => x, x => new Text(x)).Columns(3);
}
