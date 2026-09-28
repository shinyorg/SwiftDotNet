namespace SwiftDotNet.AndroidAuto;

using Android.OS;
using Android.Runtime;
using AndroidX.Car.App;
using AndroidX.Car.App.Constraints;

/// <summary>Mirrors the <see cref="CarNavigator"/> stack onto the Android Auto ScreenManager.</summary>
sealed class AndroidCarRenderer(CarContext carContext, Action<string>? log) : ICarRenderer
{
    readonly Dictionary<CarScreen, CarAppScreen> screens = [];
    CarNavigator nav = null!;
    CarMapSurfaceRenderer? activeSurface;

    public CarAppScreen? RootScreen { get; private set; }

    /// <summary>False when rendering without a host (diagnostics) - no map surface is requested.</summary>
    public bool SurfaceEnabled { get; init; } = true;

    ScreenManager ScreenManager => carContext.GetCarService(Java.Lang.Class.FromType(typeof(ScreenManager))).JavaCast<ScreenManager>()!;

    AppManager AppManager => carContext.GetCarService(Java.Lang.Class.FromType(typeof(AppManager))).JavaCast<AppManager>()!;

    public static void Dispatch(Action action)
    {
        if (Looper.MyLooper() == Looper.MainLooper)
            action();
        else
            new Handler(Looper.MainLooper!).Post(action);
    }

    /// <summary>Limits reported by the host's ConstraintManager (car API 2+); null when unavailable.</summary>
    public CarLimits? PlatformLimits()
    {
        try
        {
            if (carContext.CarAppApiLevel < 2)
                return null;
            var cm = carContext.GetCarService(Java.Lang.Class.FromType(typeof(ConstraintManager))).JavaCast<ConstraintManager>()!;
            var list = cm.GetContentLimit(ConstraintManager.ContentLimitTypeList);
            return CarLimits.Portable with
            {
                MaxListItems = list,
                MaxGridItems = cm.GetContentLimit(ConstraintManager.ContentLimitTypeGrid),
                MaxInfoItems = cm.GetContentLimit(ConstraintManager.ContentLimitTypePane),
                MaxPointsOfInterest = cm.GetContentLimit(ConstraintManager.ContentLimitTypePlaceList)
            };
        }
        catch (Exception ex)
        {
            log?.Invoke($"SwiftDotNet.Car: could not read Android Auto content limits: {ex.Message}");
            return null;
        }
    }

    public void Attach(CarNavigator navigator) => nav = navigator;

    // the Session returns RootScreen from OnCreateScreen
    public void SetRoot(CarScreen root) => RootScreen = Wrap(root);

    public void Push(CarScreen screen) => ScreenManager.Push(Wrap(screen));

    public void Pop() => ScreenManager.Pop();

    public void PopToRoot() => ScreenManager.PopToRoot();

    public void Present(CarModalScreen modal) => ScreenManager.Push(Wrap(modal));

    public void Dismiss(CarModalScreen modal)
    {
        if (!screens.TryGetValue(modal, out var screen))
            return;
        var manager = ScreenManager;
        if (ReferenceEquals(manager.Top, screen))
            manager.Pop();
        else
            manager.Remove(screen);
    }

    public void Invalidate(CarScreen screen)
    {
        if (screens.TryGetValue(screen, out var native))
            native.Invalidate();
    }

    CarAppScreen Wrap(CarScreen screen)
    {
        var native = new CarAppScreen(carContext, screen, nav, this);
        screens[screen] = native;
        return native;
    }

    internal void OnScreenDestroyed(CarAppScreen screen)
    {
        if (screens.TryGetValue(screen.Model, out var current) && ReferenceEquals(current, screen))
            screens.Remove(screen.Model);
    }

    internal void ActivateSurface(CarMapSurfaceRenderer surface)
    {
        if (ReferenceEquals(activeSurface, surface))
            return;
        try
        {
            AppManager.SetSurfaceCallback(surface);
            activeSurface = surface;
        }
        catch (Java.Lang.SecurityException ex)
        {
            log?.Invoke($"SwiftDotNet.Car: map surface denied - add the androidx.car.app.ACCESS_SURFACE permission. {ex.Message}");
        }
    }

    internal void DeactivateSurface(CarMapSurfaceRenderer surface)
    {
        if (!ReferenceEquals(activeSurface, surface))
            return;
        activeSurface = null;
        try
        {
            AppManager.SetSurfaceCallback(null);
        }
        catch (Java.Lang.SecurityException) { }
    }
}
