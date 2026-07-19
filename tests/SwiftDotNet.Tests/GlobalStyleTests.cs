using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The three global-styling mechanisms, all resolved in C# during the render pass and asserted on the
/// emitted node tree (so they hold on every backend). See <c>Core/EnvironmentValues.cs</c>,
/// <c>Core/Styles.cs</c>, <c>Core/Theme.cs</c>, and the cascade hook in <c>Core/View.ToNode</c>:
/// <list type="bullet">
/// <item><b>B — environment cascade:</b> an ambient <c>font</c>/<c>foregroundColor</c> is inherited by
/// descendants that don't set their own, using existing wire modifiers.</item>
/// <item><b>A — reusable bundles:</b> <c>.Style</c>/<c>.CardStyle</c> attach a set of modifiers, resolved at
/// render time so they can read the ambient <see cref="Theme"/>.</item>
/// <item><b>C — control styles:</b> an ambient <see cref="IButtonStyle"/> restyles every button below.</item>
/// </list>
/// These drive <see cref="SwiftApp"/>'s shared static state, so they run in the serial collection.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class GlobalStyleTests
{
    // ---- B: environment cascade ----------------------------------------------------------------

    [Fact]
    public void AmbientFontAndColor_AreInheritedByDescendants()
    {
        var root = new Host(new VStack(new Text("hi")))
            .Environment(e => e.Font(Font.Headline).ForegroundColor(Color.Blue));

        var tree = Render(root);
        var text = FirstOfType(tree, "Text");

        Assert.Equal(new[] { "headline" }, ModifierValues(text, "font"));
        Assert.Equal(new[] { "blue" }, ModifierValues(text, "foregroundColor"));
    }

    [Fact]
    public void ExplicitLocalModifier_WinsOverAmbient_AndIsNotDuplicated()
    {
        var root = new Host(new Text("bye").Font(Font.Caption))
            .Environment(e => e.Font(Font.Headline).ForegroundColor(Color.Blue));

        var tree = Render(root);
        var text = FirstOfType(tree, "Text");

        // The local font is kept and the ambient one is not added on top; the ambient color still fills in.
        Assert.Equal(new[] { "caption" }, ModifierValues(text, "font"));
        Assert.Equal(new[] { "blue" }, ModifierValues(text, "foregroundColor"));
    }

    [Fact]
    public void WithoutAnEnvironment_NoStyleModifiersAreInjected()
    {
        var tree = Render(new Host(new Text("plain")));
        var text = FirstOfType(tree, "Text");

        Assert.Empty(ModifierValues(text, "font"));
        Assert.Empty(ModifierValues(text, "foregroundColor"));
    }

    [Fact]
    public void NestedScopes_Compose_InnerOverridesOnlyWhatItSets()
    {
        var root = new Host(new Host(new Text("x")).Environment(e => e.ForegroundColor(Color.Red)))
            .Environment(e => e.Font(Font.Title));

        var tree = Render(root);
        var text = FirstOfType(tree, "Text");

        Assert.Equal(new[] { "title" }, ModifierValues(text, "font"));       // inherited from the outer scope
        Assert.Equal(new[] { "red" }, ModifierValues(text, "foregroundColor")); // set by the inner scope
    }

    // ---- A: reusable modifier bundles ----------------------------------------------------------

    [Fact]
    public void Style_AppliesTheBundlesModifiers()
    {
        var root = new Host(new VStack(new Text("card")).Style(b => b.Padding(8).CornerRadius(4)));

        var vstack = FirstOfType(Render(root), "VStack");
        var types = ModifierTypes(vstack);

        Assert.Contains("padding", types);
        Assert.Contains("cornerRadius", types);
    }

    [Fact]
    public void CardStyle_ReadsTheAmbientTheme_AtRenderTime()
    {
        var theme = Theme.Default with { CornerRadius = 20, Surface = Color.Hex("#ABCDEF") };
        var root = new Host(new VStack(new Text("c")).CardStyle()).Theme(theme);

        var vstack = FirstOfType(Render(root), "VStack");

        Assert.Equal(20d, ModifierNumber(vstack, "cornerRadius", "radius"));
        Assert.Equal(new[] { "#ABCDEF" }, ModifierValues(vstack, "background"));
    }

    // ---- C: ambient control styles -------------------------------------------------------------

    [Fact]
    public void ButtonStyle_RestylesEveryButtonBelow()
    {
        var root = new Host(new VStack(new Button("A", () => { }), new Button("B", () => { })))
            .ButtonStyle(new FilledButtonStyle());

        var buttons = OfType(Render(root), "Button");

        Assert.Equal(new[] { "blue" }, ModifierValues(buttons[0], "background")); // Theme.Default.Accent
        Assert.Equal(2, ModifierTypes(buttons[0]).Count(t => t == "padding"));    // horizontal + vertical
        Assert.Contains("cornerRadius", ModifierTypes(buttons[1]));
    }

    [Fact]
    public void ExplicitButtonModifier_WinsOverTheAmbientStyle()
    {
        var root = new Host(new Button("B", () => { }).Background(Color.Green))
            .ButtonStyle(new FilledButtonStyle());

        var button = FirstOfType(Render(root), "Button");

        Assert.Equal(new[] { "green" }, ModifierValues(button, "background"));
    }

    [Fact]
    public void FilledButtonStyle_PicksUpTheInjectedThemeAccent()
    {
        var root = new Host(new Button("A", () => { }))
            .Theme(Theme.Default with { Accent = Color.Red })
            .ButtonStyle(new FilledButtonStyle());

        var button = FirstOfType(Render(root), "Button");

        Assert.Equal(new[] { "red" }, ModifierValues(button, "background"));
    }

    [Fact]
    public void ButtonStyle_DoesNotAffectNonButtonNodes()
    {
        var root = new Host(new VStack(new Text("t"), new Button("A", () => { })))
            .ButtonStyle(new FilledButtonStyle());

        var tree = Render(root);

        Assert.Empty(ModifierValues(FirstOfType(tree, "Text"), "background"));
        Assert.NotEmpty(ModifierValues(FirstOfType(tree, "Button"), "background"));
    }

    // ---- helpers -------------------------------------------------------------------------------

    static JsonElement Render(View root)
    {
        var bridge = new CapturingStyleBridge();
        SwiftApp.Run(root, bridge);
        var op = JsonDocument.Parse(bridge.LastJson).RootElement.GetProperty("ops").EnumerateArray().First();
        return (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
    }

    static IEnumerable<JsonElement> Walk(JsonElement node)
    {
        yield return node;
        foreach (var child in node.GetProperty("children").EnumerateArray())
            foreach (var d in Walk(child)) yield return d;
    }

    static JsonElement FirstOfType(JsonElement root, string type) =>
        Walk(root).First(n => n.GetProperty("type").GetString() == type);

    static List<JsonElement> OfType(JsonElement root, string type) =>
        Walk(root).Where(n => n.GetProperty("type").GetString() == type).ToList();

    static List<string?> ModifierTypes(JsonElement node) =>
        node.GetProperty("modifiers").EnumerateArray().Select(m => m.GetProperty("type").GetString()).ToList();

    static string?[] ModifierValues(JsonElement node, string type) =>
        node.GetProperty("modifiers").EnumerateArray()
            .Where(m => m.GetProperty("type").GetString() == type)
            .Select(m => m.TryGetProperty("value", out var v) ? v.GetString() : null)
            .ToArray();

    static double ModifierNumber(JsonElement node, string type, string field) =>
        node.GetProperty("modifiers").EnumerateArray()
            .First(m => m.GetProperty("type").GetString() == type)
            .GetProperty(field).GetDouble();
}

/// <summary>A minimal composite root that just renders the view it's handed — a stand-in for an app screen.</summary>
file sealed class Host(View child) : View
{
    public override View Body => child;
}

file sealed class CapturingStyleBridge : IBridge
{
    public string LastJson { get; private set; } = "";
    public void Render(string json) => LastJson = json;
    public void SetEventHandler(Action<string, string?> handler) { }
}
