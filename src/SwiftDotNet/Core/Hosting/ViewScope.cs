using Microsoft.Extensions.DependencyInjection;

namespace SwiftDotNet.Hosting;

/// <summary>
/// A retained view plus the <see cref="IServiceScope"/> it owns — the unit that makes <c>[Inject]</c>
/// and <c>Service&lt;T&gt;()</c> resolve <b>scoped</b> services correctly.
///
/// The scope is the boundary at which a view is created and destroyed: today that is the root view, and
/// (once <c>INavigator</c> lands) each page pushed onto the navigation stack. A page's scoped
/// services — a per-screen view-model, an editing context, an <c>HttpClient</c> — live exactly as long
/// as the page is on the stack and are disposed when it is popped.
/// </summary>
/// <remarks>
/// <para><b>Why the scope boundary is the page, not the view.</b> Views built inline inside a
/// <c>Body</c> are rebuilt on every render pass, so giving each one a scope would create and dispose
/// scopes continuously. Retained views are the only place a scope has a meaningful lifetime. This is
/// why scoped injection arrives with navigation rather than waiting on full view reconciliation.</para>
/// <para>Anything resolved through <see cref="Use"/> comes from this scope; outside it, resolution
/// falls back to the app-wide provider.</para>
/// </remarks>
public sealed class ViewScope : IDisposable
{
    readonly IServiceScope _scope;
    bool _appeared;
    bool _disposed;

    ViewScope(IServiceScope scope, View view)
    {
        _scope = scope;
        View = view;
    }

    /// <summary>The retained view this scope owns.</summary>
    public View View { get; }

    /// <summary>The scope's provider. Scoped registrations resolve to instances unique to this view.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>
    /// Create a scope, build the view inside it, fill its <c>[Inject]</c> members from the <b>scoped</b>
    /// provider, and raise <see cref="IViewLifecycle.OnCreated"/>.
    /// </summary>
    /// <param name="provider">The parent provider — usually the app-wide one.</param>
    /// <param name="factory">Builds the view; runs with the new scope active, so it may resolve from it.</param>
    public static ViewScope Create(IServiceProvider provider, Func<IServiceProvider, View> factory)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(factory);

        var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        // Per-scope initializers run before anything resolves out of the scope, so an initializer can
        // seed scoped state the view's services depend on.
        foreach (var initializer in services.GetServices<ISwiftInitializer>())
            initializer.Initialize(services, scoped: true);

        View view;
        using (SwiftHost.EnterScope(services))
        {
            view = factory(services);
            // Raises the scoped InjectionViewLifecycle, so [Inject] members resolve from this scope.
            ViewLifecycleDispatcher.Created(view, services);
        }

        return new ViewScope(scope, view);
    }

    /// <summary>
    /// Run <paramref name="work"/> with this scope active, so <c>Service&lt;T&gt;()</c> inside the view's
    /// <c>Body</c> — and inside its event callbacks — resolves scoped services from here. Hosts wrap
    /// render passes and event dispatch in this.
    /// </summary>
    public void Use(Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        using (SwiftHost.EnterScope(Services))
            work();
    }

    /// <summary>Raise <c>OnAppearing</c> on the view and observers. Idempotent while already visible.</summary>
    public void Appearing()
    {
        if (_disposed || _appeared)
            return;
        _appeared = true;
        Use(() => ViewLifecycleDispatcher.Appearing(View, Services));
    }

    /// <summary>Raise <c>OnDisappearing</c> on the view and observers. Idempotent while already hidden.</summary>
    public void Disappearing()
    {
        if (_disposed || !_appeared)
            return;
        _appeared = false;
        Use(() => ViewLifecycleDispatcher.Disappearing(View, Services));
    }

    /// <summary>
    /// Raise <c>OnDisappearing</c> (if still visible) then <c>OnDestroyed</c>, and dispose the scope — in that
    /// order, so lifecycle callbacks can still resolve scoped services and scoped
    /// <see cref="IDisposable"/> services are disposed afterwards.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Disappearing();
        Use(() => ViewLifecycleDispatcher.Destroyed(View, Services));

        _disposed = true;
        _scope.Dispose();
    }
}
