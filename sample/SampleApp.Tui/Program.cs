using System.Globalization;
using SwiftDotNet;
using SwiftDotNet.Sample;
using XenoAtom.Terminal.UI.Controls;

// NOTE: `XenoAtom.Terminal.UI.Controls` is imported, but the parent `XenoAtom.Terminal.UI` deliberately is
// NOT — it declares its own `State<T>`, which would make every `State<int>` in your own views ambiguous
// with SwiftDotNet's. Import the narrowest namespace a custom renderer actually needs.

// A custom NATIVE primitive rendered by a real Terminal.UI control — the same demo the GTK sample runs,
// proving the TuiRenderers registry works. Registered before the first render so the node type resolves.
TuiRenderers.Register("NativeRating", ctx =>
{
    // Set the range and step before the value: Slider<T> clamps and snaps on every write to Value against
    // whatever bounds it has at that moment, and its defaults are 0-10 with a step of 1.
    var slider = new Slider<double> { SnapToStep = true, ShowValueLabel = true };
    slider.Minimum = 0;
    slider.Maximum = 5;
    slider.Step = 1;
    slider.Value = Math.Clamp(ctx.Number("value") ?? 0, 0, 5);
    slider.ValueChanged(() => ctx.Emit(((int)slider.Value).ToString(CultureInfo.InvariantCulture)));
    return slider;
});

// SDN_TUI_GRAPHICS=1 upgrades Image nodes from character art to real Sixel/Kitty pixels (needs a terminal
// that speaks one of those protocols — iTerm2, kitty, WezTerm). Without it, images stay character art,
// which is what every other terminal gets.
if (Environment.GetEnvironmentVariable("SDN_TUI_GRAPHICS") == "1")
    TuiGraphics.Enable();

// SDN_TUI_IMAGE_MODE=halfblock|quadrant|ascii forces one character-art mode instead of picking from the
// terminal's colour support — the knob to reach for when comparing them side by side.
if (Environment.GetEnvironmentVariable("SDN_TUI_IMAGE_MODE") is { Length: > 0 } mode &&
    Enum.TryParse<TuiImageMode>(mode, ignoreCase: true, out var parsed))
    TuiImageOptions.Mode = parsed;

// Build the shared app (services + root view), then hand its provider to the terminal host so views can
// reach services via [Inject] / Service<T>().
var swiftApp = SwiftProgram.CreateSwiftApp();
return SwiftDotNetHost.Run(swiftApp.CreateRoot(), swiftApp.Services);

/// <summary>
/// A CUSTOM NATIVE PRIMITIVE (not a composition) — emits type "NativeRating"; the renderer registered
/// above turns it into a real Terminal.UI <c>Slider</c>. Renders as ⚠ on backends with no renderer.
/// </summary>
sealed class NativeRating : CustomView
{
    readonly State<int> _value;
    public NativeRating(State<int> value) => _value = value;

    protected override string TypeName => "NativeRating";
    protected override void Configure(CustomNode node)
    {
        node.Prop("value", _value.Value);
        node.OnEvent(v => _value.Value = (int)double.Parse(v ?? "0", CultureInfo.InvariantCulture));
    }
}
