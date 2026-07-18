using Microsoft.UI.Xaml;

namespace SwiftDotNet;

/// <summary>
/// Reusable WinUI 3 <see cref="Application"/> that hosts a SwiftDotNet root view as real WinUI controls.
/// Subclass it and start it from <c>Main</c> — the control-style resources, window creation and content
/// wiring are done for you:
/// <code>
/// public sealed class App : SwiftDotNetApplication
/// {
///     protected override View CreateRoot() => new ContentView();
/// }
/// // [STAThread] static void Main() => Application.Start(_ => _ = new App());
/// </code>
/// </summary>
public abstract class SwiftDotNetApplication : Application
{
    Window? _window;

    /// <summary>Return the root view for the app. Called once during launch.</summary>
    protected abstract View CreateRoot();

    /// <summary>Window title. Override to change it.</summary>
    protected virtual string WindowTitle => "SwiftDotNet";

    protected SwiftDotNetApplication()
    {
        // WinUI control styles (normally supplied by App.xaml's XamlControlsResources).
        Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = WindowTitle };
        _window.Content = SwiftDotNetHost.CreateRootElement(CreateRoot());
        _window.Activate();
    }
}
