using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>Opt-in registration for the native AVFoundation camera renderer, MAUI's <c>UseX()</c> shape.</summary>
public static class AppleCameraBuilderExtensions
{
    /// <summary>
    /// Activate the native AVFoundation + Vision renderer for <c>CameraView</c>.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseAppleCamera();
    /// </code>
    /// </summary>
    /// <remarks>
    /// Like <c>UseAppleMaps</c>, this is a native process-wide registration rather than a container one.
    /// </remarks>
    public static SwiftDotNetAppBuilder UseAppleCamera(this SwiftDotNetAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AppleCamera.Register();
        return builder;
    }
}
