using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The compact live wire: writer, reader, and the size claim that justifies its existence.
///
/// These matter more than a typical serializer test because the constraint they encode fails silently on
/// device. An oversized Live Activity payload is rejected by APNs with no error the app can observe, so
/// the byte budget has to hold here or it is discovered by a tester days later.
/// </summary>
public class LiveWireTests
{
    static LiveView SampleTree() =>
        new LiveVStack(
                new LiveText("Arriving soon").Font(Font.Headline),
                new LiveHStack(
                        new LiveImage("truck"),
                        new LiveTimer(DateTimeOffset.UnixEpoch.AddSeconds(1_800_000_000)),
                        new LiveSpacer())
                    .Spacing(6),
                new LiveProgress(0.42).Tint(Color.Green))
            .Spacing(8)
            .Padding(12);

    [Fact]
    public void RoundTripsThroughTheReader()
    {
        var payload = LiveWire.Build(SampleTree());
        Assert.True(LiveWireReader.TryParse(payload.Json, out var parsed));

        Assert.NotNull(parsed);
        Assert.Equal("LVStack", parsed!.Type);
        Assert.Equal(3, parsed.Children.Count);
        Assert.Equal("LText", parsed.Children[0].Type);
        Assert.Equal("Arriving soon", parsed.Children[0].Props["text"]);

        // The writer shortens the modifier discriminator to "t"; the reader must restore "type" or every
        // downstream consumer (validator, lowering, both interpreters) silently sees no modifiers at all.
        var padding = parsed.Modifiers.Single(m => (string)m["type"] == "padding");
        Assert.Equal(12d, padding["top"]);
    }

    [Fact]
    public void ReSerializesToTheSameBytes()
    {
        var payload = LiveWire.Build(SampleTree());
        LiveWireReader.TryParse(payload.Json, out var parsed);

        // Writer and reader are separate hand-rolled implementations, so a stable round trip is the only
        // assertion that tests the contract rather than one side's output.
        Assert.Equal(payload.Json, LiveWire.Serialize(parsed!));
    }

    [Fact]
    public void IsSubstantiallySmallerThanTheCoreWire()
    {
        var payload = LiveWire.Build(SampleTree());
        LiveWireReader.TryParse(payload.Json, out var parsed);

        var core = NodeJson.Serialize(parsed!);

        // The whole reason a second serializer exists. If this ever stops holding, the compact wire is
        // costing complexity for nothing.
        Assert.True(payload.Bytes < core.Length * 0.7,
            $"compact wire was {payload.Bytes} bytes vs {core.Length} for the core wire");
    }

    [Fact]
    public void OmitsIdsExceptOnAddressableNodes()
    {
        var payload = LiveWire.Build(new LiveVStack(
            new LiveText("plain"),
            new LiveButton("Cancel", () => { })));

        // Ids exist to let an inbound tap find its handler, so only nodes that can be tapped need one.
        // Writing them everywhere would spend a meaningful share of a 4 KB budget on dead structure.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(payload.Json, "\"i\":"));
    }

    [Fact]
    public void RoundsDoublesRatherThanEmittingFullPrecision()
    {
        var payload = LiveWire.Build(new LiveProgress(0.1 + 0.2));

        // 0.30000000000000004 is 19 characters of nothing on a lock screen.
        Assert.Contains("0.3", payload.Json);
        Assert.DoesNotContain("0.30000", payload.Json);
    }

    [Fact]
    public void EscapesStringsTheReaderCanRecover()
    {
        var payload = LiveWire.Build(new LiveText("quote \" backslash \\ newline \n tab \t"));
        Assert.True(LiveWireReader.TryParse(payload.Json, out var parsed));
        Assert.Equal("quote \" backslash \\ newline \n tab \t", parsed!.Props["text"]);
    }

    [Fact]
    public void MalformedInputFailsWithoutThrowing()
    {
        // A widget extension reads a file another process may have been killed halfway through writing.
        // Throwing there means an extension that crashes on relaunch, forever.
        Assert.False(LiveWireReader.TryParse("{\"t\":\"Text\",", out var parsed));
        Assert.Null(parsed);
    }

    [Fact]
    public void ReusesTheCoreBrushGrammar()
    {
        var payload = LiveWire.Build(
            new LiveText("gradient").Background(new LinearGradient(Color.Red, Color.Blue)));

        // The live vocabulary deliberately does not invent a second gradient encoding: every backend
        // already parses this one.
        Assert.Contains("linear:90:red@0;blue@1", payload.Json);
    }
}
