using System.Collections.Concurrent;

namespace SwiftDotNet.Graphics;

/// <summary>
/// Process-wide async cache for remote (<c>Image.FromUrl</c>) images on self-drawing backends.
/// </summary>
/// <remarks>
/// Paint is synchronous and runs on the UI thread, so it can never block on a fetch. <see cref="Get"/> is
/// therefore non-blocking: the first call for a URL kicks off a background download and returns null (the
/// node paints nothing, exactly as it does for a not-yet-decoded local image); when the bytes land the
/// entry is filled and the caller's invalidate hook fires, so the next frame draws the image.
///
/// <para>A failed fetch is cached as a null entry so a broken URL is attempted once, not once per frame.</para>
///
/// <para>The cache is keyed by URL alone, so a decoder swap mid-process would serve images decoded by the
/// previous one. That is fine in practice — a process has one rasterizer — but it is why
/// <see cref="Clear"/> exists.</para>
/// </remarks>
public static class ImageLoader
{
    static readonly ConcurrentDictionary<string, IImage?> Cache = new();
    static readonly ConcurrentDictionary<string, bool> InFlight = new();

    /// <summary>
    /// The <see cref="HttpClient"/> used for remote images. Assign before the first image loads to supply
    /// your own handler, auth headers, or timeout.
    /// </summary>
    public static HttpClient Http { get; set; } = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// The decoded image for <paramref name="url"/>, or null while it is still loading (or if it failed).
    /// Starts the fetch on first call and invokes <paramref name="onLoaded"/> when it completes.
    /// </summary>
    public static IImage? Get(string url, IImageDecoder decoder, Action onLoaded)
    {
        if (Cache.TryGetValue(url, out var cached)) return cached;
        if (url.Length == 0 || !InFlight.TryAdd(url, true)) return null;
        _ = Load(url, decoder, onLoaded);
        return null;
    }

    static async Task Load(string url, IImageDecoder decoder, Action onLoaded)
    {
        IImage? image = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            image = decoder.Decode(bytes);
        }
        catch { /* network/decode failure caches as null — the node keeps its placeholder */ }
        Cache[url] = image;
        InFlight.TryRemove(url, out _);
        onLoaded();
    }

    /// <summary>Drop every cached remote image (frees decoded bitmaps; next paint re-fetches).</summary>
    public static void Clear()
    {
        foreach (var key in Cache.Keys)
            if (Cache.TryRemove(key, out var image) && image is IDisposable d) d.Dispose();
    }
}
