using System.Globalization;
using System.Windows;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace SwiftDotNet.Sample.Wpf;

/// <summary>
/// The WPF head: the shared <c>SampleRootView</c> rendered as <b>real WPF controls</b> (TextBlock,
/// StackPanel, Slider, TabControl, …) by the pure-C# WPF backend — no shim, no canvas.
///
/// <code>dotnet run --project sample/SampleApp.Wpf</code> (Windows only; it compiles everywhere).
/// </summary>
static class Program
{
    // WPF requires an STA thread; without this attribute the first Window constructor throws.
    [STAThread]
    static void Main() => new SampleApplication().Run();
}

sealed class SampleApplication : SwiftDotNetWpfApplication
{
    protected override Hosting.SwiftDotNetApp CreateSwiftApp()
    {
        // A custom native primitive rendered by a real WPF control, registered before the tree is built.
        // Same seam the GTK head uses — no fork of the interpreter.
        WpfRenderers.Register("NativeRating", ctx =>
        {
            var slider = new System.Windows.Controls.Slider
            {
                Minimum = 0,
                Maximum = 5,
                IsSnapToTickEnabled = true,
                TickFrequency = 1,
                Width = 160,
                Value = ctx.Number("value") ?? 0,
            };
            slider.ValueChanged += (_, e) => ctx.Emit(((int)e.NewValue).ToString(CultureInfo.InvariantCulture));
            return slider;
        });

        return SwiftProgram.CreateSwiftApp();
    }

    protected override string WindowTitle => "SwiftDotNet · WPF";
}
