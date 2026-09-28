using CarPlay;
using Foundation;

namespace SwiftDotNet.CarPlay;

/// <summary>
/// The CarPlay scene. Referenced from Info.plist (<c>UISceneDelegateClassName = SwiftDotNetCarPlaySceneDelegate</c>)
/// under the <c>CPTemplateApplicationSceneSessionRoleApplication</c> role — <c>SwiftDotNetCar.targets</c> merges
/// that entry for you.
/// </summary>
/// <remarks>
/// CarPlay can launch the app straight into this scene with no phone scene, but UIKit still runs the app
/// delegate's <c>FinishedLaunching</c> first, so <see cref="Hosting.SwiftDotNetApp.Current"/> is already built
/// and this scene shares the phone UI's container.
/// </remarks>
[Register("SwiftDotNetCarPlaySceneDelegate")]
public class SwiftDotNetCarPlaySceneDelegate : CPTemplateApplicationSceneDelegate
{
    CarNavigator? navigator;
    IServiceProvider? services;

    // Non-navigation categories (audio, charging, parking, ...)
    public override void DidConnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController)
        => Connect(interfaceController, null);

    // Navigation apps (com.apple.developer.carplay-maps) also receive the window used as the map base layer
    public override void DidConnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController, CPWindow window)
        => Connect(interfaceController, window);

    public override void DidDisconnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController)
        => Disconnect();

    public override void DidDisconnect(CPTemplateApplicationScene templateApplicationScene, CPInterfaceController interfaceController, CPWindow window)
        => Disconnect();

    void Connect(CPInterfaceController interfaceController, CPWindow? window)
    {
        services = CarHost.Resolve(null);
        var options = services.GetService(typeof(CarOptions)) as CarOptions ?? new CarOptions();
        var renderer = new CarPlayRenderer(interfaceController, window, options.Log);
        navigator = CarSession.Start(CarPlatform.CarPlay, services, renderer, CarPlayRenderer.PlatformLimits(), CarPlayRenderer.Dispatch);
    }

    void Disconnect()
    {
        if (navigator is not null && services is not null)
            CarSession.Stop(services, navigator);
        navigator = null;
    }
}
