using Microsoft.Extensions.DependencyInjection;

namespace SwiftDotNet.Hosting;

/// <summary>
/// A built SwiftDotNet application: the service provider plus the ability to create the root view.
/// The analog of .NET MAUI's <c>MauiApp</c>.
///
/// Apps build one of these in a shared <c>SwiftProgram.CreateSwiftApp()</c>, and each platform head
/// does nothing but return it:
/// <code>
/// public static class SwiftProgram
/// {
///     public static SwiftDotNetApp CreateSwiftApp()
///     {
///         var builder = SwiftDotNetApp.CreateBuilder();
///         builder.UseSwiftApp&lt;ContentView&gt;();
///         builder.Services.AddSingleton&lt;IWeatherService, WeatherService&gt;();
///         return builder.Build();
///     }
/// }
/// </code>
/// </summary>
public sealed class SwiftDotNetApp
{
    readonly Func<IServiceProvider, View> _rootFactory;
    View? _root;

    internal SwiftDotNetApp(IServiceProvider services, Func<IServiceProvider, View> rootFactory)
    {
        Services = services;
        _rootFactory = rootFactory;
    }

    /// <summary>Start configuring an app. Mirrors <c>MauiApp.CreateBuilder()</c>.</summary>
    public static SwiftDotNetAppBuilder CreateBuilder() => new();

    /// <summary>The app's service container.</summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Create the root view registered with <see cref="SwiftDotNetAppBuilderExtensions.UseSwiftApp{TRoot}(SwiftDotNetAppBuilder)"/>,
    /// raising <c>OnCreated</c> (which fills its <c>[Inject]</c> members) then <c>OnAppearing</c> — on the
    /// view itself and on every registered <see cref="IViewLifecycle"/>.
    /// </summary>
    /// <remarks>
    /// <b>Not scoped.</b> The root is retained for the life of the app, so a dedicated scope would live
    /// just as long and buy nothing; injection comes from the app-wide provider. Scoped views need a
    /// create/destroy boundary — see <see cref="ViewScope"/>, which the paused navigation plan will use
    /// for pushed pages. The root is created once; repeat calls return the same instance.
    /// </remarks>
    public View CreateRoot()
    {
        if (_root is not null)
            return _root;

        _root = _rootFactory(Services);
        ViewLifecycleDispatcher.Created(_root, Services);
        ViewLifecycleDispatcher.Appearing(_root, Services);
        return _root;
    }
}
