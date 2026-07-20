namespace SwiftDotNet;

/// <summary>
/// The running app's service provider, plus the resolution helpers everything else calls.
///
/// This is deliberately <b>resolution-only</b> — composition lives on
/// <see cref="Hosting.SwiftDotNetApp.CreateBuilder"/>. It is also deliberately bound to
/// <see cref="IServiceProvider"/> (a BCL type) rather than any container's API, so the render path,
/// <see cref="View.Service{TService}"/> and generated <c>[Inject]</c> code never take a dependency on
/// Microsoft.Extensions.DependencyInjection even though the hosting layer above does.
/// </summary>
public static class SwiftHost
{
    /// <summary>
    /// The provider for the running app. Set by <see cref="SwiftApp.Run(View, IBridge, IServiceProvider?)"/>;
    /// assignable directly in tests to swap in fakes.
    /// </summary>
    public static IServiceProvider? Services { get; set; }

    /// <summary>
    /// The provider currently in effect: the active scope's provider when one is entered (see
    /// <see cref="EnterScope"/>), otherwise the app-wide <see cref="Services"/>.
    /// </summary>
    /// <remarks>
    /// A plain static is correct here rather than <c>AsyncLocal</c>: scopes are entered and exited around
    /// synchronous work on the UI thread (building a page's node tree, dispatching its event callbacks),
    /// and <see cref="SwiftApp"/> marshals renders to that one thread. An <c>async void</c> handler that
    /// awaits will resume *after* the scope has been exited, so capture what you need before awaiting.
    /// </remarks>
    public static IServiceProvider? ActiveScope { get; private set; }

    /// <summary>
    /// Make <paramref name="scope"/> the provider that <c>Service&lt;T&gt;()</c> and generated
    /// <c>[Inject]</c> code resolve from, until the returned handle is disposed. Nests correctly.
    /// </summary>
    public static IDisposable EnterScope(IServiceProvider scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var previous = ActiveScope;
        ActiveScope = scope;
        return new ScopeHandle(previous);
    }

    sealed class ScopeHandle(IServiceProvider? previous) : IDisposable
    {
        bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            ActiveScope = previous;
        }
    }

    // Explicit-provider overloads. These are what the [Inject] source generator emits, so the
    // generated code needs nothing but this type in scope.

    /// <summary>Resolve a required service, throwing a registration-shaped message if it is missing.</summary>
    public static TService Require<TService>(IServiceProvider provider) where TService : notnull
    {
        ArgumentNullException.ThrowIfNull(provider);
        return (TService?)provider.GetService(typeof(TService)) ?? throw new InvalidOperationException(
            $"No service registered for {typeof(TService)}. Register it in SwiftProgram.CreateSwiftApp().");
    }

    /// <summary>Resolve an optional service; <c>default</c> when unregistered.</summary>
    public static TService? Optional<TService>(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        return (TService?)provider.GetService(typeof(TService));
    }

    // Ambient overloads, for View.Service<T>() and hand-written call sites.

    /// <inheritdoc cref="Require{TService}(IServiceProvider)"/>
    public static TService Require<TService>() where TService : notnull => Require<TService>(Current);

    /// <inheritdoc cref="Optional{TService}(IServiceProvider)"/>
    public static TService? Optional<TService>()
        => (ActiveScope ?? Services) is { } sp ? Optional<TService>(sp) : default;

    static IServiceProvider Current => ActiveScope ?? Services ?? throw new InvalidOperationException(
        "No SwiftDotNet service provider is running. Build one with SwiftDotNetApp.CreateBuilder(), or " +
        "assign SwiftHost.Services directly in tests.");
}
