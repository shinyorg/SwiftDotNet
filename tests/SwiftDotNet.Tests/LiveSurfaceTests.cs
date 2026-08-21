using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Activities, widgets and the channel between the app and a surface.
///
/// All three are testable headlessly because the vocabulary, the timeline model and the mailbox are plain
/// net10.0 - the platform drivers add only the nudge. That split is what makes the entry-selection rule
/// and the byte budget assertable on a Mac instead of on a lock screen.
/// </summary>
public class LiveSurfaceTests
{
    // ---- Live Activities -------------------------------------------------------------------------

    sealed record DeliveryState(string Courier, double Fraction);

    sealed class DeliveryActivity : LiveActivity<DeliveryState>
    {
        public override string Kind => "delivery";

        public override LiveView LockScreen(DeliveryState s) =>
            new LiveVStack(
                new LiveText(s.Courier).Font(Font.Headline),
                new LiveProgress(s.Fraction).Tint(Color.Green));

        public override LiveView? CompactLeading(DeliveryState s) => new LiveImage("truck");

        public override LiveView? CompactTrailing(DeliveryState s) => new LiveText($"{s.Fraction:P0}");

        public override LiveExpanded? Expanded(DeliveryState s) =>
            new LiveExpanded()
                .Leading(new LiveImage("truck"))
                .Bottom(new LiveButton("Cancel", () => Cancelled = true));

        public bool Cancelled { get; private set; }
    }

    [Fact]
    public void ActivityBuildsEverySlotItDeclares()
    {
        var payload = new DeliveryActivity()
            .BuildPayload(new DeliveryState("DHL", 0.4), LiveTarget.AppleActivity, 1000);

        Assert.Equal(
            new[]
            {
                LiveSlot.LockScreen, LiveSlot.CompactLeading, LiveSlot.CompactTrailing,
                LiveSlot.ExpandedLeading, LiveSlot.ExpandedBottom,
            }.OrderBy(x => x),
            payload.Snapshot.Trees.Keys.OrderBy(x => x));

        // Minimal and the other expanded regions are not declared, so they are absent rather than empty.
        Assert.DoesNotContain(LiveSlot.Minimal, payload.Snapshot.Trees.Keys);
    }

    [Fact]
    public void ActivityCollectsHandlersFromEverySlot()
    {
        var activity = new DeliveryActivity();
        var payload = activity.BuildPayload(new DeliveryState("DHL", 0.4), LiveTarget.AppleActivity, 1000);

        var handler = Assert.Single(payload.Actions);
        handler.Value(null);
        Assert.True(activity.Cancelled);
    }

    [Fact]
    public void ActivitySlotIdsAreScopedBySlot()
    {
        var payload = new DeliveryActivity()
            .BuildPayload(new DeliveryState("DHL", 0.4), LiveTarget.AppleActivity, 1000);

        // Ids must be unique across slots, not merely within one. Seeding every slot from "l" would give
        // the lock screen's first child and the expanded region's first child the same id, and a tap on
        // one would fire the other's handler.
        Assert.StartsWith(LiveSlot.ExpandedBottom, payload.Actions.Keys.Single());
    }

    sealed class FatActivity : LiveActivity<string>
    {
        public override string Kind => "fat";

        // Eight slots of long text: individually fine, collectively past the APNs ceiling.
        public override LiveView LockScreen(string s) => Block(s);
        public override LiveView? CompactLeading(string s) => Block(s);
        public override LiveView? CompactTrailing(string s) => Block(s);
        public override LiveView? Minimal(string s) => Block(s);
        public override LiveExpanded? Expanded(string s) =>
            new LiveExpanded().Leading(Block(s)).Trailing(Block(s)).Center(Block(s)).Bottom(Block(s));

        static LiveView Block(string s) => new LiveVStack(new LiveText(s), new LiveText(s), new LiveText(s));
    }

    [Fact]
    public void ActivityBudgetIsCheckedAcrossAllSlotsAtOnce()
    {
        var payload = new FatActivity()
            .BuildPayload(new string('x', 400), LiveTarget.AppleActivity, 1000);

        // The 4 KB ceiling is on the whole content state. A per-slot check would pass eight times over
        // and still be rejected by APNs, which is exactly the silent failure this guards.
        Assert.True(payload.Snapshot.Bytes > LiveBudget.ActivityHardBytes);
        Assert.Contains(payload.Diagnostics, d => d.Code == "SDNL001" && d.Severity == LiveSeverity.Error);
        Assert.Throws<InvalidOperationException>(payload.Assert);
    }

    [Fact]
    public void SmallActivityStaysUnderBudget()
    {
        var payload = new DeliveryActivity()
            .BuildPayload(new DeliveryState("DHL", 0.4), LiveTarget.AppleActivity, 1000);

        Assert.True(payload.Snapshot.Bytes < LiveBudget.ActivityWarnBytes,
            $"a five-slot activity serialized to {payload.Snapshot.Bytes} bytes");
        payload.Assert();
    }

