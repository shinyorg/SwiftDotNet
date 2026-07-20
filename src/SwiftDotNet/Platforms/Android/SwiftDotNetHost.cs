using Android.Content;
using AndroidView = Android.Views.View;

namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET Android app. In your Activity:
/// <code>
/// SetContentView(SwiftDotNetHost.CreateRootView(this, new ContentView()));
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    /// <summary>
    /// Builds the Compose-backed root <see cref="AndroidView"/> for <paramref name="root"/> and
    /// starts the render loop. From here, C# state changes drive real Jetpack Compose.
    /// </summary>
    public static AndroidView CreateRootView(Context context, View root, IServiceProvider? services = null)
    {
        var bridge = new AndroidBridge();
        var view = bridge.CreateHostView(context);
        SwiftApp.Run(root, bridge, services);
        return view;
    }
}
