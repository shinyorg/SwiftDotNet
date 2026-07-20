using Microsoft.UI.Xaml;

namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET WinUI 3 app. In your Window:
/// <code>
/// window.Content = SwiftDotNetHost.CreateRootElement(new ContentView());
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    /// <summary>
    /// Builds the WinUI-backed root <see cref="UIElement"/> for <paramref name="root"/> and starts the
    /// render loop. From here, C# state changes drive real WinUI controls.
    /// </summary>
    public static UIElement CreateRootElement(View root, IServiceProvider? services = null)
    {
        var bridge = new WinBridge();
        SwiftApp.Run(root, bridge, services);
        return bridge.Host;
    }
}
