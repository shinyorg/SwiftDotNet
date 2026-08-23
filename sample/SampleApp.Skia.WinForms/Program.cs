using System.Windows.Forms;
using SwiftDotNet;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;

namespace SwiftDotNet.Sample.Skia.WinForms;

/// <summary>
/// The Skia-on-WinForms head: one <see cref="SwiftDotNetSkiaControl"/> fills the form and the
/// self-drawing engine paints the whole shared UI onto it. This is the <em>only</em> WinForms backend —
/// see the remarks on <see cref="SwiftDotNetSkiaControl"/> for why there is no native-control one.
///
/// <code>dotnet run --project sample/SampleApp.Skia.WinForms</code> (Windows only; it compiles everywhere).
/// </summary>
static class Program
{
    // WinForms requires an STA thread; without this attribute the message loop refuses to start.
    [STAThread]
    static void Main()
    {
        // Per-monitor DPI awareness + visual styles. Without it the canvas is bitmap-stretched by the OS
        // on a HiDPI display and every glyph is blurry, since the engine already renders at device scale.
        ApplicationConfiguration.Initialize();

        // Skia renderers for the sample's CustomView controls (Map, CameraView), shared with every other
        // Skia head instead of being re-registered per platform.
        SkiaSampleRenderers.RegisterAll();

        var swiftApp = SwiftProgram.CreateSwiftApp();
        System.Windows.Forms.Application.Run(new SwiftDotNetSkiaForm(swiftApp) { Text = "SwiftDotNet · Skia (WinForms)" });
    }
}
