using Microsoft.Extensions.DependencyInjection;

namespace SwiftDotNet.Hosting;

/// <summary>
/// Raises lifecycle events on a retained view and on every <see cref="IViewLifecycle"/> registered in
/// the container.
///
/// Dependency injection is itself just a registered observer (<see cref="InjectionViewLifecycle"/>),
/// so the ordering rule below is what guarantees a view's <c>[Inject]</c> members are filled before its
/// own <c>OnCreated</c> runs — no special-casing.
/// </summary>
/// <remarks>
/// <b>Ordering.</b> Setup (<c>Created</c>, <c>Appearing</c>) runs observers first, then the view. Teardown
/// (<c>Disappearing</c>, <c>Destroyed</c>) runs the view first, then observers — mirroring construction and
/// disposal order, so an observer that tears down state does it after the view is finished with it.
/// </remarks>
public static class ViewLifecycleDispatcher
{
    /// <summary>Raise <c>OnCreated</c>: observers (which include the <c>[Inject]</c> fill), then the view.</summary>
    public static void Created(View view, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var observer in Observers(services))
            observer.OnCreated(view);
        view.OnCreated();
    }

    /// <summary>Raise <c>OnAppearing</c>: observers, then the view.</summary>
    public static void Appearing(View view, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(services);

        foreach (var observer in Observers(services))
            observer.OnAppearing(view);
        view.OnAppearing();
    }

    /// <summary>Raise <c>OnDisappearing</c>: the view, then observers.</summary>
    public static void Disappearing(View view, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(services);

        view.OnDisappearing();
        foreach (var observer in Observers(services))
            observer.OnDisappearing(view);
    }

    /// <summary>Raise <c>OnDestroyed</c>: the view, then observers.</summary>
    public static void Destroyed(View view, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(services);

        view.OnDestroyed();
        foreach (var observer in Observers(services))
            observer.OnDestroyed(view);
    }

    static IEnumerable<IViewLifecycle> Observers(IServiceProvider services) =>
        services.GetServices<IViewLifecycle>();
}

/// <summary>
/// The framework's own lifecycle observer: fills a view's <c>[Inject]</c> members on creation, from
/// whichever provider it was resolved out of.
/// </summary>
/// <remarks>
/// Registered by <see cref="SwiftDotNetAppBuilder.Build"/> as <b>scoped</b>, so resolving it from a
/// <see cref="ViewScope"/> injects that scope's services, while resolving it from the app provider
/// injects app-wide (non-scoped) ones. That is the whole of the scoped-vs-not distinction — there is no
/// second code path.
/// </remarks>
sealed class InjectionViewLifecycle(IServiceProvider services) : IViewLifecycle
{
    public void OnCreated(View view) => view.InjectServices(services);
    public void OnAppearing(View view) { }
    public void OnDisappearing(View view) { }
    public void OnDestroyed(View view) { }
}
