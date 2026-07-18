using Foundation;
using UIKit;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace TvSample;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);

        // The SAME shared ContentView the other platforms render — here as SwiftUI on tvOS.
        Window.RootViewController = SwiftDotNetHost.CreateRootController(new ContentView());
        Window.MakeKeyAndVisible();
        return true;
    }
}
