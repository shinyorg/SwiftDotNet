using Microsoft.UI.Xaml;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace WindowsSample;

public static class Program
{
    [STAThread]
    static void Main()
    {
        // Code-only WinUI 3 bootstrap (no App.xaml).
        Application.Start(_ => _ = new App());
    }
}

/// <summary>Minimal code-only WinUI 3 Application hosting the shared ContentView.</summary>
public sealed class App : Application
{
    Window? _window;

    public App()
    {
        // WinUI control styles (normally supplied by App.xaml's XamlControlsResources).
        Resources.MergedDictionaries.Add(new Microsoft.UI.Xaml.Controls.XamlControlsResources());
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window { Title = "SwiftDotNet" };

        // The SAME shared ContentView the iOS/macOS/Android/GTK apps render — here as WinUI.
        _window.Content = SwiftDotNetHost.CreateRootElement(new ContentView());
        _window.Activate();
    }
}
