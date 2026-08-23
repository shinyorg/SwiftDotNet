using System.Windows;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// Reusable WPF <see cref="Application"/> that hosts a SwiftDotNet root view as real WPF controls.
/// Subclass it and start it from <c>Main</c> — window creation and content wiring are done for you:
/// <code>
/// public sealed class App : SwiftDotNetWpfApplication
/// {
///     protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
/// }
/// // [STAThread] static void Main() => new App().Run();
/// </code>
/// </summary>
/// <remarks>
/// The WPF twin of the WinUI backend's <c>SwiftDotNetApplication</c>, named distinctly for the same
/// reason <see cref="SwiftDotNetWpfHost"/> is — see the note there.
/// </remarks>
public abstract class SwiftDotNetWpfApplication : Application
{
    /// <summary>
    /// Build the app — services, logging and the root view. Called once during startup.
    /// The MAUI analog of <c>CreateMauiApp()</c>; put the body in a shared <c>SwiftProgram</c>.
    /// </summary>
    protected abstract SwiftDotNetApp CreateSwiftApp();

    /// <summary>Window title. Override to change it.</summary>
    protected virtual string WindowTitle => "SwiftDotNet";

    /// <summary>Initial window size, in DIPs. Override to change it.</summary>
    protected virtual System.Windows.Size WindowSize => new(440, 820);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var app = CreateSwiftApp();
        MainWindow = new Window
        {
            Title = WindowTitle,
            Width = WindowSize.Width,
            Height = WindowSize.Height,
            Content = SwiftDotNetWpfHost.CreateRootElement(app.CreateRoot(), app.Services),
        };
        MainWindow.Show();
    }
}
