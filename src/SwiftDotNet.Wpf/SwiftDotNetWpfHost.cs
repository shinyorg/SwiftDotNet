using System.Windows;

namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET WPF app as <b>real WPF controls</b>.
/// In your window:
/// <code>
/// window.Content = SwiftDotNetWpfHost.CreateRootElement(new ContentView());
/// </code>
/// </summary>
/// <remarks>
/// Named <c>SwiftDotNetWpfHost</c> rather than the <c>SwiftDotNetHost</c> the GTK and WinUI backends use,
/// on purpose: all three live in the <c>SwiftDotNet</c> namespace, and a WPF app that also raises its
/// target platform version to 10.0.19041 (common — it is how a WPF app reaches WinRT APIs) would resolve
/// SwiftDotNet's WinUI TFM and end up with two <c>SwiftDotNetHost</c>s to pick between.
/// </remarks>
public static class SwiftDotNetWpfHost
{
    /// <summary>
    /// Builds the WPF-backed root <see cref="UIElement"/> for <paramref name="root"/> and starts the
    /// render loop. From here, C# state changes drive real WPF controls.
    /// </summary>
    /// <param name="services">
    /// The container <c>[Inject]</c> properties and <c>SwiftHost.Services</c> resolve from — pass a
    /// <c>SwiftDotNetApp.Services</c> to share one container with the rest of the app.
    /// </param>
    public static UIElement CreateRootElement(View root, IServiceProvider? services = null)
    {
        var bridge = new WpfBridge();
        // WPF installs a DispatcherSynchronizationContext on the UI thread, so SwiftApp.Run captures a
        // real one here and off-thread State mutations marshal back onto the dispatcher.
        SwiftApp.Run(root, bridge, services);
        return bridge.Host;
    }
}
