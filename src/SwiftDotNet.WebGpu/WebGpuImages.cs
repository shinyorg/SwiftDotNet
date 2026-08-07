using Silk.NET.WebGPU;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// A decoded image awaiting (or holding) its GPU texture.
/// </summary>
/// <remarks>
/// Decoding and uploading are deliberately split: decode happens wherever the engine asks for the image,
/// which may be a background thread finishing a download, while the upload must happen on the thread that
/// owns the device. The renderer uploads on first use and caches the texture here.
/// </remarks>
public sealed unsafe class WebGpuImage : IImage, IDisposable
{
    internal WebGpuImage(Pixels pixels)
    {
        Source = pixels;
        Width = pixels.Width;
        Height = pixels.Height;
    }

    internal Pixels Source { get; }

    /// <summary>The uploaded texture view, or null until the renderer has seen this image.</summary>
    internal TextureView* View { get; set; }

    internal Texture* Texture { get; set; }

    public int Width { get; }
    public int Height { get; }

    public void Dispose()
    {
        View = null;
        Texture = null;
    }
}

/// <summary>
/// The WebGPU <see cref="IImageDecoder"/>. Uses the shared pure-managed <see cref="PngDecoder"/>, so the
/// backend takes no imaging dependency.
/// </summary>
/// <remarks>
/// PNG only. That is the honest limit of what the shared decoder covers — a JPEG or WebP asset returns
/// null and the node paints nothing, exactly as it does for a failed download. Register a richer decoder
/// by assigning <see cref="Fallback"/>.
/// </remarks>
public sealed class WebGpuImages : IImageDecoder
{
    /// <summary>An optional decoder tried when the payload is not a PNG.</summary>
    public static Func<byte[], Pixels?>? Fallback { get; set; }

    public IImage? Decode(byte[] bytes)
    {
        if (PngDecoder.TryDecode(bytes, out var pixels) && pixels is not null) return new WebGpuImage(pixels);
        var viaFallback = Fallback?.Invoke(bytes);
        return viaFallback is null ? null : new WebGpuImage(viaFallback);
    }

    public IImage? DecodeFile(string path)
    {
        try
        {
            return File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;
        }
        catch
        {
            return null;   // unreadable asset behaves like a missing one
        }
    }
}
