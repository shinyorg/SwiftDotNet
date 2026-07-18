using Foundation;
using UIKit;

namespace SwiftDotNet;

/// <summary>
/// Reusable tvOS application delegate that hosts a SwiftDotNet root view as real SwiftUI (focus-driven).
/// Subclass it, export the ObjC name, and return your root — the window + host-controller wiring is done
/// for you:
/// <code>
/// [Register("AppDelegate")]
/// public sealed class AppDelegate : SwiftDotNetAppDelegate
/// {
///     protected override View CreateRoot() => new ContentView();
/// }
/// // static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
/// </code>
/// </summary>
public abstract class SwiftDotNetAppDelegate : UIApplicationDelegate
{
    /// <summary>Return the root view for the app. Called once during launch.</summary>
    protected abstract View CreateRoot();

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = SwiftDotNetHost.CreateRootController(CreateRoot()),
        };
        Window.MakeKeyAndVisible();
        return true;
    }
}
