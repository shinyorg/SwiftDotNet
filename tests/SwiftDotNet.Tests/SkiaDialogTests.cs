using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Multi-button Alert / ActionSheet on the self-drawing engine. Unlike the toolkit backends there is no OS
/// modal layer here, so the buttons are chrome the engine paints and hit-tests itself — which makes this the
/// one backend where "the tap landed on the button that was drawn" is testable end to end.
/// See <c>VisualNode.PaintAlert/PaintActionSheet/HitTestOverlay</c>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SkiaDialogTests
{
    const int W = 400, H = 800;

    [Fact]
    public void TappingASecondAlertButton_RunsThatButtonsAction()
    {
        var chosen = "";
        var presented = new State<bool>(true);
        var host = Run(new DialogHostView(presented, sheet: false, c => chosen = c));

        Assert.True(host.Bridge.TryGetDialogButtonCenter("0", 1, out var center),
            "the alert's buttons must be laid out after a paint pass");
        host.Image.Tap(center.X, center.Y);

        Assert.Equal("keep", chosen);
        Assert.False(presented.Value);
    }

    [Fact]
    public void TappingTheFirstAlertButton_RunsTheDestructiveAction()
    {
        var chosen = "";
        var presented = new State<bool>(true);
        var host = Run(new DialogHostView(presented, sheet: false, c => chosen = c));

        Assert.True(host.Bridge.TryGetDialogButtonCenter("0", 0, out var center));
        host.Image.Tap(center.X, center.Y);

        Assert.Equal("delete", chosen);
    }

    [Fact]
    public void TappingOutsideTheAlert_DismissesWithoutChoosing()
    {
        var chosen = "";
        var presented = new State<bool>(true);
        var host = Run(new DialogHostView(presented, sheet: false, c => chosen = c));

        host.Image.Tap(4, 4);   // top-left corner: scrim, well clear of the 300pt card

        Assert.Equal("", chosen);
        Assert.False(presented.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ActionSheetOptions_AreEachIndividuallyTappable(int index)
    {
        // A stack of options only works if each one's rect is distinct — the bug this guards against is
        // every option resolving to the same index (or to the cancel row, which sits in a separate card).
        var chosen = "";
        var presented = new State<bool>(true);
        var host = Run(new DialogHostView(presented, sheet: true, c => chosen = c));

        Assert.True(host.Bridge.TryGetDialogButtonCenter("0", index, out var center));
        host.Image.Tap(center.X, center.Y);

        Assert.Equal(DialogHostView.SheetLabels[index], chosen);
        Assert.False(presented.Value);
    }

    [Fact]
    public void ActionSheetCancelRow_SitsBelowTheOptionsAndReportsItself()
    {
        var presented = new State<bool>(true);
        var chosen = "";
        var host = Run(new DialogHostView(presented, sheet: true, c => chosen = c));

        Assert.True(host.Bridge.TryGetDialogButtonCenter("0", 2, out var lastOption));
        Assert.True(host.Bridge.TryGetDialogButtonCenter("0", 3, out var cancel));
        Assert.True(cancel.Y > lastOption.Y, "the cancel row is detached below the option card");

        host.Image.Tap(cancel.X, cancel.Y);
        Assert.Equal("Cancel", chosen);
    }

    [Fact]
    public void UnpresentedDialog_PaintsNothingAndSwallowsNoTaps()
    {
        var presented = new State<bool>(false);
        var host = Run(new DialogHostView(presented, sheet: false, _ => { }));

        Assert.False(host.Bridge.TryGetDialogButtonCenter("0", 0, out _));
        // The body underneath must still be reachable — an unpresented alert is not an input blocker.
        Assert.True(host.Bridge.TryGetFrame("0.0", out _));
    }

    static (SkiaBridge Bridge, SkiaImageHost Image) Run(View view)
    {
        var bridge = new SkiaBridge();
        var image = new SkiaImageHost(bridge);
        SwiftApp.Run(view, bridge);
        image.RenderPng(W, H);
        return (bridge, image);
    }
}

/// <summary>Root view whose node id is "0" — an Alert or an ActionSheet over a plain body.</summary>
file sealed class DialogHostView(State<bool> presented, bool sheet, Action<string> onChoice) : View
{
    internal static readonly string[] SheetLabels = { "Copy", "Move", "Delete" };

    public override View Body => sheet
        ? new ActionSheet(presented, "Pick one", new[]
        {
            new AlertButton(SheetLabels[0], DialogRole.Default, () => onChoice(SheetLabels[0])),
            new AlertButton(SheetLabels[1], DialogRole.Default, () => onChoice(SheetLabels[1])),
            AlertButton.Destructive(SheetLabels[2], () => onChoice(SheetLabels[2])),
            AlertButton.Cancel("Cancel", () => onChoice("Cancel")),
        }, new Text("body"), message: "Choose what to do with the file.")
        : new Alert(presented, "Delete?", "This cannot be undone.", new[]
        {
            AlertButton.Destructive("Delete", () => onChoice("delete")),
            AlertButton.Cancel("Keep", () => onChoice("keep")),
        }, new Text("body"));
}
