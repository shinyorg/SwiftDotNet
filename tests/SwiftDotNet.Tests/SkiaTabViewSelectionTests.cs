using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Two-way <see cref="TabView"/> selected-index binding on Skia: tapping a tab pushes the new index to the
/// bound state, and assigning the state programmatically switches the visible tab. Covers both the tab bar
/// and the paged carousel. See <c>TabView.SelectedIndex</c>, <c>SkiaNode.SyncTabIndex/EmitTabIndexIfBound</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaTabViewSelectionTests
{
    const int W = 400, H = 800;

    [Fact]
    public void TappingTab_UpdatesBoundIndex()
    {
        var index = new State<int>(0);
        var view = new TabbedView(index, paged: false);
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        // The tab bar sits along the bottom; tap the 3rd of 3 cells.
        host.Tap(W * 5f / 6f, H - 20);
        Assert.Equal(2, index.Value);
    }

    [Fact]
    public void ProgrammaticIndex_SwitchesVisibleTab()
    {
        var index = new State<int>(0);
        var view = new TabbedView(index, paged: false);
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        // Assign the bound state → the engine must show tab 1's content ("Page B").
        SwiftApp.Transaction(() => index.Value = 1);
        host.RenderPng(W, H);

        Assert.True(bridge.TryGetFrame("0.1.0", out _), "tab 1's content should be laid out when index==1");
    }

    [Fact]
    public void PagedCarousel_SwipeAdvancesBoundIndex()
    {
        var index = new State<int>(0);
        var view = new TabbedView(index, paged: true);
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        host.RenderPng(W, H);

        // Paged: a tap on the right half advances to the next page.
        host.Tap(W * 0.8f, H / 2f);
        Assert.Equal(1, index.Value);
    }
}

file sealed class TabbedView(State<int> index, bool paged) : View
{
    public override View? Body
    {
        get
        {
            var tv = new TabView(
                new Tab("A", "1.circle", new Text("Page A")),
                new Tab("B", "2.circle", new Text("Page B")),
                new Tab("C", "3.circle", new Text("Page C"))
            ).SelectedIndex(index);
            return paged ? tv.Paged() : tv;
        }
    }
}
