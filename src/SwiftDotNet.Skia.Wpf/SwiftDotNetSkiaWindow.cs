using System.Windows;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// A ready-made WPF <see cref="Window"/> whose entire content is a SwiftDotNet view tree drawn by the
/// Skia backend. The parallel of the GTK head's <c>SwiftDotNetHost.Run</c> — window creation and the
/// render loop are done for you:
/// <code>
/// [STAThread]
/// static void Main()
/// {
///     var app = new Application();
///     app.Run(new SwiftDotNetSkiaWindow(SwiftProgram.CreateSwiftApp()));
/// }
/// </code>
/// </summary>
public class SwiftDotNetSkiaWindow : Window
{
    /// <summary>Hosts <paramref name="root"/>, resolving injections from <paramref name="services"/>.</summary>
    public SwiftDotNetSkiaWindow(View root, IServiceProvider? services = null)
    {
        Title = "SwiftDotNet";
        Width = 440;
        Height = 820;
        Surface = new SwiftDotNetSkiaElement(root, services);
        Content = Surface;
    }

    /// <summary>Hosts the app's root view and shares its DI container.</summary>
    public SwiftDotNetSkiaWindow(SwiftDotNetApp app)
        : this((app ?? throw new ArgumentNullException(nameof(app))).CreateRoot(), app.Services)
    {
    }

    /// <summary>The canvas element, for reaching the bridge or the gesture router.</summary>
    public SwiftDotNetSkiaElement Surface { get; }
}
