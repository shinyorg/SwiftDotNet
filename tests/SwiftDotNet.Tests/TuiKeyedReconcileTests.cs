using SwiftDotNet;
using Xunit;

using TTextBlock = XenoAtom.Terminal.UI.Controls.TextBlock;

namespace SwiftDotNet.Tests;

/// <summary>
/// Host-side keyed reconciliation on the terminal backend — the same contract
/// <c>SkiaKeyedReconcileTests</c> locks in, for the same reason. Node ids are structural paths, so a
/// recycled row that moves must <em>adopt its new id</em>: <see cref="TuiBridge.Emit"/> routes events by
/// id, and a row that kept its old one would fire the action bound to whatever item now sits where it
/// used to be. See <c>TuiNode.Adopt</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class TuiKeyedReconcileTests
{
    [Fact]
    public void KeyedReorder_RecyclesRowVisualsInsteadOfRebuilding()
    {
        var view = new KeyedListView("a", "b", "c");
        var (bridge, pump) = TuiTestHost.Run(view);

        var rowA = bridge.FindControl("0.0");
        var rowB = bridge.FindControl("0.1");
        Assert.NotNull(rowA);
        Assert.NotNull(rowB);

        view.Reorder("c", "a", "b");
        pump.Drain();

        // "a" moved from index 0 to index 1 and "b" from 1 to 2 — both keep their live control instances.
        Assert.Same(rowA, bridge.FindControl("0.1"));
        Assert.Same(rowB, bridge.FindControl("0.2"));
    }

    [Fact]
    public void KeyedReorder_RestampsMovedRowIdsSoEventsStayRouted()
    {
        var view = new KeyedListView("a", "b", "c");
        var (bridge, pump) = TuiTestHost.Run(view);

        view.Reorder("c", "a", "b");
        pump.Drain();

        // Each retained row must believe it lives where it now actually lives. If Adopt failed to
        // re-stamp, the recycled row at 0.1 would still be carrying "0.0".
        Assert.Equal("0.0", bridge.StoredId("0.0"));
        Assert.Equal("0.1", bridge.StoredId("0.1"));
        Assert.Equal("0.2", bridge.StoredId("0.2"));
    }

    [Fact]
    public void KeyedReorder_MovesRowContentWithTheRow()
    {
        var view = new KeyedListView("a", "b", "c");
        var (bridge, pump) = TuiTestHost.Run(view);
        Assert.Equal("a", Assert.IsType<TTextBlock>(bridge.FindControl("0.0")).Text);

        view.Reorder("c", "a", "b");
        pump.Drain();

        // Recycling is only correct if the recycled row still shows its own item.
        Assert.Equal("c", Assert.IsType<TTextBlock>(bridge.FindControl("0.0")).Text);
        Assert.Equal("a", Assert.IsType<TTextBlock>(bridge.FindControl("0.1")).Text);
        Assert.Equal("b", Assert.IsType<TTextBlock>(bridge.FindControl("0.2")).Text);
    }

    [Fact]
    public void KeyedInsert_BuildsOnlyTheNewRow()
    {
        var view = new KeyedListView("a", "b");
        var (bridge, pump) = TuiTestHost.Run(view);
        var rowA = bridge.FindControl("0.0");

        view.Reorder("z", "a", "b");
        pump.Drain();

        Assert.Equal("z", Assert.IsType<TTextBlock>(bridge.FindControl("0.0")).Text);
        Assert.Same(rowA, bridge.FindControl("0.1"));
    }
}

/// <summary>A keyed list of text rows, reorderable from the test.</summary>
file sealed class KeyedListView : View
{
    readonly State<int> _tick = new(0);
    readonly System.Collections.Generic.List<string> _items;

    public KeyedListView(params string[] items) => _items = [.. items];

    public void Reorder(params string[] items)
    {
        _items.Clear();
        _items.AddRange(items);
        _tick.Value++;
    }

    public override View? Body => Touch(_tick) is var _
        ? SwiftDotNet.List.ForEach(_items, x => x, x => new Text(x))
        : null;

    static int Touch(State<int> s) => s.Value;
}
