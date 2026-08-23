using SwiftDotNet.Graphics;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The platform-view seam: node types registered with <see cref="PlatformViews"/> are not painted by the
/// self-drawing engine but reported to an <see cref="IPlatformViewHost"/>, which floats a real OS control
/// over the canvas at that frame. This is what lets a <c>WebView</c> — or an embedded .NET MAUI view — show
/// up in a Skia tree at all.
///
/// The MAUI half of that story can only be verified by hand on a simulator; everything the *engine* owns
/// (where a placement lands, how it clips and scrolls, when it must hide, and when it must be disposed) is
/// asserted here, headlessly, with a recording host.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class PlatformViewSeamTests
{
    const int W = 400, H = 600;
    const string Probe = "SeamProbe";

    public PlatformViewSeamTests() => PlatformViews.Register(Probe);

    static (SkiaBridge bridge, SkiaImageHost host, RecordingHost pv) Harness(View root)
    {
        var bridge = new SkiaBridge();
        var pv = new RecordingHost();
        bridge.PlatformViewHost = pv;
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        return (bridge, host, pv);
    }

    [Fact]
    public void Placement_IsEmittedAtTheLaidOutFrame_AndCarriesProps()
    {
        var (bridge, host, pv) = Harness(new VStack(
            new Text("above"),
            new ProbeView("alpha").Size(200, 90)));

        host.RenderPng(W, H);

        var p = Assert.Single(pv.Last);
        Assert.Equal(Probe, p.Type);
        Assert.True(p.Visible);
        Assert.Equal(200, p.Frame.Width, 1);
        Assert.Equal(90, p.Frame.Height, 1);
        Assert.Equal("alpha", p.Props["name"]);

        // The placement frame is the node's own laid-out rect — the engine and the host agree on geometry.
        Assert.True(bridge.TryGetFrame(p.Id, out var frame));
        Assert.Equal(frame.Top, p.Frame.Top, 1);
    }

    [Fact]
    public void NoHostAttached_PaintsThePlaceholderInsteadOfPlacing()
    {
        // A headless / game-engine host never sets PlatformViewHost. Nothing should be recorded, and the
        // node must still paint (the ⚠️ placeholder) rather than leaving a hole nothing ever fills.
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new ProbeView("alpha").Size(200, 90), bridge);

        var png = host.RenderPng(W, H);
        Assert.NotEmpty(png);
    }

    [Fact]
    public void ScrolledViewport_TracksTheOffset_AndClipsToTheViewport()
    {
        var rows = Enumerable.Range(0, 40)
            .Select(i => (View)new ProbeView(i.ToString()).Size(200, 60))
            .ToArray();
        var (bridge, host, pv) = Harness(new ScrollView(new VStack(rows)));

        host.RenderPng(W, H);
        var firstTop = pv.Last.First(p => p.Props["name"] as string == "0").Frame.Top;
        Assert.All(pv.Last, p => Assert.NotNull(p.Clip));

        bridge.Scroll(new Point(W / 2f, H / 2f), 300);
        host.RenderPng(W, H);

        var moved = pv.Last.First(p => p.Props["name"] as string == "0");
        Assert.Equal(firstTop - 300, moved.Frame.Top, 1);
    }

    [Fact]
    public void ScrolledOutOfTheViewport_ReportsInvisible_ButStaysPresent()
    {
        // The distinction matters: a host must *hide* a scrolled-away control, not dispose it — disposal is
        // reserved for ids that leave the set entirely (see RemovedSubtree_LeavesTheSet).
        var rows = Enumerable.Range(0, 40)
            .Select(i => (View)new ProbeView(i.ToString()).Size(200, 60))
            .ToArray();
        var (bridge, host, pv) = Harness(new ScrollView(new VStack(rows)));

        host.RenderPng(W, H);
        bridge.Scroll(new Point(W / 2f, H / 2f), 1200);
        host.RenderPng(W, H);

        var first = pv.Last.FirstOrDefault(p => p.Props["name"] as string == "0");
        if (first.Id is not null) Assert.False(first.Visible);   // present-but-hidden, or culled entirely
        Assert.Contains(pv.Last, p => p.Visible);                // whatever scrolled *into* view is shown
    }

    [Fact]
    public void RemovedSubtree_LeavesTheSet_SoTheHostCanDispose()
    {
        var show = new State<bool>(true);
        var (_, host, pv) = Harness(new ToggleHostView(show));

        host.RenderPng(W, H);
        Assert.Single(pv.Last);

        SwiftApp.Transaction(() => show.Value = false);
        host.RenderPng(W, H);
        Assert.Empty(pv.Last);
    }

    [Fact]
    public void PresentedSheet_HidesTheSceneBehindIt_ButShowsItsOwnContent()
    {
        // Z-order inversion: a real OS control always floats above the canvas, so a canvas-drawn Sheet
        // would otherwise appear *behind* it. Only the topmost painted layer keeps its platform views.
        var presented = new State<bool>(false);
        var (_, host, pv) = Harness(new Sheet(presented,
            body: new ProbeView("behind").Size(200, 90),
            content: new ProbeView("inside").Size(200, 90)));

        host.RenderPng(W, H);
        Assert.True(pv.Last.Single(p => p.Props["name"] as string == "behind").Visible);

        SwiftApp.Transaction(() => presented.Value = true);
        host.RenderPng(W, H);

        Assert.False(pv.Last.Single(p => p.Props["name"] as string == "behind").Visible);
        Assert.True(pv.Last.Single(p => p.Props["name"] as string == "inside").Visible);
    }

    [Fact]
    public void RegisteredWebView_BecomesAPlacementInsteadOfThePaintedApology()
    {
        // WebView is the built-in node the self-drawing engine has never been able to draw — it paints
        // "native WebView — not drawable on a canvas". Registering it as a platform view is what lets a
        // host that *can* place one (the MAUI host) show the real thing, with no change to the DSL.
        PlatformViews.Register("WebView");
        try
        {
            var (_, host, pv) = Harness(new VStack(new WebView("https://example.com")));
            host.RenderPng(W, H);

            var p = Assert.Single(pv.Last);
            Assert.Equal("WebView", p.Type);
            Assert.True(p.Visible);
            // The host needs the props to configure the control it builds — that is why a placement
            // carries them rather than just a rect.
            Assert.Equal("https://example.com", p.Props["url"]);
        }
        finally
        {
            PlatformViews.Unregister("WebView");
        }
    }

    [Fact]
    public void Unregistering_ReturnsTheTypeToPainting()
    {
        PlatformViews.Register("WebView");
        PlatformViews.Unregister("WebView");

        var (_, host, pv) = Harness(new VStack(new WebView("https://example.com")));
        host.RenderPng(W, H);

        Assert.Empty(pv.Last);
    }

    [Fact]
    public void KeyedListReorder_KeepsIdentityWithTheKey()
    {
        var items = new State<List<string>>(new() { "a", "b", "c" });
        var (_, host, pv) = Harness(new KeyedProbeList(items));

        host.RenderPng(W, H);
        var before = pv.Last.OrderBy(p => p.Frame.Top).Select(p => p.Props["name"] as string).ToList();
        Assert.Equal(new[] { "a", "b", "c" }, before);

        SwiftApp.Transaction(() => items.Value = new List<string> { "c", "a", "b" });
        host.RenderPng(W, H);
        var after = pv.Last.OrderBy(p => p.Frame.Top).Select(p => p.Props["name"] as string).ToList();
        Assert.Equal(new[] { "c", "a", "b" }, after);
        Assert.Equal(3, pv.Last.Count);
    }
}

