namespace SwiftDotNet;

/// <summary>
/// Entry point of a car experience. Register with <c>builder.UseCar&lt;TApp&gt;()</c> (or <c>services.AddCar&lt;TApp&gt;()</c>); the platform
/// renderer creates the root screen each time a head unit connects.
/// </summary>
public abstract class CarApp
{
    public abstract CarScreen CreateRootScreen(IServiceProvider services);

    /// <summary>A head unit connected and the root screen is on screen.</summary>
    public virtual void OnConnected(ICarNavigator navigator) { }

    public virtual void OnDisconnected() { }
}

public enum CarPlatform
{
    CarPlay,
    AndroidAuto
}

/// <summary>
/// Phone-side view of the car session (inject it into phone pages/view models to show connection state).
/// </summary>
public interface ICarConnection
{
    bool IsConnected { get; }
    CarPlatform? Platform { get; }
    ICarNavigator? Navigator { get; }
    event EventHandler? StateChanged;
}

/// <summary>Default <see cref="ICarConnection"/>; renderers call <see cref="SetConnected"/>/<see cref="SetDisconnected"/>.</summary>
public sealed class CarConnection : ICarConnection
{
    public bool IsConnected => Navigator is not null;
    public CarPlatform? Platform { get; private set; }
    public ICarNavigator? Navigator { get; private set; }
    public event EventHandler? StateChanged;

    public void SetConnected(CarPlatform platform, ICarNavigator navigator)
    {
        Platform = platform;
        Navigator = navigator;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetDisconnected()
    {
        Platform = null;
        Navigator = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Shared connect/disconnect orchestration used by both renderers.</summary>
public static class CarSession
{
    public static CarNavigator Start(CarPlatform platform, IServiceProvider services, ICarRenderer renderer, CarLimits? platformLimits, Action<Action> dispatch)
    {
        var options = services.GetService(typeof(CarOptions)) as CarOptions ?? new CarOptions();
        var app = services.GetService(typeof(CarApp)) as CarApp
            ?? throw new InvalidOperationException("No CarApp registered. Call builder.UseCar<TApp>() (or services.AddCar<TApp>()).");

        var navigator = new CarNavigator(renderer, services, options, platformLimits, dispatch, platform);
        navigator.Start(app.CreateRootScreen(services));
        (services.GetService(typeof(ICarConnection)) as CarConnection)?.SetConnected(platform, navigator);
        app.OnConnected(navigator);
        return navigator;
    }

    public static void Stop(IServiceProvider services, CarNavigator navigator)
    {
        navigator.Stop();
        (services.GetService(typeof(ICarConnection)) as CarConnection)?.SetDisconnected();
        (services.GetService(typeof(CarApp)) as CarApp)?.OnDisconnected();
    }
}
