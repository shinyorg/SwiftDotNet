using SwiftDotNet;
using Xunit;
using XenoAtom.Terminal.UI.Rendering;

using TTheme = XenoAtom.Terminal.UI.Styling.Theme;

namespace SwiftDotNet.Tests;

/// <summary>
/// Whole-tree layout, rendered to real cells. These are the tests that would have caught the surface
/// wrapper swallowing its content's stretch intent — a bug invisible to type-level mapping assertions,
/// because every control was correct and only the widths were wrong.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class TuiLayoutSnapshotTests
{
    [Fact]
    public void SectionStretchesToTheFullWidth_SoRowsAreNotShrinkWrapped()
    {
        var plain = Render(new SectionView(), width: 40, height: 8).Select(Plain).ToList();

        // The Section becomes a captioned box. If the surface wrapper shrink-wrapped it, the box would be
        // only as wide as its longest row instead of filling the terminal.
        var box = Assert.Single(plain, l => l.Contains('┌'));
        Assert.Contains("Settings", box);
        Assert.Equal(40, box.TrimEnd().Length);
        Assert.EndsWith("┐", box.TrimEnd());
    }

    [Fact]
    public void SpacerPushesSiblingsApartAcrossTheFullRow()
    {
        var plain = Render(new SpacedRowView(), width: 30, height: 3).Select(Plain).ToList();
        var row = Assert.Single(plain, l => l.Contains("left"));

        Assert.StartsWith("left", row.TrimStart());
        Assert.EndsWith("right", row.TrimEnd());
        // Not merely adjacent — the Spacer must have absorbed the slack between them.
        Assert.True(row.IndexOf("right", StringComparison.Ordinal) - "left".Length > 10, row);
    }

    [Fact]
    public void BorderModifierDrawsABoxAndInsetsItsContent()
    {
        var plain = Render(new BorderedView(), width: 20, height: 5).Select(Plain).ToList();

        Assert.Contains(plain, l => l.Contains('╭') && l.Contains('╮'));
        Assert.Contains(plain, l => l.Contains('╰') && l.Contains('╯'));
        // The label must sit inside the box, not be painted over by its edge.
        var label = Assert.Single(plain, l => l.Contains("hi"));
        Assert.StartsWith("│", label.TrimStart());
    }

    [Fact]
    public void FrameModifierConvertsPixelsToCells()
    {
        // .Frame(80, 32) at the default 8×16px cell is 10 columns by 2 rows. A filled shape is spaces
        // carrying a background colour, so the run has to be measured in the markup — stripping the tags
        // would leave nothing but whitespace to count.
        var lines = Render(new FramedShapeView(), width: 30, height: 6);
        var fills = lines
            .Select(l => System.Text.RegularExpressions.Regex.Match(l, @"on #007aff\](\s+)\[/\]"))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value.Length)
            .ToList();

        Assert.Equal(2, fills.Count);            // two rows tall
        Assert.All(fills, w => Assert.Equal(10, w));   // ten columns wide
    }

    static IReadOnlyList<string> Render(View view, int width, int height)
    {
        var bridge = new TuiBridge();
        SwiftApp.Run(view, bridge);
        return VisualSnapshotRenderer.Render(bridge.Host, width, height, TTheme.Default).ToMarkupLines();
    }

    /// <summary>Strips markup tags, leaving the glyphs a terminal would show.</summary>
    static string Plain(string markup)
        => System.Text.RegularExpressions.Regex.Replace(markup, @"\[[^\]]*\]", "");
}

file sealed class SectionView : View
{
    public override View Body => new Form(
        new Section("Settings", new Text("Wi-Fi"), new Text("Bluetooth")));
}

file sealed class SpacedRowView : View
{
    public override View Body => new HStack(new Text("left"), new Spacer(), new Text("right"));
}

file sealed class BorderedView : View
{
    public override View Body => new VStack(new Text("hi").Border(Color.Blue, 1));
}

file sealed class FramedShapeView : View
{
    public override View Body => new VStack(new Rectangle().Frame(80, 32).ForegroundColor(Color.Blue));
}
