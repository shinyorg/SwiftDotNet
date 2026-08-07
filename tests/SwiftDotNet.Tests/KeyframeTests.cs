using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The Core keyframe model: the wire encoding every backend decodes, the sampler the C# backends share,
/// and the clock that turns elapsed seconds into a phase. These are the contract the Swift and Kotlin
/// bridges are hand-written against, so they're pinned here rather than left to the backends.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class KeyframeWireTests
{
    [Fact]
    public void Encodes_TracksStopsAndPerStopCurves()
    {
        var m = Keyframes(new Text("x").Keyframes(k => k
            .Track(Prop.Opacity, t => t.At(0, 1).At(0.5, 0.3, Anim.EaseOut()).At(1, 1))
            .Track(Prop.Scale, t => t.At(0, 1).At(0.6, 1.2, Anim.Spring()).At(1, 1))
            .Duration(1.2)));

        Assert.Equal("opacity:0,1;0.5,0.3,easeOut;1,1|scale:0,1;0.6,1.2,spring;1,1", m.GetProperty("tracks").GetString());
        Assert.Equal(1.2, m.GetProperty("duration").GetDouble());
        Assert.Equal("easeInOut", m.GetProperty("curve").GetString());
        // A non-repeating timeline carries no repeat keys at all, so a backend can branch on presence.
        Assert.False(m.TryGetProperty("repeatCount", out _));
    }

    [Fact]
    public void Repeating_CarriesCountAndDirection()
    {
        var m = Keyframes(new Text("x").Keyframes(k => k
            .Track(Prop.Rotation, t => t.At(0, 0).At(1, 360))
            .Repeating(autoreverse: true)));

        Assert.Equal(-1d, m.GetProperty("repeatCount").GetDouble());
        Assert.Equal("true", m.GetProperty("autoreverse").GetString());
    }

    [Fact]
    public void StopsAreSortedByTime_NotDeclarationOrder()
    {
        var m = Keyframes(new Text("x").Keyframes(k => k
            .Track(Prop.Opacity, t => t.At(1, 0).At(0, 1).At(0.5, 0.5))));

        Assert.Equal("opacity:0,1;0.5,0.5;1,0", m.GetProperty("tracks").GetString());
    }

    [Fact]
    public void EmptyTimeline_AddsNoModifier()
    {
        var node = Render(new Text("x").Keyframes(_ => { }));
        Assert.DoesNotContain(
            node.GetProperty("modifiers").EnumerateArray(),
            m => m.GetProperty("type").GetString() == "keyframes");
    }

    [Fact]
    public void Parse_RoundTripsTheEncoding()
    {
        var tracks = KeyframeWire.Parse("opacity:0,1;0.5,0.3,easeOut;1,1|scale:0,1;1,2");

        Assert.Equal(2, tracks.Count);
        Assert.Equal("opacity", tracks[0].Property);
        Assert.Equal(3, tracks[0].Stops.Count);
        Assert.Equal(0.3, tracks[0].Stops[1].Value);
        Assert.Equal(AnimationCurve.EaseOut, tracks[0].Stops[1].Curve);
        Assert.Null(tracks[0].Stops[0].Curve);   // no per-stop curve → the timeline default applies
        Assert.Equal("scale", tracks[1].Property);
    }

    [Fact]
    public void Parse_SkipsMalformedSegmentsInsteadOfThrowing()
    {
        // A bad stop must not take down a render — the good tracks still come through.
        var tracks = KeyframeWire.Parse("opacity:junk;0,1;1,0|garbage|scale:0,1");

        Assert.Equal(2, tracks.Count);
        Assert.Equal(2, tracks[0].Stops.Count);
        Assert.Equal("scale", tracks[1].Property);
    }

    [Fact]
    public void Sample_InterpolatesBetweenStopsAndClampsOutsideThem()
    {
        var stops = KeyframeWire.Parse("o:0.25,0;0.75,1")[0].Stops;

        Assert.Equal(0, KeyframeWire.Sample(stops, 0, AnimationCurve.Linear));      // before the first stop
        Assert.Equal(0.5, KeyframeWire.Sample(stops, 0.5, AnimationCurve.Linear), 3);
        Assert.Equal(1, KeyframeWire.Sample(stops, 1, AnimationCurve.Linear));      // after the last
    }

    [Fact]
    public void Sample_UsesTheArrivingStopsCurve()
    {
        // Linear default, but the second stop demands easeIn — at the midpoint that reads 0.25, not 0.5.
        var stops = KeyframeWire.Parse("o:0,0;1,1,easeIn")[0].Stops;
        Assert.Equal(0.25, KeyframeWire.Sample(stops, 0.5, AnimationCurve.Linear), 3);
    }

    [Fact]
    public void Sample_CoincidentStopsAreAHardCut()
    {
        var stops = KeyframeWire.Parse("o:0,0;0.5,0;0.5,1;1,1")[0].Stops;
        Assert.Equal(0, KeyframeWire.Sample(stops, 0.4, AnimationCurve.Linear), 3);
        Assert.Equal(1, KeyframeWire.Sample(stops, 0.6, AnimationCurve.Linear), 3);
    }

    [Fact]
    public void Phase_PlayOnce_RunsToOneThenFinishes()
    {
        Assert.Equal(0.5, KeyframeWire.Phase(0.5, 1, 0, null, false, out var mid), 3);
        Assert.False(mid);

        Assert.Equal(1, KeyframeWire.Phase(1.5, 1, 0, null, false, out var done), 3);
        Assert.True(done);
    }

    [Fact]
    public void Phase_HoldsAtZeroThroughTheDelay()
    {
        Assert.Equal(0, KeyframeWire.Phase(0.4, 1, 0.5, null, false, out _), 3);
        Assert.Equal(0.5, KeyframeWire.Phase(1.0, 1, 0.5, null, false, out _), 3);
    }

    [Fact]
    public void Phase_ForeverLoop_NeverFinishesAndSawtooths()
    {
        Assert.Equal(0.25, KeyframeWire.Phase(3.25, 1, 0, -1, false, out var done), 3);
        Assert.False(done);
    }

    [Fact]
    public void Phase_Autoreverse_RunsOddCyclesBackwards()
    {
        Assert.Equal(0.25, KeyframeWire.Phase(0.25, 1, 0, -1, true, out _), 3);   // cycle 0, forward
        Assert.Equal(0.75, KeyframeWire.Phase(1.25, 1, 0, -1, true, out _), 3);   // cycle 1, reversed
        Assert.Equal(0.25, KeyframeWire.Phase(2.25, 1, 0, -1, true, out _), 3);   // cycle 2, forward again
    }

    [Fact]
    public void Phase_FiniteAutoreverse_SettlesBackWhereItStarted()
    {
        // Two autoreversing cycles = there and back twice, so it ends on the track's *first* value.
        Assert.Equal(0, KeyframeWire.Phase(5, 1, 0, 2, true, out var done), 3);
        Assert.True(done);
    }

    // ---- Bake: the flattening the CSS backends (Web, GTK) build their rules from ----

    [Fact]
    public void Bake_GivesEveryPropertyAValueAtEveryStop()
    {
        // The two tracks declare stops at different times; a CSS rule needs both properties at each.
        var baked = KeyframeWire.Bake(KeyframeWire.Parse("opacity:0,1;1,0|scale:0,1;0.5,2;1,1"), AnimationCurve.Linear);

        Assert.All(baked, stop => Assert.Equal(2, stop.Values.Count));
        Assert.Contains(baked, s => Math.Abs(s.Time - 0.5) < 1e-9);
        var mid = baked.First(s => Math.Abs(s.Time - 0.5) < 1e-9);
        Assert.Equal(0.5, mid.Values.First(v => v.Property == "opacity").Value, 3);
        Assert.Equal(2, mid.Values.First(v => v.Property == "scale").Value, 3);
    }

    [Fact]
    public void Bake_LinearTimelineNeedsNoExtraStops()
    {
        // Nothing to approximate — the browser's own linear interpolation is already exact.
        var baked = KeyframeWire.Bake(KeyframeWire.Parse("opacity:0,1;1,0"), AnimationCurve.Linear);
        Assert.Equal(2, baked.Count);
    }

    [Fact]
    public void Bake_SubdividesCurvedSegmentsSoTheEaseSurvives()
    {
        var baked = KeyframeWire.Bake(KeyframeWire.Parse("opacity:0,0;1,1,easeIn"), AnimationCurve.Linear);

        Assert.True(baked.Count > 2, "an eased segment must be sampled, not straightened to two endpoints");
        // The sampled shape is the ease itself: easeIn is t² , so the halfway sample sits at 0.25.
        var mid = baked.First(s => Math.Abs(s.Time - 0.5) < 1e-9);
        Assert.Equal(0.25, mid.Values[0].Value, 3);
    }

    [Fact]
    public void RuleName_IsStablePerTimelineAndDiffersAcrossThem()
    {
        Assert.Equal(KeyframeWire.RuleName("opacity:0,1;1,0"), KeyframeWire.RuleName("opacity:0,1;1,0"));
        Assert.NotEqual(KeyframeWire.RuleName("opacity:0,1;1,0"), KeyframeWire.RuleName("opacity:0,1;1,0.5"));
        Assert.StartsWith("sdn-kf-", KeyframeWire.RuleName("opacity:0,1;1,0"));
    }

    // ---- harness -------------------------------------------------------------

    static JsonElement Keyframes(View view) =>
        Render(view).GetProperty("modifiers").EnumerateArray()
            .First(m => m.GetProperty("type").GetString() == "keyframes");

    static JsonElement Render(View view)
    {
        var bridge = new CapturingKeyframeBridge();
        SwiftApp.Run(new KeyframeHost(view), bridge);
        var op = JsonDocument.Parse(bridge.LastJson!).RootElement.GetProperty("ops").EnumerateArray().First();
        // KeyframeHost renders the view directly, so the root node IS the view under test.
        return (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
    }
}

file sealed class KeyframeHost(View child) : View
{
    public override View Body => child;
}

file sealed class CapturingKeyframeBridge : IBridge
{
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) { }
    public void Render(string json) => LastJson = json;
}
