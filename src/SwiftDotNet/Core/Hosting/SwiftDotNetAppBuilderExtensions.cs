using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace SwiftDotNet.Hosting;

/// <summary>
/// The <c>UseX()</c> seam. SwiftDotNet's optional libraries (Controls, Maps, Camera, Skia) each ship
/// their own extension on <see cref="SwiftDotNetAppBuilder"/> that registers their renderers, so a
/// consumer opts in with one line — the same shape as <c>UseMauiCommunityToolkit()</c>.
/// </summary>
public static class SwiftDotNetAppBuilderExtensions
{
    /// <summary>
    /// Register <typeparamref name="TRoot"/> as the app's root view, resolved from the container so it
    /// gets constructor injection.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="ActivatorUtilities"/>, which is AOT-workable but can surface trim warnings for
    /// constructor discovery. For a fully trim-clean registration, use the
    /// <see cref="UseSwiftApp{TRoot}(SwiftDotNetAppBuilder, Func{IServiceProvider, TRoot})"/> overload
    /// and construct the root explicitly.
    /// </remarks>
    [RequiresUnreferencedCode("The root view's constructor is resolved at runtime. Use the factory overload to stay trim-safe.")]
    public static SwiftDotNetAppBuilder UseSwiftApp<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRoot>(
        this SwiftDotNetAppBuilder builder) where TRoot : View
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSwiftRoot<TRoot>();
        builder.SetRootFactory(static sp => sp.GetRequiredService<TRoot>());
        return builder;
    }

    /// <summary>
    /// Register the app's root view with an explicit factory — the trim-clean form, since no
    /// constructor is discovered at runtime.
    /// </summary>
    public static SwiftDotNetAppBuilder UseSwiftApp<TRoot>(
        this SwiftDotNetAppBuilder builder, Func<IServiceProvider, TRoot> factory) where TRoot : View
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(factory);
        builder.Services.AddSingleton(factory);
        builder.SetRootFactory(sp => factory(sp));
        return builder;
    }

    [RequiresUnreferencedCode("The root view's constructor is resolved at runtime.")]
    static void TryAddSwiftRoot<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRoot>(
        this IServiceCollection services) where TRoot : View
    {
        // The root is retained for the app's lifetime, so singleton is the only correct lifetime.
        // Registered only if the app hasn't already registered it with its own factory.
        for (var i = 0; i < services.Count; i++)
            if (services[i].ServiceType == typeof(TRoot))
                return;

        services.AddSingleton<TRoot>();
    }
}
