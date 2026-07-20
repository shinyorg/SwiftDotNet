using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// An <see cref="HStack"/> whose greedy child (a <c>TextField</c>, which claims the whole width offered
/// to it) sits next to a fixed-size sibling must still fit the row: the greedy child gives ground and
/// takes only what is left over. Before this, such a row measured wider than its parent and was centred
/// to a negative origin — clipping at *both* edges, which is what the chat input bar did.
/// See <c>SkiaNode.MeasureStack</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaRowOverflowTests
{
    const int W = 400, H = 600;

    [Fact]
    public void GreedyChildYieldsWidthToItsFixedSibling()
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new InputBarView(), bridge);
        host.RenderPng(W, H);

        Assert.True(bridge.TryGetFrame("0.0", out var field), "the text field should be laid out");
        Assert.True(bridge.TryGetFrame("0.1", out var send), "the send button should be laid out");

        // Both ends stay inside the viewport — no clipping on either edge.
        Assert.True(field.Left >= 0, $"row starts off-screen at {field.Left}");
        Assert.True(send.Right <= W + 0.5f, $"row overflows the right edge to {send.Right}");

        // The fixed-size sibling keeps its natural width; the greedy one absorbed the shortfall.
        Assert.True(send.Width > 0 && send.Width < W / 2, $"unexpected button width {send.Width}");
        Assert.True(field.Width > 0, "the field should still get some width");
        Assert.True(field.Right <= send.Left + 0.5f, "the field should not overlap the button");
    }

    [Fact]
    public void RowThatAlreadyFitsIsUnchanged()
    {
        // Two fixed-size children: nothing overflows, so the greedy-shrink path must not engage and
        // the row keeps its natural (centred) placement.
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new NarrowRowView(), bridge);
        host.RenderPng(W, H);

        Assert.True(bridge.TryGetFrame("0.0", out var a));
        Assert.True(bridge.TryGetFrame("0.1", out var b));
        Assert.True(a.Right <= b.Left + 0.5f, "children should be laid out in order without overlap");
        Assert.True(b.Right <= W, "a row that fits must stay inside the viewport");
    }
}

file sealed class InputBarView : View
{
    readonly State<string> _draft = State("");

    public override View? Body =>
        new HStack(
            new TextField("Message…", _draft),
            new Text("Send").Padding(horizontal: 16, vertical: 10));
}

file sealed class NarrowRowView : View
{
    public override View? Body => new HStack(new Text("a"), new Text("b"));
}
