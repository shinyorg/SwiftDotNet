using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// End-to-end check of host-side keyed reconciliation on the Skia backend: after a keyed list is
/// reordered, a reused row still routes its events to the correct item. This is the subtle part —
/// a reused <c>SkiaNode</c> must adopt its new (moved) structural id so <c>Emit(Id)</c> lands in the
/// current render's action table. If id-refresh were broken, tapping the row at position 1 would fire
/// the action bound to whatever item now sits at position 0. See <c>SkiaNode.SetChildren/Adopt</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaKeyedReconcileTests
{
    const int W = 400, H = 800;

    [Fact]
    public void KeyedReorder_RoutesRowEventToCorrectItem()
    {
        var view = new ButtonListView(new List<string> { "a", "b", "c" });
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);

        host.RenderPng(W, H);              // lay out so rows have frames

        // Sanity: tapping the row at position 0 fires item "a".
        TapRow(host, bridge, 0);
        Assert.Equal("a", view.LastClicked);

        // Reorder to c, a, b. Same count + same row type → the differ emits a keyed setChildren and the
        // Skia host reconciles rows by key, reusing each SkiaNode and re-stamping its moved id.
        view.Reorder("c", "a", "b");
        host.RenderPng(W, H);

        // "a" now lives at position 1. Tapping position 1 must fire "a", not the item that used to be there.
        TapRow(host, bridge, 1);
        Assert.Equal("a", view.LastClicked);

        // ...and position 0 now fires "c".
        TapRow(host, bridge, 0);
        Assert.Equal("c", view.LastClicked);
    }

    /// <summary>Tap the button that is the list's child at <paramref name="index"/> (id <c>0.{index}</c>).</summary>
    static void TapRow(SkiaImageHost host, SkiaBridge bridge, int index)
    {
        Assert.True(bridge.TryGetFrame($"0.{index}", out var frame), $"no frame for row {index}");
        host.Tap(frame.MidX, frame.MidY);
    }
}

/// <summary>A keyed list whose rows are buttons; clicking a row records the item it was built from.</summary>
file sealed class ButtonListView : View
{
    readonly List<string> _items;
    readonly State<int> _tick = new(0);

    public ButtonListView(List<string> items) => _items = items;

    public string? LastClicked { get; private set; }

    public void Reorder(params string[] order)
    {
        _items.Clear();
        _items.AddRange(order);
        SwiftApp.Transaction(() => _tick.Value++);
    }

    public override View? Body => Touch(_tick) is var _
        ? List.ForEach(_items, x => x, x => new Button(x, () => LastClicked = x))
        : null;

    static int Touch(State<int> s) => s.Value;
}
