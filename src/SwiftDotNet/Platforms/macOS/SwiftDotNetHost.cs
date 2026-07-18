using AppKit;

namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET macOS (AppKit) app. In your
/// <c>NSApplicationDelegate</c>:
/// <code>
/// window.ContentViewController = SwiftDotNetHost.CreateRootController(new ContentView());
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    /// <summary>
    /// Builds the SwiftUI-backed root <see cref="NSViewController"/> for <paramref name="root"/> and
    /// starts the render loop. From here, C# state changes drive real SwiftUI (AppKit-hosted).
    /// </summary>
    public static NSViewController CreateRootController(View root)
    {
        var bridge = new MacBridge();
        var controller = bridge.CreateHostController();
        SwiftApp.Run(root, bridge);
        return controller;
    }
}
