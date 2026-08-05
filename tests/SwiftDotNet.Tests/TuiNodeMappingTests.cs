using SwiftDotNet;
using Xunit;

// The DSL under test and the toolkit it renders onto use the same names for their control vocabularies,
// so the terminal side is aliased throughout — see the same note at the top of `TuiNode.cs`.
using TVStack = XenoAtom.Terminal.UI.Controls.VStack;
using TTextBlock = XenoAtom.Terminal.UI.Controls.TextBlock;
using TButton = XenoAtom.Terminal.UI.Controls.Button;
using TSwitch = XenoAtom.Terminal.UI.Controls.Switch;
using TSlider = XenoAtom.Terminal.UI.Controls.Slider<double>;
using TTextBox = XenoAtom.Terminal.UI.Controls.TextBox;
using TRule = XenoAtom.Terminal.UI.Controls.Rule;
using TProgressBar = XenoAtom.Terminal.UI.Controls.ProgressBar;

namespace SwiftDotNet.Tests;

/// <summary>
/// The terminal backend's node → visual mapping, driven headlessly. Unlike the GTK and Web backends,
/// XenoAtom.Terminal.UI builds its retained tree without needing a TTY, so the whole interpreter is
/// testable on macOS the same way Skia is — no terminal, no alternate screen, no input.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class TuiNodeMappingTests
{
    [Fact]
    public void CoreNodes_MapToTerminalControls()
    {
        var (bridge, _) = TuiTestHost.Run(new MappingView());

        // The root is a VStack; children follow in declaration order.
        Assert.IsType<TVStack>(bridge.FindControl("0"));
        Assert.IsType<TTextBlock>(bridge.FindControl("0.0"));
        Assert.IsType<TButton>(bridge.FindControl("0.1"));
        Assert.IsType<TSwitch>(bridge.FindControl("0.2"));
        Assert.IsType<TSlider>(bridge.FindControl("0.3"));
        Assert.IsType<TTextBox>(bridge.FindControl("0.4"));
        Assert.IsType<TRule>(bridge.FindControl("0.5"));
        Assert.IsType<TProgressBar>(bridge.FindControl("0.6"));
    }

    [Fact]
    public void SecureField_RendersAsAPasswordTextBox()
    {
        var (bridge, _) = TuiTestHost.Run(new SecureView());
        Assert.True(Assert.IsType<TTextBox>(bridge.FindControl("0.0")).IsPassword);
    }

    [Fact]
    public void UnknownNodeType_FallsBackToAWarningLabel()
    {
        // No renderer registered for this type: it must degrade to a visible marker rather than throwing,
        // so one unmapped control cannot take the whole app down.
        var (bridge, _) = TuiTestHost.Run(new CustomOnlyView());
        Assert.Contains("UnregisteredThing", Assert.IsType<TTextBlock>(bridge.FindControl("0.0")).Text);
    }

    [Fact]
    public void RegisteredCustomRenderer_WinsOverTheFallback()
    {
        TuiRenderers.Register("TestGauge", ctx => new TProgressBar(ctx.Number("value") ?? 0));
        var (bridge, _) = TuiTestHost.Run(new RegisteredCustomView());
        Assert.Equal(0.75, Assert.IsType<TProgressBar>(bridge.FindControl("0.0")).Value, 3);
    }

    [Fact]
    public void StateChange_UpdatesTheLiveControlInPlace()
    {
        var view = new CounterView();
        var (bridge, pump) = TuiTestHost.Run(view);
        var text = Assert.IsType<TTextBlock>(bridge.FindControl("0.0"));
        Assert.Equal("0", text.Text);

        view.Increment();
        pump.Drain();

        // The patch is an updateProps, so it must mutate the SAME control instance rather than rebuild —
        // a rebuild would drop caret position, scroll offset and focus on every keystroke.
        Assert.Same(text, bridge.FindControl("0.0"));
        Assert.Equal("1", text.Text);
    }

    [Fact]
    public void ToggleValueFromState_RoundTripsIntoTheSwitch()
    {
        var view = new ToggleView();
        var (bridge, pump) = TuiTestHost.Run(view);
        var toggle = Assert.IsType<TSwitch>(bridge.FindControl("0.0"));
        Assert.False(toggle.IsOn);

        view.TurnOn();
        pump.Drain();

        Assert.Same(toggle, bridge.FindControl("0.0"));
        Assert.True(toggle.IsOn);
    }

    [Fact]
    public void SliderValueFromState_RoundTripsWithoutRebuilding()
    {
        var view = new VolumeView();
        var (bridge, pump) = TuiTestHost.Run(view);
        var slider = Assert.IsType<TSlider>(bridge.FindControl("0.0"));
        Assert.Equal(0.25, slider.Value, 3);

        view.SetVolume(0.9);
        pump.Drain();

        Assert.Same(slider, bridge.FindControl("0.0"));
        Assert.Equal(0.9, slider.Value, 3);
    }
}

file sealed class MappingView : View
{
    readonly State<bool> _on = new(false);
    readonly State<double> _volume = new(0.5);
    readonly State<string> _name = new("");

    public override View Body => new VStack(
        new Text("hello"),
        new Button("tap", () => { }),
        new Toggle("notify", _on),
        new Slider(_volume),
        new TextField("name", _name),
        new Divider(),
        new ProgressView(0.5)
    );
}

file sealed class SecureView : View
{
    readonly State<string> _password = new("");
    public override View Body => new VStack(new SecureField("password", _password));
}

file sealed class UnregisteredThing : CustomView
{
    protected override string TypeName => "UnregisteredThing";
    protected override void Configure(CustomNode node) { }
}

file sealed class CustomOnlyView : View
{
    public override View Body => new VStack(new UnregisteredThing());
}

file sealed class TestGauge : CustomView
{
    protected override string TypeName => "TestGauge";
    protected override void Configure(CustomNode node) => node.Prop("value", 0.75);
}

file sealed class RegisteredCustomView : View
{
    public override View Body => new VStack(new TestGauge());
}

file sealed class CounterView : View
{
    readonly State<int> _count = new(0);
    public void Increment() => _count.Value++;
    public override View Body => new VStack(new Text(_count.Value.ToString()));
}

file sealed class ToggleView : View
{
    readonly State<bool> _on = new(false);
    public void TurnOn() => _on.Value = true;
    public override View Body => new VStack(new Toggle("notify", _on));
}

file sealed class VolumeView : View
{
    readonly State<double> _volume = new(0.25);
    public void SetVolume(double value) => _volume.Value = value;
    public override View Body => new VStack(new Slider(_volume));
}
