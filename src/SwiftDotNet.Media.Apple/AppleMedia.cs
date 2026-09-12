using SwiftDotNet.Graphics;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// Registers the Apple frame producers with the engine's surface seam, so <c>VideoView</c>,
/// <c>CameraView</c> and (optionally) <c>WebView</c> show real content on a self-drawing backend —
/// Skia, WebGPU, MonoGame, Godot — running on iOS, Mac Catalyst, macOS or tvOS.
/// </summary>
/// <remarks>
/// <para>This is the counterpart to <c>AppleCamera.Register</c>: that one activates a <b>native</b>
/// renderer for the SwiftUI backend, where the OS composites a real preview layer. This one feeds
/// <em>pixels</em> to a backend that draws everything itself. An app can reference both — they serve
/// different backends and never collide.</para>
///
/// <para>Info.plist requirements are the app's: <c>NSCameraUsageDescription</c> for the camera, and
/// <c>NSAppTransportSecurity</c> as usual for plain-http media.</para>
/// </remarks>
public static class AppleMedia
{
    static bool _registered;

    /// <summary>
    /// Register video and camera producers. Idempotent; call once at startup, before the first render.
    /// </summary>
    /// <param name="web">
    /// Also serve <c>WebView</c> nodes from an off-screen <c>WKWebView</c>. Off by default: snapshotting
    /// WebKit is expensive and, in a MAUI host, floating a real web view over the canvas is better. Turn
    /// it on for hosts with no view hierarchy — Silk, MonoGame, Godot. See <see cref="AppleWebSource"/>.
    /// </param>
    public static void Register(bool web = false)
    {
        if (_registered) return;
        _registered = true;

#if !TVOS
        FrameSources.Register(SurfaceRenderer.CameraNode, _ => new AppleCameraSource());
#endif

        FrameSources.Register(SurfaceRenderer.VideoNode, request =>
        {
            var source = request.Props.TryGetValue("source", out var value) ? value as string ?? "" : "";
            return AppleVideoSource.CanOpen(source) ? new AppleVideoSource(source) : null;
        });

#if !TVOS
        if (web)
            FrameSources.Register(SurfaceRenderer.WebNode, request =>
            {
                var url = request.Props.TryGetValue("url", out var u) ? u as string : null;
                var html = request.Props.TryGetValue("html", out var h) ? h as string : null;
                return url is null && html is null ? null : new AppleWebSource(url, html);
            });
#endif
    }
}

/// <summary>Opt-in registration for the Apple frame producers, MAUI's <c>UseX()</c> shape.</summary>
public static class AppleMediaBuilderExtensions
{
    /// <summary>
    /// Feed <c>VideoView</c> / <c>CameraView</c> (and optionally <c>WebView</c>) from AVFoundation and
    /// WebKit on a self-drawing backend.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseAppleMedia();
    /// </code>
    /// </summary>
    public static SwiftDotNetAppBuilder UseAppleMedia(this SwiftDotNetAppBuilder builder, bool web = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AppleMedia.Register(web);
        return builder;
    }
}
