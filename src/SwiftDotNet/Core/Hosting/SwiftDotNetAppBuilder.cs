using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SwiftDotNet.Hosting;

/// <summary>
/// Configures a <see cref="SwiftDotNetApp"/>. The analog of .NET MAUI's <c>MauiAppBuilder</c>:
/// registrations go on <see cref="Services"/>, logging on <see cref="Logging"/>, and optional
/// SwiftDotNet libraries opt in through <c>UseX()</c> extension methods.
/// </summary>
/// <remarks>
/// <c>Configuration</c> is deliberately not exposed yet: <c>ConfigurationManager</c>'s binding is
/// reflection-based and is the usual trim casualty under iOS AOT. It can be added when there is a
/// concrete need.
/// </remarks>
public sealed class SwiftDotNetAppBuilder
{
    Func<IServiceProvider, View>? _rootFactory;

    internal SwiftDotNetAppBuilder() => Logging = new BuilderLoggingBuilder(Services);

    /// <summary>The app's service registrations.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>Logging configuration, equivalent to <c>MauiAppBuilder.Logging</c>.</summary>
    public ILoggingBuilder Logging { get; }

    /// <summary>
    /// Set the factory used for the root view. Called by
    /// <see cref="SwiftDotNetAppBuilderExtensions.UseSwiftApp{TRoot}(SwiftDotNetAppBuilder)"/>; last call wins.
    /// </summary>
    internal void SetRootFactory(Func<IServiceProvider, View> factory) => _rootFactory = factory;

    /// <summary>Build the provider and return the app handle.</summary>
    /// <exception cref="InvalidOperationException">No root view was registered.</exception>
    public SwiftDotNetApp Build()
    {
        var rootFactory = _rootFactory ?? throw new InvalidOperationException(
            "No root view registered. Call builder.UseSwiftApp<TRoot>() before Build().");

        Services.AddLogging();

        // DI is delivered as a lifecycle observer like any other. Scoped, so it injects from whichever
        // provider the view was created against — app-wide by default, or a ViewScope's when there is one.
        Services.AddScoped<IViewLifecycle, InjectionViewLifecycle>();

        var provider = Services.BuildServiceProvider();

        // Application-wide initializers, in registration order. scoped: false — this is the root
        // provider, and this call happens exactly once per app.
        foreach (var initializer in provider.GetServices<ISwiftInitializer>())
            initializer.Initialize(provider, scoped: false);

        return new SwiftDotNetApp(provider, rootFactory);
    }

    /// <summary>
    /// Minimal <see cref="ILoggingBuilder"/> over the same collection, so <c>builder.Logging.AddDebug()</c>
    /// works without pulling in the generic host.
    /// </summary>
    sealed class BuilderLoggingBuilder(IServiceCollection services) : ILoggingBuilder
    {
        public IServiceCollection Services { get; } = services;
    }
}
