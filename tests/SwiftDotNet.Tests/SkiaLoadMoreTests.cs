using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// End-to-end incremental load on Skia: scrolling a <c>.OnReachEnd(...)</c> list to near its bottom fires
/// the load-more callback, which appends rows. See <c>SkiaBridge.Scroll</c> and <c>List.OnReachEnd</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaLoadMoreTests
{
    const int W = 400, H = 600;

    [Fact]
    public void ScrollingToEnd_FiresLoadMore_AndAppendsRows()
    {
        var view = new PagingView();
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        var initial = view.Count;
        Assert.Equal(60, initial);

        // Scroll well past the bottom of the visible window → within the end threshold → load-more.
        host.Scroll(W / 2f, H / 2f, 5000f);
        host.RenderPng(W, H);

        Assert.True(view.Count > initial, "load-more should have appended rows");
    }
}

file sealed class PagingView : View
{
    readonly List<string> _items = Enumerable.Range(0, 60).Select(i => i.ToString()).ToList();
    readonly State<int> _tick = new(0);

    public int Count => _items.Count;

    public override View? Body
    {
        get
        {
            _ = _tick.Value;
            return List.ForEach(_items, x => x, x => new Text($"Row {x}"))
                .OnReachEnd(LoadMore, threshold: 150);
        }
    }

    void LoadMore()
    {
        var start = _items.Count;
        for (var i = 0; i < 20; i++) _items.Add((start + i).ToString());
        SwiftApp.Transaction(() => _tick.Value++);
    }
}
