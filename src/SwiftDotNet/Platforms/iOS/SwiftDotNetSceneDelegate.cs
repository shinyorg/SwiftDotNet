using Foundation;
using UIKit;

namespace SwiftDotNet;

/// <summary>
/// The phone window scene, for apps whose Info.plist declares a <c>UIApplicationSceneManifest</c>.
/// </summary>
/// <remarks>
/// <para>Most apps never need this: without a scene manifest, <see cref="SwiftDotNetAppDelegate"/> creates
/// the window itself. A scene manifest becomes mandatory the moment an app has a second kind of scene — a
/// CarPlay scene is the reason this exists — and from then on UIKit only shows windows that belong to a
/// scene. Point <c>UIWindowSceneSessionRoleApplication</c> at this class
/// (<c>UISceneDelegateClassName = SwiftDotNetSceneDelegate</c>); <c>SwiftDotNetCar.targets</c> does it for
/// you.</para>
/// <para>The app itself is still built by the app delegate's <c>CreateSwiftApp()</c>, which UIKit always
/// runs first, so the phone scene and any car scene share one container.</para>
/// </remarks>
[Register("SwiftDotNetSceneDelegate")]
public class SwiftDotNetSceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public virtual void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        var app = Hosting.SwiftDotNetApp.Current ?? throw new InvalidOperationException(
            "SwiftDotNetSceneDelegate connected before the app was built. The app delegate must derive from " +
            "SwiftDotNetAppDelegate so CreateSwiftApp() runs in FinishedLaunching.");

        Window = new UIWindow(windowScene)
        {
            RootViewController = SwiftDotNetHost.CreateRootController(app.CreateRoot(), app.Services),
        };
        Window.MakeKeyAndVisible();
    }
}