    // ---- Widgets ---------------------------------------------------------------------------------

    sealed class ForecastWidget : Widget<int>
    {
        public override string Kind => "forecast";

        public override IReadOnlyList<WidgetFamily> Families { get; } =
            new[] { WidgetFamily.Small, WidgetFamily.Medium };

        public override LiveView Body(int degrees, WidgetFamily family) => family switch
        {
            WidgetFamily.Small => new LiveText($"{degrees}"),
            _ => new LiveHStack(new LiveText("Ottawa"), new LiveText($"{degrees}")),
        };

        public override Task<WidgetTimeline<int>> TimelineAsync(WidgetContext context) =>
            Task.FromResult(WidgetTimeline
                .Entry(DateTimeOffset.UnixEpoch.AddSeconds(1000), 10)
                .Entry(DateTimeOffset.UnixEpoch.AddSeconds(2000), 12)
                .Entry(DateTimeOffset.UnixEpoch.AddSeconds(3000), 14)
                .RefreshAfter(DateTimeOffset.UnixEpoch.AddSeconds(4000)));
    }

    [Fact]
    public async Task WidgetRendersEveryEntryForEveryDeclaredFamily()
    {
        var payload = await new ForecastWidget()
            .BuildPayloadAsync(new WidgetContext(), new LiveTarget { Platform = LivePlatform.Apple });

        // 3 entries x 2 families. This fan-out is why placements matter: rendering families nobody placed
        // is the difference between 6 trees and 21.
        Assert.Equal(6, payload.Snapshot.Trees.Count);
        Assert.Contains("Small@1000", payload.Snapshot.Trees.Keys);
        Assert.Contains("Medium@3000", payload.Snapshot.Trees.Keys);
    }

    [Fact]
    public async Task WidgetRendersOnlyPlacedFamiliesWhenSomeArePlaced()
    {
        var context = new WidgetContext
        {
            Placements = new[] { new SurfacePlacement("forecast", WidgetFamily.Small, "1") },
        };

        var payload = await new ForecastWidget()
            .BuildPayloadAsync(context, new LiveTarget { Platform = LivePlatform.Apple });

        Assert.Equal(3, payload.Snapshot.Trees.Count);
        Assert.All(payload.Snapshot.Trees.Keys, k => Assert.StartsWith("Small@", k));
    }

    [Theory]
    [InlineData(500, "10")]     // before every entry -> earliest
    [InlineData(1500, "10")]    // between entries -> the one in effect
    [InlineData(2500, "12")]
    [InlineData(9999, "14")]    // past the tail -> stays on the last
    public async Task WidgetSelectsTheEntryInEffect(double at, string expected)
    {
        var payload = await new ForecastWidget()
            .BuildPayloadAsync(new WidgetContext(), new LiveTarget { Platform = LivePlatform.Apple });

        var json = payload.TreeFor(WidgetFamily.Small, at);

        // Both hosts implement this rule independently - the Swift provider and the Android provider - so
        // pinning it here is what keeps the two from drifting.
        Assert.NotNull(json);
        Assert.Contains($"\"text\":\"{expected}\"", json);
    }

    sealed class StaticWidget : Widget<string>
    {
        public override string Kind => "static";
        public override LiveView Body(string s, WidgetFamily family) => new LiveText(s);
        public override Task<WidgetTimeline<string>> TimelineAsync(WidgetContext context) =>
            Task.FromResult(WidgetTimeline.Single("hello"));
    }

    [Fact]
    public async Task WidgetReportsATimelineThatWillNeverUpdate()
    {
        var payload = await new StaticWidget()
            .BuildPayloadAsync(new WidgetContext(), new LiveTarget { Platform = LivePlatform.Apple });

        // Fine for static content and a bug for anything time-based, and the platform reports it as
        // nothing at all - the last entry simply stays on screen forever.
        Assert.Contains(payload.Diagnostics, d => d.Code == "SDNL021");
    }

    // ---- The channel -----------------------------------------------------------------------------

    static string TempRoot() =>
        Path.Combine(Path.GetTempPath(), "sdn-live-tests", Guid.NewGuid().ToString("n"));

    [Fact]
    public async Task ChannelRoundTripsASnapshot()
    {
        var channel = new FileSurfaceChannel(TempRoot());
        var payload = new DeliveryActivity()
            .BuildPayload(new DeliveryState("DHL", 0.4), LiveTarget.AppleActivity, 1234);

        await channel.PublishAsync(payload.Snapshot);
        var read = await channel.ReadAsync("delivery");

        Assert.NotNull(read);
        Assert.Equal(LiveSurface.Activity, read!.Surface);
        Assert.Equal(1234, read.PublishedAt);
        Assert.Equal(payload.Snapshot.Trees, read.Trees);
    }

