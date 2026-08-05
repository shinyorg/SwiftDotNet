using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The backend-independent half of grid/absolute layout: track parsing, cell placement, the
/// proportional-bounds rule, and the wire shape all seven backends decode.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class GridEngineTests
{
    // ---- track parsing --------------------------------------------------------

    [Fact]
    public void ParseTracks_RoundTripsEveryKind()
    {
        var tracks = GridEngine.ParseTracks("fixed:80,star:2.5,auto,flex:40:120,flex:10:inf", 1);

        Assert.Equal(5, tracks.Length);
        Assert.Equal(GridTrackKind.Fixed, tracks[0].Kind);
        Assert.Equal(80, tracks[0].Value);
        Assert.Equal(GridTrackKind.Star, tracks[1].Kind);
        Assert.Equal(2.5, tracks[1].Value);
        Assert.Equal(GridTrackKind.Auto, tracks[2].Kind);
        Assert.Equal(GridTrackKind.Flexible, tracks[3].Kind);
        Assert.Equal(40, tracks[3].Value);
        Assert.Equal(120, tracks[3].Max);
        Assert.Null(tracks[4].Max);   // "inf" is unbounded
    }

    [Fact]
    public void ParseTracks_FallsBackToEqualStars()
    {
        foreach (var spec in new string?[] { null, "" })
        {
            var tracks = GridEngine.ParseTracks(spec, 3);
            Assert.Equal(3, tracks.Length);
            Assert.All(tracks, t => Assert.Equal(GridTrackKind.Star, t.Kind));
        }
    }

    [Fact]
    public void ParseTracks_TreatsAnUnknownTokenAsAuto()
    {
        var tracks = GridEngine.ParseTracks("wat,fixed:10", 1);
        Assert.Equal(GridTrackKind.Auto, tracks[0].Kind);
        Assert.Equal(GridTrackKind.Fixed, tracks[1].Kind);
    }

    // ---- placement ------------------------------------------------------------

    [Fact]
    public void Place_FlowsInReadingOrder()
    {
        var spans = GridEngine.Place(2, Flow(4), out var rows);

        Assert.Equal(2, rows);
        Assert.Equal((0, 0), (spans[0].Column, spans[0].Row));
        Assert.Equal((1, 0), (spans[1].Column, spans[1].Row));
        Assert.Equal((0, 1), (spans[2].Column, spans[2].Row));
        Assert.Equal((1, 1), (spans[3].Column, spans[3].Row));
    }

    [Fact]
    public void Place_WrapsAWideChildToItsOwnRow()
    {
        // A 2-column span can't share row 0 with anything in a 3-column grid once one cell is taken.
        var spans = GridEngine.Place(3, new (int?, int?, int, int)[]
        {
            (null, null, 1, 1),
            (null, null, 3, 1),
            (null, null, 1, 1),
        }, out var rows);

        Assert.Equal((0, 0), (spans[0].Column, spans[0].Row));
        Assert.Equal((0, 1), (spans[1].Column, spans[1].Row));   // needs all 3 columns → next row
        Assert.Equal((0, 2), (spans[2].Column, spans[2].Row));
        Assert.Equal(3, rows);
    }

    [Fact]
    public void Place_FlowsAroundAPinnedChild()
    {
        var spans = GridEngine.Place(2, new (int?, int?, int, int)[]
        {
            (1, 0, 1, 1),          // pinned to the right of row 0
            (null, null, 1, 1),
            (null, null, 1, 1),
        }, out _);

        Assert.Equal((1, 0), (spans[0].Column, spans[0].Row));
        Assert.Equal((0, 0), (spans[1].Column, spans[1].Row));   // takes the free cell beside it
        Assert.Equal((0, 1), (spans[2].Column, spans[2].Row));   // row 0 is now full
    }

    [Fact]
    public void Place_ReservesEveryCellOfARowSpan()
    {
        var spans = GridEngine.Place(2, new (int?, int?, int, int)[]
        {
            (0, 0, 1, 2),          // occupies column 0 of rows 0 and 1
            (null, null, 1, 1),
            (null, null, 1, 1),
        }, out var rows);

        Assert.Equal((1, 0), (spans[1].Column, spans[1].Row));
        Assert.Equal((1, 1), (spans[2].Column, spans[2].Row));   // column 0 of row 1 is still taken
        Assert.Equal(2, rows);
    }

    [Fact]
    public void Place_ClampsASpanWiderThanTheGrid()
    {
        var spans = GridEngine.Place(2, new (int?, int?, int, int)[] { (null, null, 9, 1) }, out _);

        Assert.Equal(0, spans[0].Column);
        Assert.Equal(2, spans[0].ColumnSpan);   // never wider than the grid
    }

    static (int?, int?, int, int)[] Flow(int count)
    {
        var r = new (int?, int?, int, int)[count];
        for (var i = 0; i < count; i++) r[i] = (null, null, 1, 1);
        return r;
    }

    // ---- absolute bounds ------------------------------------------------------

    [Fact]
    public void Resolve_PointBoundsPassThrough()
    {
        var r = AbsoluteLayoutBounds.Resolve(10, 20, 60, 30, LayoutFlags.None, 400, 200, 0, 0);
        Assert.Equal((10, 20, 60, 30), r);
    }

    [Fact]
    public void Resolve_ProportionalSizeIsAFractionOfTheHost()
    {
        var r = AbsoluteLayoutBounds.Resolve(0, 0, 0.5, 0.25, LayoutFlags.SizeProportional, 400, 200, 0, 0);
        Assert.Equal(200, r.Width);
        Assert.Equal(50, r.Height);
    }

    [Theory]
    [InlineData(0.0, 0.0)]      // flush leading
    [InlineData(0.5, 160.0)]    // centered: (400-80)/2
    [InlineData(1.0, 320.0)]    // flush trailing, still on screen
    public void Resolve_ProportionalPositionAnchorsAcrossTheFreeSpace(double x, double expected)
    {
        var r = AbsoluteLayoutBounds.Resolve(x, 0, 80, 20, LayoutFlags.XProportional, 400, 200, 0, 0);
        Assert.Equal(expected, r.X);
    }

    [Fact]
    public void Resolve_AutoSizeFallsBackToTheMeasuredSize()
    {
        var r = AbsoluteLayoutBounds.Resolve(5, 5, null, null, LayoutFlags.None, 400, 200, 33, 11);
        Assert.Equal(33, r.Width);
        Assert.Equal(11, r.Height);
    }

    [Fact]
    public void ParseFlags_RoundTripsTheToken()
    {
        Assert.Equal(LayoutFlags.None, AbsoluteLayoutBounds.Parse(null));
        Assert.Equal(LayoutFlags.None, AbsoluteLayoutBounds.Parse(""));
        Assert.Equal(LayoutFlags.All, AbsoluteLayoutBounds.Parse("xywh"));
        Assert.Equal(LayoutFlags.PositionProportional, AbsoluteLayoutBounds.Parse("xy"));
        Assert.Equal(LayoutFlags.HeightProportional, AbsoluteLayoutBounds.Parse("h"));
    }

    // ---- wire shape -----------------------------------------------------------

    [Fact]
    public void Grid_SerializesItsTracksSpacingAndAlignment()
    {
        var json = Wire(new Grid(2, new Text("a"))
            .Columns(GridTrack.Fixed(80), GridTrack.Star(2), GridTrack.Auto)
            .Rows(GridTrack.Fixed(40))
            .ColumnSpacing(12)
            .RowSpacing(4)
            .Alignment(Alignment.Leading));

        Assert.Contains("\"columns\":3", json);          // the track list redefines the column count
        Assert.Contains("\"columnTracks\":\"fixed:80,star:2,auto\"", json);
        Assert.Contains("\"rowTracks\":\"fixed:40\"", json);
        Assert.Contains("\"columnSpacing\":12", json);
        Assert.Contains("\"rowSpacing\":4", json);
        Assert.Contains("\"alignment\":\"leading\"", json);
    }

    [Fact]
    public void Grid_WithoutTracks_StaysOnTheOldWireShape()
    {
        var json = Wire(new Grid(3, new Text("a")).Spacing(8));

        Assert.Contains("\"columns\":3", json);
        Assert.Contains("\"spacing\":8", json);
        Assert.DoesNotContain("columnTracks", json);
        Assert.DoesNotContain("rowTracks", json);
    }

    [Fact]
    public void GridCellAndGridSpan_MergeIntoOneModifier()
    {
        var json = Wire(new Grid(2, new Text("a").GridCell(column: 1, row: 2).GridSpan(columns: 2, rows: 3)));

        // One gridCell modifier, carrying the last value set for each field.
        Assert.Equal(1, Occurrences(json, "\"type\":\"gridCell\""));
        Assert.Contains("\"column\":1", json);
        Assert.Contains("\"row\":2", json);
        Assert.Contains("\"columnSpan\":2", json);
        Assert.Contains("\"rowSpan\":3", json);
    }

    [Fact]
    public void GridSpan_OmitsTheDefaultSpans()
    {
        var json = Wire(new Grid(2, new Text("a").GridSpan(columns: 2)));

        Assert.Contains("\"columnSpan\":2", json);
        Assert.DoesNotContain("rowSpan", json);   // a span of 1 is the default and stays off the wire
    }

    [Fact]
    public void LayoutBounds_SerializesOnlyWhatWasDeclared()
    {
        var json = Wire(new AbsoluteLayout(
            new Text("a").LayoutBounds(4, 8),
            new Text("b").LayoutBounds(0.5, 0, 100, AbsoluteLayout.AutoSize, LayoutFlags.XProportional)));

        Assert.Contains("\"type\":\"AbsoluteLayout\"", json);
        // Child a: position only — no width/height, no flags.
        Assert.Contains("{\"type\":\"layoutBounds\",\"x\":4,\"y\":8}", json);
        // Child b: an auto height drops off the wire; the flag token names the proportional axis.
        Assert.Contains("\"width\":100", json);
        Assert.Contains("\"flags\":\"x\"", json);
        Assert.DoesNotContain("\"height\":", json);
    }

    /// <summary>The JSON a backend actually receives for <paramref name="view"/>.</summary>
    static string Wire(View view)
    {
        var bridge = new GridCapturingBridge();
        SwiftApp.Run(view, bridge);
        return bridge.LastJson!;
    }

    static int Occurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }
}

file sealed class GridCapturingBridge : IBridge
{
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) { }
    public void Render(string json) => LastJson = json;
}
