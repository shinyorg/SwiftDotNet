using Foundation;
using UIKit;

namespace SwiftDotNet;

/// <summary>
/// Reusable iOS application delegate that hosts a SwiftDotNet root view as real SwiftUI. Subclass it,
/// export the ObjC name, and return your root — the window + host-controller wiring is done for you:
/// <code>
/// [Register("AppDelegate")]
/// public sealed class AppDelegate : SwiftDotNetAppDelegate
/// {
///     protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
/// }
/// // static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
/// </code>
/// </summary>
public abstract class SwiftDotNetAppDelegate : UIApplicationDelegate
{
    /// <summary>
    /// Build the app — services, logging and the root view. Called once during launch.
    /// The MAUI analog of <c>CreateMauiApp()</c>; put the body in a shared <c>SwiftProgram</c>.
    /// </summary>
    protected abstract Hosting.SwiftDotNetApp CreateSwiftApp();

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        var app = Hosting.SwiftDotNetApp.GetOrCreate(CreateSwiftApp);

        // With a scene manifest (e.g. a CarPlay app), UIKit drives windows per scene: a window made here
        // would have no scene and never show. SwiftDotNetSceneDelegate builds the phone window instead,
        // from the same app.
        if (UsesScenes)
            return true;

        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = SwiftDotNetHost.CreateRootController(app.CreateRoot(), app.Services),
        };
        Window.MakeKeyAndVisible();
        return true;
    }

    /// <summary>True when Info.plist declares <c>UIApplicationSceneManifest</c>.</summary>
    internal static bool UsesScenes => NSBundle.MainBundle.ObjectForInfoDictionary("UIApplicationSceneManifest") is not null;
}
