using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

public static class CarHostingExtensions
{
    /// <summary>
    /// Registers the car experience on the app builder. The car renderers resolve <typeparamref name="TApp"/>
    /// from the same container as the phone UI, so a service shared by both (a repository, a location feed)
    /// keeps phone and car in sync.
    /// <para>Platform wiring still needed in the head:
    /// iOS — the CarPlay scene manifest (import <c>SwiftDotNetCar.targets</c>) and a CarPlay category entitlement;
    /// Android — a <c>[Service]</c> subclass of <c>SwiftDotNetCarAppService</c> with the car-app intent filter and category.</para>
    /// </summary>
    public static SwiftDotNetAppBuilder UseCar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this SwiftDotNetAppBuilder builder, Action<CarOptions>? configure = null)
        where TApp : CarApp
    {
        builder.Services.AddCar<TApp>(configure);
        return builder;
    }

    /// <summary>The <see cref="IServiceCollection"/> form of <see cref="UseCar{TApp}"/>, for hosts that build their own container.</summary>
    public static IServiceCollection AddCar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApp>(
        this IServiceCollection services, Action<CarOptions>? configure = null)
        where TApp : CarApp
    {
        var options = new CarOptions();
        configure?.Invoke(options);

        services.TryAddSingleton(options);
        services.TryAddSingleton<CarConnection>();
        services.TryAddSingleton<ICarConnection>(sp => sp.GetRequiredService<CarConnection>());
        services.TryAddSingleton<TApp>();
        services.TryAddSingleton<CarApp>(sp => sp.GetRequiredService<TApp>());
        return services;
    }
}

/// <summary>
/// The service provider the car entry points (CarPlay scene delegate, Android car-app service) resolve from.
/// </summary>
/// <remarks>
/// A head unit can connect before any phone UI exists — Android starts the <c>CarAppService</c> without an
/// activity, and CarPlay can launch the app straight into its car scene. So the car hosts cannot assume the
/// phone head already built the app; they fall back to <see cref="SwiftDotNetApp.GetOrCreate"/>, which builds
/// it once per process and hands the same container to whichever head asks next.
/// </remarks>
public static class CarHost
{
    static IServiceProvider? _services;

    /// <summary>An explicit provider (tests, custom hosts). Wins over the app's container when set.</summary>
    public static IServiceProvider? Services
    {
        get => _services ?? SwiftDotNetApp.Current?.Services;
        set => _services = value;
    }

    /// <summary>Resolves the provider, building the app through <paramref name="createApp"/> when no head has yet.</summary>
    public static IServiceProvider Resolve(Func<SwiftDotNetApp>? createApp)
    {
        if (Services is { } services)
            return services;
        if (createApp is null)
            throw new InvalidOperationException(
                "SwiftDotNet.Car has no app to connect to. Override CreateSwiftApp() on the car entry point, " +
                "or set CarHost.Services before a head unit connects.");
        return SwiftDotNetApp.GetOrCreate(createApp).Services;
    }
}
