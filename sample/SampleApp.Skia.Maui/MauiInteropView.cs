using SwiftDotNet;
// The SwiftDotNet DSL and MAUI share a lot of type names (View, Color, Font, WebView, DatePicker…).
// Aliases beat global usings, so every ambiguous name is pinned explicitly here.
using SdnView = SwiftDotNet.View;
using SdnWebView = SwiftDotNet.WebView;
using Font = SwiftDotNet.Font;
using Color = SwiftDotNet.Color;
using MauiDatePicker = Microsoft.Maui.Controls.DatePicker;
using MauiSwitch = Microsoft.Maui.Controls.Switch;
using MauiActivityIndicator = Microsoft.Maui.Controls.ActivityIndicator;
using ScrollView = SwiftDotNet.ScrollView;
using Button = SwiftDotNet.Button;

namespace SampleApp.Skia.Maui;

/// <summary>
/// The MAUI-interop tab: real .NET MAUI controls living inside a self-drawn SwiftDotNet tree, placed by
/// the engine's platform-view seam.
///
/// <para>Everything on this page is deliberately something the canvas <em>cannot</em> draw — a native date
/// wheel, a platform switch, a spinner with the OS's own animation, and a WebView — so if any of it shows
/// up, it is genuinely a MAUI view floating at the frame the engine computed.</para>
///
/// <para>It also exercises the two behaviours worth watching by hand on a device, because no headless test
/// can: scrolling (the controls have to track the canvas, and clip at the viewport edge) and presenting a
/// Sheet (every platform view must vanish while an overlay is up, since a native view always floats above
/// canvas pixels).</para>
/// </summary>
public sealed class MauiInteropView : SdnView
{
    readonly State<string> _date = new("(unchanged)");
    readonly State<bool> _busy = new(false);
    readonly State<bool> _sheet = new(false);

    public override SdnView Body => new Sheet(_sheet,
        body: new ScrollView(
            new VStack(
                new Text("MAUI controls, in a Skia tree")
                    .Font(Font.LargeTitle).ForegroundColor(Color.Accent),
                new Text("Each boxed control below is a real Microsoft.Maui.Controls view floated over the "
                       + "canvas at the frame the engine laid out for it.")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary),

                Section("DatePicker — a native wheel the canvas has no way to draw",
                    new MauiView(() => new MauiDatePicker())
                        .Update(v =>
                        {
                            var picker = (MauiDatePicker)v;
                            // Bind once: re-subscribing every frame would stack handlers.
                            picker.DateSelected -= OnDateSelected;
                            picker.DateSelected += OnDateSelected;
                        })
                        .Size(300, 44)),
                new Text($"Picked: {_date.Value}").Font(Font.Body).ForegroundColor(Color.Green),

                Section("Switch — drives SwiftDotNet state through OnEvent",
                    new MauiView(() => new MauiSwitch())
                        .Key("busy-switch")
                        .Update(v =>
                        {
                            var toggle = (MauiSwitch)v;
                            if (toggle.IsToggled != _busy.Value) toggle.IsToggled = _busy.Value;
                            toggle.Toggled -= OnToggled;
                            toggle.Toggled += OnToggled;
                        })
                        .OnEvent(v => _busy.Value = v == "true")
                        .Size(60, 32)),

                Section("ActivityIndicator — the OS spinner, running only while the switch is on",
                    new MauiView(() => new MauiActivityIndicator())
                        .Update(v => ((MauiActivityIndicator)v).IsRunning = _busy.Value)
                        .Size(44, 44)),

                Section("WebView — the node that used to paint \"not drawable on a canvas\"",
                    new SdnWebView("https://learn.microsoft.com/dotnet/maui/").Frame(height: 220)),

                new Button("Present a sheet (every platform view should vanish)", () => _sheet.Value = true),

                // Padding at the bottom so the last control can be scrolled clear of the home indicator.
                new Text(" ").Frame(height: 40))
            .Spacing(14).Padding(20)),

        content: new VStack(
            new Text("Sheet content").Font(Font.Title),
            new Text("The controls behind this sheet are hidden, not merely covered: a real OS control "
                   + "always floats above canvas pixels, so the engine suppresses every placement while an "
                   + "overlay is presented. A MauiView *inside* the sheet still shows —")
                .Font(Font.Caption).ForegroundColor(Color.Secondary),
            new MauiView(() => new MauiActivityIndicator { IsRunning = true }).Size(44, 44),
            new Button("Dismiss", () => _sheet.Value = false))
            .Spacing(14).Padding(24));

    void OnDateSelected(object? sender, Microsoft.Maui.Controls.DateChangedEventArgs e)
        => _date.Value = $"{e.NewDate:yyyy-MM-dd}";

    void OnToggled(object? sender, Microsoft.Maui.Controls.ToggledEventArgs e)
        => MauiViewRegistry.Emit("busy-switch", e.Value ? "true" : "false");

    static SdnView Section(string caption, SdnView content) =>
        new VStack(
                new Text(caption).Font(Font.Caption).ForegroundColor(Color.Secondary),
                content)
            .Spacing(6);
}
