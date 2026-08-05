namespace SwiftDotNet;

/// <summary>
/// A decoded image: straight (non-premultiplied) RGBA8, row-major, no padding. Deliberately the
/// simplest possible shape — it is the only contract between whatever decoded the bytes and
/// <see cref="TuiAsciiArt"/>, so a richer decoder (the optional <c>SwiftDotNet.Tui.Graphics</c> package)
/// can be swapped in without the art renderer knowing.
/// </summary>
public sealed class TuiPixels(int width, int height, byte[] rgba)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>RGBA bytes, <c>Width * Height * 4</c> of them.</summary>
    public byte[] Rgba { get; } = rgba;

    /// <summary>The pixel at (<paramref name="x"/>, <paramref name="y"/>), clamped to the edges.</summary>
    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        var i = (y * Width + x) * 4;
        return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }
}

/// <summary>
/// Turns encoded image bytes into <see cref="TuiPixels"/>. The core TUI backend ships only
/// <see cref="TuiPngDecoder"/> so it stays dependency-free; registering a richer decoder is how JPEG /
/// WebP / GIF support is added (see <c>SwiftDotNet.Tui.Graphics</c>).
/// </summary>
public interface ITuiImageDecoder
{
    bool TryDecode(ReadOnlySpan<byte> bytes, out TuiPixels? pixels);
}

/// <summary>
/// The decoder chain images are run through, tried in registration order, newest first. Register a
/// decoder before the first render:
/// <code>
/// TuiImageDecoders.Register(new MyJpegDecoder());
/// </code>
/// </summary>
public static class TuiImageDecoders
{
    static readonly List<ITuiImageDecoder> Decoders = [new TuiPngDecoder()];

    /// <summary>Adds a decoder, ahead of every decoder registered before it.</summary>
    public static void Register(ITuiImageDecoder decoder) => Decoders.Insert(0, decoder);

    /// <summary>
    /// Decodes with the first decoder that recognises the payload; null when none does — which the image
    /// node treats as "show the alt text", never as an error.
    /// </summary>
    public static TuiPixels? Decode(ReadOnlySpan<byte> bytes)
    {
        foreach (var decoder in Decoders)
        {
            try
            {
                if (decoder.TryDecode(bytes, out var pixels) && pixels is not null) return pixels;
            }
            catch
            {
                // A malformed payload must not take the app down — try the next decoder, then give up.
            }
        }
        return null;
    }
}
