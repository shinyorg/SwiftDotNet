using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The <see cref="Grid"/> layout engine on Skia: explicit column tracks (Fixed/Star/Auto), column and
/// row spans, explicit <c>.GridCell</c> pinning, and per-axis spacing. See
/// <c>SkiaNode.MeasureGrid/ResolveTracks/ArrangeGrid</c> and <see cref="GridEngine"/>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaGridTrackTests
{
    const int W = 400, H = 800;

    static SkiaBridge Render(View view)
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);
        return bridge;
    }

    [Fact]
    public void FixedAndStarColumns_SizeIndependently()
    {
        var bridge = Render(new TrackView());

        Assert.True(bridge.TryGetFrame("0.0", out var a));
        Assert.True(bridge.TryGetFrame("0.1", out var b));
        Assert.True(bridge.TryGetFrame("0.2", out var c));

        // The three cells start at 0, 80+gap, and 80+gap+star+gap. Each shape is greedy, so its frame
        // *is* its cell: column 0 is exactly the Fixed(80) track.
        Assert.Equal(80, a.Width, 1);
        // Two Star(1) columns split what's left of 400 after the fixed track and two 10pt gaps.
        var expectedStar = (400f - 80 - 20) / 2;
        Assert.Equal(expectedStar, b.Width, 1);
        Assert.Equal(expectedStar, c.Width, 1);

        // Gaps land between the columns.
        Assert.Equal(a.Right + 10, b.Left, 1);
        Assert.Equal(b.Right + 10, c.Left, 1);
    }

    [Fact]
    public void ColumnSpan_CoversItsTracksAndGaps()
    {
        var bridge = Render(new SpanView());

        Assert.True(bridge.TryGetFrame("0.0", out var header));  // spans all 3 columns
        Assert.True(bridge.TryGetFrame("0.1", out var first));   // row 1, column 0
        Assert.True(bridge.TryGetFrame("0.3", out var third));   // row 1, column 2

        // The header covers column 0 through column 2 — its cell is the full grid width, so it starts
        // where column 0 does and ends where column 2 does.
        Assert.Equal(first.Left, header.Left, 1);
        Assert.Equal(third.Right, header.Right, 1);

        // …and the spanning child forced a second row: the flowed children sit below it.
        Assert.True(first.Top > header.Top, "row 1 should start below the spanning header");
    }

    [Fact]
    public void GridCell_PinsAChildAndOthersFlowAroundIt()
    {
        var bridge = Render(new PinnedView());

        Assert.True(bridge.TryGetFrame("0.0", out var pinned));  // pinned to column 1, row 0
        Assert.True(bridge.TryGetFrame("0.1", out var flowA));   // flows into column 0, row 0
        Assert.True(bridge.TryGetFrame("0.2", out var flowB));   // column 0 is taken → row 1

        Assert.Equal(pinned.Top, flowA.Top, 1);
        Assert.True(flowA.Left < pinned.Left, "the flowed child should take the free column 0");
        Assert.True(flowB.Top > flowA.Top, "with row 0 full, the next child wraps to row 1");
    }

    [Fact]
    public void GreedySpanningChild_DoesNotStarveTheStarColumn()
    {
        // A shape is greedy: it measures as the whole width offered. A spanning one therefore reports a
        // huge desired width, and the naive "grow the last content-sized track in the span" rule handed
        // all of it to the Auto column — collapsing the Star column to nothing. A span crossing a Star
        // track must instead leave the leftover to the star pass.
        var bridge = Render(new GreedySpanView());

        // The spanner fills row 0, so the flowed children start at row 1: Fixed | Star | Auto.
        Assert.True(bridge.TryGetFrame("0.1", out var fixedCol));
        Assert.True(bridge.TryGetFrame("0.2", out var star));
        Assert.True(bridge.TryGetFrame("0.3", out var auto));

        Assert.Equal(60, fixedCol.Width, 1);

        // Star gets everything not claimed by Fixed(60), the Auto text, and two 10pt gaps.
        Assert.True(star.Width > 200, $"star column collapsed to {star.Width}");
        Assert.True(auto.Width < 80, $"auto column swallowed the leftover ({auto.Width})");
    }

    [Fact]
    public void RowAndColumnSpacing_ApplyPerAxis()
    {
        var bridge = Render(new PerAxisSpacingView());

        Assert.True(bridge.TryGetFrame("0.0", out var a));
        Assert.True(bridge.TryGetFrame("0.1", out var b));
        Assert.True(bridge.TryGetFrame("0.2", out var c));

        Assert.Equal(a.Right + 24, b.Left, 1);  // ColumnSpacing(24)
        Assert.Equal(a.Bottom + 4, c.Top, 1);   // RowSpacing(4)
    }
}

file sealed class TrackView : View
{
    public override View? Body =>
        new Grid(
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20))
            .Columns(GridTrack.Fixed(80), GridTrack.Star(), GridTrack.Star())
            .Spacing(10);
}

file sealed class SpanView : View
{
    public override View? Body =>
        new Grid(3,
                new Rectangle().Frame(height: 20).GridSpan(columns: 3),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20))
            .Spacing(8);
}

file sealed class PinnedView : View
{
    public override View? Body =>
        new Grid(2,
                new Rectangle().Frame(height: 20).GridCell(column: 1, row: 0),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20))
            .Spacing(8);
}

file sealed class PerAxisSpacingView : View
{
    public override View? Body =>
        new Grid(2,
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20),
                new Rectangle().Frame(height: 20))
            .ColumnSpacing(24)
            .RowSpacing(4);
}

file sealed class GreedySpanView : View
{
    public override View? Body =>
        new Grid(
                new Rectangle().Frame(height: 20).GridSpan(columns: 3),   // greedy, spans everything
                new Rectangle().Frame(height: 20),                        // Fixed column
                new Rectangle().Frame(height: 20),                        // Star column
                new Text("Auto"))                                         // Auto column
            .Columns(GridTrack.Fixed(60), GridTrack.Star(), GridTrack.Auto)
            .Spacing(10);
}
