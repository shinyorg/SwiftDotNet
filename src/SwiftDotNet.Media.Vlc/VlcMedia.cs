using SwiftDotNet.Graphics;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// Registers LibVLC as the frame producer for <c>VideoView</c> and, optionally, <c>CameraView</c> —
/// the desktop answer for hosts with no platform media stack (Silk.NET, WPF, WinForms, the headless
/// harness, Linux/framebuffer).
/// </summary>
/// <remarks>
/// The app must ship the native library: add <c>VideoLAN.LibVLC.Windows</c> / <c>VideoLAN.LibVLC.Mac</c>
/// to the head project, or install <c>libvlc</c> on Linux. See <see cref="VlcFrameSource"/>.
/// </remarks>
public static class VlcMedia
{
    static bool _registered;

    /// <summary>
    /// Register the LibVLC producer. Idempotent; call once at startup, before the first render.
    /// </summary>
    /// <param name="camera">
    /// Also serve <c>CameraView</c> from the OS capture device (<c>dshow://</c>, <c>avcapture://</c>,
    /// <c>v4l2://</c>). Off by default — on a desktop a camera is a privacy-sensitive device and a
    /// dependency on VLC's capture modules, so it should be a deliberate choice.
    /// </param>
    /// <param name="cameraMrl">Override the capture locator (a specific device, an RTSP camera).</param>
    /// <param name="options">Extra per-media VLC options, e.g. <c>":network-caching=300"</c>.</param>
    public static void Register(bool camera = false, string? cameraMrl = null, params string[] options)
    {
        if (_registered) return;
        _registered = true;

        FrameSources.Register(SurfaceRenderer.VideoNode, request =>
        {
            var source = request.Props.TryGetValue("source", out var value) ? value as string ?? "" : "";
            return source.Length == 0 ? null : new VlcFrameSource(source, options);
        });

        if (camera)
            FrameSources.Register(SurfaceRenderer.CameraNode,
                _ => new VlcFrameSource(cameraMrl ?? VlcFrameSource.DefaultCameraMrl, options));
    }
}

/// <summary>Opt-in registration for the LibVLC producer, MAUI's <c>UseX()</c> shape.</summary>
public static class VlcMediaBuilderExtensions
{
    /// <summary>
    /// Play <c>VideoView</c> (and optionally <c>CameraView</c>) through LibVLC on a self-drawing backend.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseVlcMedia(camera: true);
    /// </code>
    /// </summary>
    public static SwiftDotNetAppBuilder UseVlcMedia(this SwiftDotNetAppBuilder builder, bool camera = false,
        string? cameraMrl = null, params string[] options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        VlcMedia.Register(camera, cameraMrl, options);
        return builder;
    }
}
