namespace SwiftDotNet.AndroidAuto;

using Android.Content;
using AndroidX.Car.App;
using AndroidX.Lifecycle;

/// <summary>
/// Runs screens through the real androidx.car.app template builders without a head unit, so builder
/// validation errors (missing titles, too many actions, unsupported combinations...) show up in a phone
/// build or a test run instead of only on Android Auto. Host-side checks (and rendering) are not covered.
/// </summary>
public static class AndroidCarDiagnostics
{
    public sealed record Result(string Screen, string? Tab, bool Success, string Detail);

    public static IReadOnlyList<Result> Validate(
        Context context,
        IServiceProvider services,
        IEnumerable<Func<CarScreen>> screens,
        IEnumerable<CarModalTemplate>? modals = null,
        int hostCarApiLevel = 7)
    {
        var owner = new Owner();
        owner.Registry.HandleLifecycleEvent(Lifecycle.Event.OnCreate!);
#pragma warning disable XAOBS001 // CarContext.Create / UpdateHandshakeInfo are library-internal; only used for host-less diagnostics
        var carContext = CarContext.Create(owner.Registry)!;

        // what CarAppService does when the host binds: attach the app context and record the host API level
        var attach = Java.Lang.Class.FromType(typeof(ContextWrapper)).GetDeclaredMethod("attachBaseContext", Java.Lang.Class.FromType(typeof(Context)));
        attach!.Accessible = true;
        attach.Invoke(carContext, context.ApplicationContext!);
        carContext.UpdateHandshakeInfo(new HandshakeInfo(context.PackageName!, hostCarApiLevel));
#pragma warning restore XAOBS001

        var options = services.GetService(typeof(CarOptions)) as CarOptions ?? new CarOptions();
        var results = new List<Result>();
        var all = screens.Concat((modals ?? []).Select(m => (Func<CarScreen>)(() => new CarModalScreen(m))));
        foreach (var factory in all)
        {
            var screen = factory();
            var name = screen.GetType().Name;
            try
            {
                var renderer = new AndroidCarRenderer(carContext, options.Log) { SurfaceEnabled = false };
                var navigator = new CarNavigator(renderer, services, options, renderer.PlatformLimits(), a => a(), CarPlatform.AndroidAuto);
                navigator.Start(screen);
                var native = renderer.RootScreen!;

                if (navigator.BuildTemplate(screen) is CarTabsTemplate tabs)
                {
                    foreach (var tab in tabs.Tabs)
                        results.Add(Run(name, tab.Id, () => native.RenderForDiagnostics(tab.Id)));
                }
                else
                {
                    results.Add(Run(name, null, () => native.RenderForDiagnostics(null)));
                }
                navigator.Stop();
            }
            catch (Exception ex)
            {
                results.Add(new Result(name, null, false, ex.Message));
            }
        }
        return results;
    }

    static Result Run(string screen, string? tab, Func<AndroidX.Car.App.Model.ITemplate> render)
    {
        try
        {
            var template = render();
            return new Result(screen, tab, true, ((Java.Lang.Object)template).Class.SimpleName);
        }
        catch (Exception ex)
        {
            return new Result(screen, tab, false, ex.Message);
        }
    }

    sealed class Owner : Java.Lang.Object, ILifecycleOwner
    {
        public Owner() => Registry = new LifecycleRegistry(this);
        public LifecycleRegistry Registry { get; }
        public Lifecycle Lifecycle => Registry;
    }
}
