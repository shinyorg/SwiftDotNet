using System.Collections.Concurrent;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Process-wide async cache for remote (<c>Image.FromUrl</c>) images on the Skia backend.
///
/// Paint is synchronous and runs on the UI thread, so it can never block on a fetch. <see cref="Get"/> is
/// therefore non-blocking: the first call for a URL kicks off a background download and returns null (the
/// node paints nothing, exactly as it does for a not-yet-decoded local image); when the bytes land the
/// entry is filled and the bridge is invalidated, so the next frame draws the image.
///
/// A failed fetch is cached as a null entry so a broken URL is attempted once, not once per frame.
/// </summary>
public static class SkiaImageLoader
{
    static readonly ConcurrentDictionary<string, SKImage?> Cache = new();
    static readonly ConcurrentDictionary<string, bool> InFlight = new();

    /// <summary>
    /// The <see cref="HttpClient"/> used for remote images. Assign before the first image loads to supply
    /// your own handler, auth headers, or timeout.
    /// </summary>
    public static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// The decoded image for <paramref name="url"/>, or null while it is still loading (or if it failed).
    /// Starts the fetch on first call and invalidates <paramref name="bridge"/> when it completes.
    /// </summary>
    internal static SKImage? Get(string url, SkiaBridge bridge)
    {
        if (Cache.TryGetValue(url, out var cached)) return cached;
        if (url.Length == 0 || !InFlight.TryAdd(url, true)) return null;
        _ = Load(url, bridge);
        return null;
    }

    static async Task Load(string url, SkiaBridge bridge)
    {
        SKImage? image = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            image = SKImage.FromEncodedData(bytes);
        }
        catch { /* network/decode failure caches as null — the node keeps its placeholder */ }
        Cache[url] = image;
        InFlight.TryRemove(url, out _);
        bridge.RequestRepaint();
    }

    /// <summary>Drop every cached remote image (frees decoded bitmaps; next paint re-fetches).</summary>
    public static void Clear()
    {
        foreach (var key in Cache.Keys)
            if (Cache.TryRemove(key, out var image)) image?.Dispose();
    }
}
