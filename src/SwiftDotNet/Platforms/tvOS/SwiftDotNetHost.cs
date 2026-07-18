using UIKit;

namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET tvOS app. In your <c>AppDelegate</c>:
/// <code>
/// Window = new UIWindow(UIScreen.MainScreen.Bounds);
/// Window.RootViewController = SwiftDotNetHost.CreateRootController(new ContentView());
/// Window.MakeKeyAndVisible();
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    /// <summary>
    /// Builds the SwiftUI-backed root <see cref="UIViewController"/> for <paramref name="root"/> and starts
    /// the render loop. From here, C# state changes drive real SwiftUI (focus-driven on tvOS).
    /// </summary>
    public static UIViewController CreateRootController(View root)
    {
        var bridge = new TvBridge();
        var controller = bridge.CreateHostController();
        SwiftApp.Run(root, bridge);
        return controller;
    }
}
