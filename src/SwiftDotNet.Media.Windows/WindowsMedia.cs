using SwiftDotNet.Graphics;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// Registers the Windows frame producer with the engine's surface seam, so <c>CameraView</c> shows a
/// real preview on a self-drawing backend running on Windows.
/// </summary>
/// <remarks>
/// <para><b>Camera only.</b> For <c>VideoView</c> on Windows, register <c>VlcMedia</c> as well —
/// <c>MediaPlayer</c>'s frame-server mode hands frames to a Direct3D surface rather than to memory, so a
/// CPU path through it needs Win2D or hand-written D3D interop, while LibVLC already renders into
/// memory. For <c>WebView</c>, WebView2 has no public off-screen mode; inside a MAUI host, float a real
/// web view over the canvas through <c>IPlatformViewHost</c> instead.</para>
///
/// <para><b>Status: never compiled</b> — this needs a Windows machine. See
/// <c>docs/video-and-camera.md</c>.</para>
/// </remarks>
public static class WindowsMedia
{
    static bool _registered;

    /// <summary>Register the camera producer. Idempotent; call once at startup, before the first render.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        FrameSources.Register(SurfaceRenderer.CameraNode, _ => new WindowsCameraSource());
    }
}

/// <summary>Opt-in registration for the Windows frame producer, MAUI's <c>UseX()</c> shape.</summary>
public static class WindowsMediaBuilderExtensions
{
    /// <summary>
    /// Feed <c>CameraView</c> from <c>Windows.Media.Capture</c> on a self-drawing backend. Pair it with
    /// <c>UseVlcMedia()</c> for <c>VideoView</c>.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseWindowsMedia()
    ///        .UseVlcMedia();
    /// </code>
    /// </summary>
    public static SwiftDotNetAppBuilder UseWindowsMedia(this SwiftDotNetAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        WindowsMedia.Register();
        return builder;
    }
}
