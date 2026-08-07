using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// The TUI backend's PNG support. The decoder itself is shared — see
/// <see cref="PngDecoder"/> in <c>SwiftDotNet.Graphics</c>, which every self-drawing backend uses so
/// there is one implementation of the format rather than one per rasterizer. This type is the TUI-shaped
/// wrapper around it.
///
/// <para>Coverage is unchanged: the non-interlaced baseline, bit depths 1/2/4/8/16, all five colour types,
/// honouring <c>tRNS</c>. Adam7 interlacing is rejected rather than half-decoded, and the caller falls
/// back to alt text.</para>
/// </summary>
public sealed class TuiPngDecoder : ITuiImageDecoder
{
    public bool TryDecode(ReadOnlySpan<byte> bytes, out TuiPixels? pixels)
    {
        if (!PngDecoder.TryDecode(bytes, out var decoded) || decoded is null)
        {
            pixels = null;
            return false;
        }

        pixels = new TuiPixels(decoded.Width, decoded.Height, decoded.Rgba);
        return true;
    }
}
