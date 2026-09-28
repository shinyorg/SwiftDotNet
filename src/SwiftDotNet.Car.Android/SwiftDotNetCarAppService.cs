// androidx.car.app builder bindings are annotated as returning nullable builders/objects but never return null.
#pragma warning disable CS8602, CS8603, CS8604

namespace SwiftDotNet.AndroidAuto;

using Android.Content;
using Android.Content.PM;
using AndroidX.Car.App;
using AndroidX.Car.App.Validation;
using AndroidX.Lifecycle;
using SwiftDotNet.Hosting;

/// <summary>
/// The Android Auto entry point. Subclass it in the app — the category is app-specific, so it goes on your
/// own <c>[IntentFilter]</c> — and return the same app the phone activity builds:
/// <code>
/// [Service(Exported = true)]
/// [IntentFilter(["androidx.car.app.CarAppService"], Categories = ["androidx.car.app.category.CHARGING"])]
/// public class CarService : SwiftDotNetCarAppService
/// {
///     protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
/// }
/// </code>
/// </summary>
/// <remarks>
/// Android binds this service without starting an activity when the phone connects to a car, so
/// <see cref="CreateSwiftApp"/> may be the first thing that builds the app. It goes through
/// <see cref="SwiftDotNetApp.GetOrCreate"/>, so if the phone UI is (or later becomes) open, both share one
/// container.
/// </remarks>
public abstract class SwiftDotNetCarAppService : CarAppService
{
    /// <summary>Build the app. Put the body in the shared <c>SwiftProgram</c>, exactly as the activity does.</summary>
    protected abstract SwiftDotNetApp CreateSwiftApp();

    public override HostValidator CreateHostValidator()
    {
        var options = SwiftDotNet.CarHost.Resolve(CreateSwiftApp).GetService(typeof(CarOptions)) as CarOptions;
        var debuggable = (ApplicationInfo!.Flags & ApplicationInfoFlags.Debuggable) != 0;
        if (options?.AndroidAllowAllHosts ?? debuggable)
            return HostValidator.AllowAllHostsValidator;

        // allow-list shipped with androidx.car.app (Android Auto, Android Automotive templates host, ...)
        var allowList = Resources!.GetIdentifier("hosts_allowlist_sample", "array", PackageName);
        return new HostValidator.Builder(ApplicationContext!).AddAllowedHosts(allowList).Build();
    }

    public override Session OnCreateSession() => new SwiftDotNetCarSession(SwiftDotNet.CarHost.Resolve(CreateSwiftApp));
}

/// <summary>One Android Auto connection: creates the navigator and the root screen.</summary>
public sealed class SwiftDotNetCarSession : Session
{
    readonly IServiceProvider services;
    CarNavigator? navigator;

    public SwiftDotNetCarSession(IServiceProvider services) => this.services = services;

    public override Screen OnCreateScreen(Intent? p0)
    {
        var options = services.GetService(typeof(CarOptions)) as CarOptions ?? new CarOptions();
        var renderer = new AndroidCarRenderer(CarContext, options.Log);
        navigator = CarSession.Start(CarPlatform.AndroidAuto, services, renderer, renderer.PlatformLimits(), AndroidCarRenderer.Dispatch);
        Lifecycle.AddObserver(new SessionObserver(this));
        return renderer.RootScreen!;
    }

    void OnDestroyed()
    {
        if (navigator is not null)
            CarSession.Stop(services, navigator);
        navigator = null;
    }

    sealed class SessionObserver(SwiftDotNetCarSession session) : Java.Lang.Object, IDefaultLifecycleObserver
    {
        public void OnDestroy(ILifecycleOwner owner) => session.OnDestroyed();
    }
}
