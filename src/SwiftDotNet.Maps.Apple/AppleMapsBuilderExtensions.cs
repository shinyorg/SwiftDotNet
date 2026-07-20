using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>Opt-in registration for the native MapKit renderer, MAUI's <c>UseX()</c> shape.</summary>
public static class AppleMapsBuilderExtensions
{
    /// <summary>
    /// Activate the native MapKit renderer for the <see cref="Map"/> control, so <c>Map</c> nodes render
    /// as a real map instead of the "unknown view" placeholder.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseAppleMaps();
    /// </code>
    /// </summary>
    /// <remarks>
    /// Registration is a native, process-wide side effect (it calls into the embedded Swift framework),
    /// not a container registration — the builder is simply the one place an app declares what it uses.
    /// </remarks>
    public static SwiftDotNetAppBuilder UseAppleMaps(this SwiftDotNetAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AppleMaps.Register();
        return builder;
    }
}
