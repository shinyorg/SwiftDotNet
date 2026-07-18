using Foundation;
using UIKit;
using SwiftDotNet;

namespace SampleApp;

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        Window = new UIWindow(UIScreen.MainScreen.Bounds);
        Window.RootViewController = SwiftDotNetHost.CreateRootController(new SwiftDotNet.Sample.ContentView());
        Window.MakeKeyAndVisible();
        return true;
    }
}