    [Fact]
    public async Task ChannelQueuesAndDrainsActions()
    {
        var channel = new FileSurfaceChannel(TempRoot());

        await channel.PostActionAsync(new SurfaceAction("delivery", "lockScreen.1", null, 10));
        await channel.PostActionAsync(new SurfaceAction("delivery", "lockScreen.2", "yes", 11));

        var drained = await channel.DrainActionsAsync();
        Assert.Equal(2, drained.Count);
        Assert.Equal("lockScreen.2", drained[1].NodeId);
        Assert.Equal("yes", drained[1].Value);

        // Draining clears: a tap must not be replayed on every foreground.
        Assert.Empty(await channel.DrainActionsAsync());
    }

    [Fact]
    public void ActionLineSurvivesDelimitersInItsFields()
    {
        var action = new SurfaceAction("kind|with|pipes", "node\\path", "line\nbreak", 5.25);
        Assert.True(SurfaceAction.TryParse(action.ToLine(), out var parsed));
        Assert.Equal(action, parsed);
    }

    [Fact]
    public void CorruptActionLinesAreRejectedNotThrown()
    {
        // The mailbox is appended to by a separate process the system can kill mid-write; a corrupt line
        // must not take down app startup.
        Assert.False(SurfaceAction.TryParse("garbage", out _));
        Assert.False(SurfaceAction.TryParse("not-a-number|k|n|v", out _));
    }

    [Fact]
    public async Task RouterDispatchesDrainedActionsAndReportsOrphans()
    {
        var channel = new FileSurfaceChannel(TempRoot());
        var router = new LiveActionRouter();

        var fired = false;
        router.Register("delivery", new Dictionary<string, Action<string?>>
        {
            ["lockScreen.1"] = _ => fired = true,
        });

        await channel.PostActionAsync(new SurfaceAction("delivery", "lockScreen.1", null, 1));
        await channel.PostActionAsync(new SurfaceAction("delivery", "gone.9", null, 2));

        var unhandled = await router.DrainAsync(channel);

        Assert.True(fired);
        // An action against a tree published by a previous launch has no handler. That is normal, not an
        // error, and the caller decides what to do with it.
        Assert.Equal("gone.9", Assert.Single(unhandled).NodeId);
    }

    [Fact]
    public void SnapshotEncodingRoundTrips()
    {
        var snapshot = new SurfaceSnapshot
        {
            Kind = "delivery",
            Surface = LiveSurface.Widget,
            Trees = new Dictionary<string, string> { ["Small@10"] = "{\"t\":\"Text\"}" },
            PublishedAt = 10.5,
            RefreshAfter = 20.25,
        };

        // The Swift shim parses exactly this format, so the round trip here is standing in for a
        // cross-language contract that nothing else in the build can check.
        Assert.True(FileSurfaceChannel.TryDecode(FileSurfaceChannel.Encode(snapshot), out var decoded));
        Assert.Equal(snapshot.Kind, decoded.Kind);
        Assert.Equal(snapshot.Surface, decoded.Surface);
        Assert.Equal(snapshot.PublishedAt, decoded.PublishedAt);
        Assert.Equal(snapshot.RefreshAfter, decoded.RefreshAfter);
        Assert.Equal(snapshot.Trees, decoded.Trees);
    }

    // ---- Lowering --------------------------------------------------------------------------------

    [Fact]
    public void LoweringRewritesTheVocabularyOntoCoreNodes()
    {
        var payload = LiveWire.Build(new LiveVStack(
            new LiveText("hi").Font(Font.Headline),
            new LiveProgress(0.5),
            LiveShape.Capsule().Background(Color.Red)));

        var core = LiveLowering.ToCoreNode(payload.Root, 0);

        // The bitmap route works because the live vocabulary is a strict subset of the main DSL, so
        // renaming the types is enough to reuse the whole layout and paint engine.
        Assert.Equal("VStack", core.Type);
        Assert.Equal("Text", core.Children[0].Type);
        Assert.Equal("ProgressView", core.Children[1].Type);
        Assert.Equal("Capsule", core.Children[2].Type);
    }

    [Fact]
    public void LoweringRemapsModifierKeysCoreAlreadyOwns()
    {
        var payload = LiveWire.Build(new LiveText("x").CornerRadius(6).Opacity(0.5));
        var core = LiveLowering.ToCoreNode(payload.Root, 0);

        Assert.Equal(6d, core.Modifiers.Single(m => (string)m["type"] == "cornerRadius")["radius"]);
        Assert.Equal(0.5d, core.Modifiers.Single(m => (string)m["type"] == "opacity")["amount"]);
    }

    [Fact]
    public void LoweringFreezesATimerAndSaysSo()
    {
        var payload = LiveWire.Build(new LiveTimer(DateTimeOffset.UnixEpoch.AddSeconds(3725)));
        var core = LiveLowering.ToCoreNode(payload.Root, 5);

        // A bitmap is a still frame, so the one node whose whole value is that it ticks by itself is
        // exactly the one the bitmap route cannot carry.
        Assert.Equal("Text", core.Type);
        Assert.Equal("1:02:00", core.Props["text"]);
        Assert.Contains(LiveLowering.Diagnose(payload.Root), d => d.Code == "SDNL030");
    }
}
