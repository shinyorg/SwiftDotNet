using System.Windows;
using SwiftDotNet;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;

namespace SwiftDotNet.Sample.Skia.Wpf;

/// <summary>
/// The Skia-on-WPF head: one <see cref="SwiftDotNetSkiaElement"/> fills the window and the self-drawing
/// engine paints the whole shared UI onto it — same pixels as the macOS, Silk and MAUI Skia heads.
///
/// <code>dotnet run --project sample/SampleApp.Skia.Wpf</code> (Windows only; it compiles everywhere).
/// </summary>
static class Program
{
    // WPF requires an STA thread; without this attribute the first Window constructor throws.
    [STAThread]
    static void Main()
    {
        // Skia renderers for the sample's CustomView controls (Map, CameraView), shared with every other
        // Skia head instead of being re-registered per platform.
        SkiaSampleRenderers.RegisterAll();

        var swiftApp = SwiftProgram.CreateSwiftApp();
        var app = new Application();
        app.Run(new SwiftDotNetSkiaWindow(swiftApp) { Title = "SwiftDotNet · Skia (WPF)" });
    }
}
