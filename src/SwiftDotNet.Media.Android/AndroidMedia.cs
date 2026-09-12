using SwiftDotNet.Graphics;
using SwiftDotNet.Hosting;

namespace SwiftDotNet;

/// <summary>
/// Registers the Android frame producers with the engine's surface seam, so <c>VideoView</c>,
/// <c>CameraView</c> and (optionally) <c>WebView</c> show real content on a self-drawing backend — Skia,
/// WebGPU, MonoGame, Godot — running on Android.
/// </summary>
/// <remarks>
/// Manifest requirements are the app's: <c>android.permission.CAMERA</c> (requested at runtime) for the
/// camera and <c>android.permission.INTERNET</c> for remote media or pages.
/// </remarks>
public static class AndroidMedia
{
    static bool _registered;

    /// <summary>
    /// Register video and camera producers. Idempotent; call once at startup, before the first render.
    /// </summary>
    /// <param name="web">
    /// Also serve <c>WebView</c> nodes from an off-screen <c>android.webkit.WebView</c>. Off by default
    /// so nothing changes for an app that does not ask for it — but unlike the Apple equivalent this one
    /// is cheap and takes real input, so turn it on freely. See <see cref="AndroidWebSource"/>.
    /// </param>
    public static void Register(bool web = false)
    {
        if (_registered) return;
        _registered = true;

        FrameSources.Register(SurfaceRenderer.CameraNode, _ => new AndroidCameraSource());

        FrameSources.Register(SurfaceRenderer.VideoNode, request =>
        {
            var source = request.Props.TryGetValue("source", out var value) ? value as string ?? "" : "";
            return source.Length == 0 ? null : new AndroidVideoSource(source);
        });

        if (web)
            FrameSources.Register(SurfaceRenderer.WebNode, request =>
            {
                var url = request.Props.TryGetValue("url", out var u) ? u as string : null;
                var html = request.Props.TryGetValue("html", out var h) ? h as string : null;
                return url is null && html is null ? null : new AndroidWebSource(url, html);
            });
    }
}

/// <summary>Opt-in registration for the Android frame producers, MAUI's <c>UseX()</c> shape.</summary>
public static class AndroidMediaBuilderExtensions
{
    /// <summary>
    /// Feed <c>VideoView</c> / <c>CameraView</c> (and optionally <c>WebView</c>) from Camera2, MediaCodec
    /// and WebKit on a self-drawing backend.
    /// <code>
    /// builder.UseSwiftApp&lt;ContentView&gt;()
    ///        .UseAndroidMedia(web: true);
    /// </code>
    /// </summary>
    public static SwiftDotNetAppBuilder UseAndroidMedia(this SwiftDotNetAppBuilder builder, bool web = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        AndroidMedia.Register(web);
        return builder;
    }
}
