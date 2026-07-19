using SkiaSharp;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Verifies Skia viewport culling: a keyed <see cref="List"/> taller than the window only paints the
/// rows actually on screen, and scrolling changes *which* rows paint. Uses a custom Skia renderer that
/// tallies its own paints. See <c>SkiaNodePaint.PaintChildren</c> (the ScrollView/List/Form branch).
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaViewportCullingTests
{
    const int W = 400, H = 600;

    [Fact]
    public void TallList_OnlyPaintsVisibleRows_AndCullingTracksScroll()
    {
        var probe = new PaintCounter();
        SkiaRenderers.Register("CullProbe", probe);

        // 100 rows × 80px each = 8000px of content in a 600px window → most rows are offscreen.
        var view = new ProbeListView(Enumerable.Range(0, 100).Select(i => i.ToString()).ToList());
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);

        probe.Reset();
        host.RenderPng(W, H);
        var atTop = probe.Count;

        // Far fewer than all 100 rows painted (only the visible window + partials), and clearly > 0.
        Assert.InRange(atTop, 1, 20);

        // Scroll to a different region and confirm a fresh (non-identical) set of rows paints.
        probe.ClearKeys();
        host.Scroll(W / 2f, H / 2f, 2000f);
        host.RenderPng(W, H);
        var scrolledKeys = probe.PaintedKeys;

        probe.ClearKeys();
        host.RenderPng(W, H);   // repaint at the same offset → same visible rows
        Assert.Equal(scrolledKeys, probe.PaintedKeys);

        // The scrolled window must include rows that were NOT visible at the top.
        Assert.Contains(scrolledKeys, k => !TopRowKeys(atTop).Contains(k));
    }

    // The top window shows roughly rows 0..(atTop-1); anything beyond that proves the cull window moved.
    static HashSet<string> TopRowKeys(int atTop) =>
        Enumerable.Range(0, atTop).Select(i => i.ToString()).ToHashSet();
}

/// <summary>A keyed list of fixed-height custom "CullProbe" rows.</summary>
file sealed class ProbeListView(List<string> items) : View
{
    public override View? Body => List.ForEach(items, x => x, x => new Probe(x));
}

file sealed class Probe(string label) : CustomView
{
    protected override string TypeName => "CullProbe";
    protected override void Configure(CustomNode node) => node.Prop("label", label);
}

/// <summary>Custom renderer that records each row it is asked to paint (by its <c>label</c> prop).</summary>
file sealed class PaintCounter : ISkiaRenderer
{
    readonly HashSet<string> _painted = new();
    public int Count { get; private set; }

    public void Reset() { Count = 0; _painted.Clear(); }
    public void ClearKeys() => _painted.Clear();
    public HashSet<string> PaintedKeys => new(_painted);

    public SKSize Measure(SkiaRenderContext ctx, SKSize available) => new(available.Width, 80);

    public void Paint(SkiaRenderContext ctx, SKCanvas canvas, SKRect rect)
    {
        Count++;
        _painted.Add(ctx.String("label"));
    }
}
