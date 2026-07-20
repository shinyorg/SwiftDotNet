using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>Opt-in registration for the MapLibre GL renderer on Web, MAUI's <c>UseX()</c> shape.</summary>
public static class MapLibreBuilderExtensions
{
    /// <summary>
    /// Render the <see cref="Map"/> control as a MapLibre GL map. The host page must load MapLibre
    /// (see the sample's <c>index.html</c>).
    /// </summary>
    public static SwiftDotNetAppBuilder UseMapLibreMaps(this SwiftDotNetAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        MapsWeb.UseMapLibre();
        return builder;
    }
}
