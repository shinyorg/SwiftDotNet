using SwiftDotNet;
using Xunit;
using XenoAtom.Terminal.UI.Rendering;

using TTheme = XenoAtom.Terminal.UI.Styling.Theme;

namespace SwiftDotNet.Tests;

/// <summary>
/// <see cref="Grid"/> tracks/spans and <see cref="AbsoluteLayout"/> rendered to real terminal cells.
/// Terminal.UI's own Grid does the track sizing; what's tested here is that the wire tracks, the
/// <see cref="GridEngine"/> placement, and <see cref="TuiAbsolute"/>'s arrange all reach it intact.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class TuiGridAbsoluteTests
{
    [Fact]
    public void GridColumns_PutSiblingsOnOneRowAndWrapTheThird()
    {
        var plain = Render(new TwoColumnGridView(), width: 30, height: 6).Select(Plain).ToList();

        // "aa" and "bb" share a row (two columns); "cc" wraps to the next one.
        var row0 = Assert.Single(plain, l => l.Contains("aa"));
        Assert.Contains("bb", row0);
        var row1 = Assert.Single(plain, l => l.Contains("cc"));
        Assert.DoesNotContain("aa", row1);
        Assert.True(plain.IndexOf(row1) > plain.IndexOf(row0), "the third cell should wrap below");
    }

    [Fact]
    public void ColumnSpan_KeepsTheSpanningChildOnItsOwnRow()
    {
        var plain = Render(new SpanningGridView(), width: 30, height: 6).Select(Plain).ToList();

        var header = Assert.Single(plain, l => l.Contains("header"));
        var body = Assert.Single(plain, l => l.Contains("aa"));

        // The header covers both columns, so nothing else can share its row.
        Assert.DoesNotContain("aa", header);
        Assert.Contains("bb", body);
        Assert.True(plain.IndexOf(body) > plain.IndexOf(header));
    }

    [Fact]
    public void FixedColumn_IsNarrowerThanItsStarNeighbour()
    {
        var plain = Render(new FixedColumnGridView(), width: 40, height: 4).Select(Plain).ToList();
        var row = Assert.Single(plain, l => l.Contains("L") && l.Contains("R"));

        // Fixed(16) is 2 cells at the default 8px cell width, so the star column takes the rest and the
        // right-hand label starts near the left edge, not half way across.
        var right = row.IndexOf('R');
        Assert.InRange(right, 1, 8);
    }

    [Fact]
    public void AbsoluteLayout_PlacesChildrenAtTheirDeclaredCells()
    {
        var plain = Render(new AbsoluteTuiView(), width: 30, height: 8).Select(Plain).ToList();

        // .LayoutBounds(0, 0) → row 0; .LayoutBounds(80, 32) → 10 cells across, 2 rows down.
        Assert.Contains("origin", plain[0]);
        var offset = Assert.Single(plain, l => l.Contains("moved"));
        Assert.Equal(2, plain.IndexOf(offset));
        Assert.Equal(10, offset.IndexOf("moved", StringComparison.Ordinal));
    }

    [Fact]
    public void AbsoluteLayout_ProportionalPositionAnchorsToTheFarEdge()
    {
        var plain = Render(new ProportionalTuiView(), width: 30, height: 6).Select(Plain).ToList();
        var row = Assert.Single(plain, l => l.Contains("end"));

        // x: 1 anchors the child's trailing edge to the panel's, so "end" finishes at the last column.
        Assert.Equal(30, row.TrimEnd().Length);
        Assert.EndsWith("end", row.TrimEnd());
    }

    static IReadOnlyList<string> Render(View view, int width, int height)
    {
        var bridge = new TuiBridge();
        SwiftApp.Run(view, bridge);
        return VisualSnapshotRenderer.Render(bridge.Host, width, height, TTheme.Default).ToMarkupLines();
    }

    static string Plain(string markup)
        => System.Text.RegularExpressions.Regex.Replace(markup, @"\[[^\]]*\]", "");
}

file sealed class TwoColumnGridView : View
{
    public override View Body =>
        new Grid(2, new Text("aa"), new Text("bb"), new Text("cc")).Spacing(0);
}

file sealed class SpanningGridView : View
{
    public override View Body =>
        new Grid(2,
                new Text("header").GridSpan(columns: 2),
                new Text("aa"),
                new Text("bb"))
            .Spacing(0);
}

file sealed class FixedColumnGridView : View
{
    public override View Body =>
        new Grid(new Text("L"), new Text("R"))
            .Columns(GridTrack.Fixed(16), GridTrack.Star())
            .Spacing(0);
}

file sealed class AbsoluteTuiView : View
{
    public override View Body =>
        new AbsoluteLayout(
            new Text("origin").LayoutBounds(0, 0),
            new Text("moved").LayoutBounds(80, 32));
}

file sealed class ProportionalTuiView : View
{
    public override View Body =>
        new AbsoluteLayout(
            new Text("end").LayoutBounds(1, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize,
                LayoutFlags.XProportional));
}