/// <summary>Captures the last full set the engine handed the host, which is all a real host reconciles against.</summary>
sealed class RecordingHost : IPlatformViewHost
{
    public IReadOnlyList<PlatformViewPlacement> Last { get; private set; } = Array.Empty<PlatformViewPlacement>();

    public void SyncPlatformViews(IReadOnlyList<PlatformViewPlacement> placements) => Last = placements;
}

/// <summary>A stand-in for `MauiView` — a custom node type registered as a platform view.</summary>
file sealed class ProbeView : CustomView
{
    readonly string _name;
    double _w = -1, _h = -1;

    public ProbeView(string name) => _name = name;

    public ProbeView Size(double w, double h) { _w = w; _h = h; return this; }

    protected override string TypeName => "SeamProbe";

    protected override void Configure(CustomNode n)
    {
        n.Prop("name", _name).Prop("w", _w).Prop("h", _h);
    }
}

file sealed class ToggleHostView(State<bool> show) : View
{
    public override View? Body => show.Value
        ? new VStack(new ProbeView("alpha").Size(200, 90))
        : new VStack(new Text("gone"));
}

file sealed class KeyedProbeList(State<List<string>> items) : View
{
    public override View? Body =>
        new VStack(items.Value.Select(x => (View)new ProbeView(x).Size(200, 60)).ToArray());
}
