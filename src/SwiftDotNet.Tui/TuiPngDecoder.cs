using System.Buffers.Binary;
using System.IO.Compression;

namespace SwiftDotNet;

/// <summary>
/// A pure-managed PNG decoder — the reason the core terminal backend can render images without taking a
/// dependency on SkiaSharp (which is what <c>XenoAtom.Terminal.UI.Graphics</c> would drag in for all
/// three desktop RIDs). Inflate comes from <see cref="ZLibStream"/> in the BCL; everything above it —
/// chunk walking, bit unpacking, the five scanline filters — is here. No reflection, so it stays
/// trim/AOT-safe alongside the rest of <c>Core</c>.
///
/// <para>Covers the non-interlaced baseline: bit depths 1/2/4/8/16, and all five colour types
/// (grayscale, RGB, palette, grayscale+alpha, RGBA), honouring <c>tRNS</c> transparency. Adam7
/// interlacing is rejected rather than half-decoded — the caller then falls back to alt text, which is
/// honest, and progressive PNGs are vanishingly rare as app assets.</para>
/// </summary>
public sealed class TuiPngDecoder : ITuiImageDecoder
{
    static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public bool TryDecode(ReadOnlySpan<byte> bytes, out TuiPixels? pixels)
    {
        pixels = null;
        if (bytes.Length < 8 || !bytes[..8].SequenceEqual(Signature)) return false;

        int width = 0, height = 0, bitDepth = 0, colorType = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        var idat = new MemoryStream();

        var pos = 8;
        while (pos + 8 <= bytes.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes[pos..]);
            if (length < 0 || pos + 12 + length > bytes.Length) return false;
            var type = bytes.Slice(pos + 4, 4);
            var data = bytes.Slice(pos + 8, length);

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length < 13) return false;
                width = (int)BinaryPrimitives.ReadUInt32BigEndian(data);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..]);
                bitDepth = data[8];
                colorType = data[9];
                if (data[12] != 0) return false;      // Adam7 interlace — see the type remarks.
            }
            else if (type.SequenceEqual("PLTE"u8)) palette = data.ToArray();
            else if (type.SequenceEqual("tRNS"u8)) transparency = data.ToArray();
            else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
            else if (type.SequenceEqual("IEND"u8)) break;

            pos += 12 + length;
        }

        if (width <= 0 || height <= 0 || idat.Length == 0) return false;
        var channels = Channels(colorType);
        if (channels == 0) return false;

        idat.Position = 0;
        var raw = Inflate(idat, RawLength(width, height, bitDepth, channels));
        if (raw is null) return false;

        Unfilter(raw, width, height, bitDepth, channels);
        pixels = ToRgba(raw, width, height, bitDepth, colorType, channels, palette, transparency);
        return pixels is not null;
    }

    static int Channels(int colorType) => colorType switch
    {
        0 => 1,   // grayscale
        2 => 3,   // RGB
        3 => 1,   // palette index
        4 => 2,   // grayscale + alpha
        6 => 4,   // RGBA
        _ => 0,
    };

    /// <summary>Bytes in the inflated stream: every scanline is one filter byte plus its packed pixels.</summary>
    static int RawLength(int width, int height, int bitDepth, int channels)
        => height * (1 + (width * channels * bitDepth + 7) / 8);

    static byte[]? Inflate(Stream compressed, int expected)
    {
        try
        {
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
            var buffer = new byte[expected];
            var read = 0;
            while (read < expected)
            {
                var n = zlib.Read(buffer, read, expected - read);
                if (n == 0) break;
                read += n;
            }
            return read == expected ? buffer : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reverses the five PNG scanline filters in place, leaving each row's pixel bytes where its filter
    /// byte used to lead them. Filters are defined over the <em>byte</em> distance of one pixel, which for
    /// sub-byte depths is 1 — hence the max.
    /// </summary>
    static void Unfilter(byte[] raw, int width, int height, int bitDepth, int channels)
    {
        var bytesPerPixel = Math.Max(1, channels * bitDepth / 8);
        var stride = (width * channels * bitDepth + 7) / 8;

        for (var y = 0; y < height; y++)
        {
            var rowStart = y * (stride + 1);
            var filter = raw[rowStart];
            var row = rowStart + 1;
            var prior = row - (stride + 1);

            for (var x = 0; x < stride; x++)
            {
                var a = x >= bytesPerPixel ? raw[row + x - bytesPerPixel] : 0;   // left
                var b = y > 0 ? raw[prior + x] : 0;                              // up
                var c = y > 0 && x >= bytesPerPixel ? raw[prior + x - bytesPerPixel] : 0;   // up-left
                raw[row + x] = filter switch
                {
                    0 => raw[row + x],
                    1 => (byte)(raw[row + x] + a),
                    2 => (byte)(raw[row + x] + b),
                    3 => (byte)(raw[row + x] + (a + b) / 2),
                    4 => (byte)(raw[row + x] + Paeth(a, b, c)),
                    _ => raw[row + x],
                };
            }
        }
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static TuiPixels? ToRgba(byte[] raw, int width, int height, int bitDepth, int colorType,
        int channels, byte[]? palette, byte[]? transparency)
    {
        if (colorType == 3 && palette is null) return null;

        var stride = (width * channels * bitDepth + 7) / 8;
        var rgba = new byte[width * height * 4];
        var max = (1 << bitDepth) - 1;

        for (var y = 0; y < height; y++)
        {
            var row = y * (stride + 1) + 1;
            for (var x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                switch (colorType)
                {
                    case 0:
                    {
                        var v = Sample(raw, row, x, 0, bitDepth, channels);
                        var g = Scale(v, max);
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = g;
                        // tRNS for grayscale carries the single fully-transparent sample value.
                        rgba[o + 3] = transparency is { Length: >= 2 } &&
                                      v == BinaryPrimitives.ReadUInt16BigEndian(transparency) ? (byte)0 : (byte)255;
                        break;
                    }
                    case 2:
                    {
                        rgba[o] = Scale(Sample(raw, row, x, 0, bitDepth, channels), max);
                        rgba[o + 1] = Scale(Sample(raw, row, x, 1, bitDepth, channels), max);
                        rgba[o + 2] = Scale(Sample(raw, row, x, 2, bitDepth, channels), max);
                        rgba[o + 3] = 255;
                        break;
                    }
                    case 3:
                    {
                        var index = Sample(raw, row, x, 0, bitDepth, channels);
                        var p = index * 3;
                        if (p + 2 >= palette!.Length) return null;
                        rgba[o] = palette[p];
                        rgba[o + 1] = palette[p + 1];
                        rgba[o + 2] = palette[p + 2];
                        rgba[o + 3] = transparency is not null && index < transparency.Length
                            ? transparency[index]
                            : (byte)255;
                        break;
                    }
                    case 4:
                    {
                        var g = Scale(Sample(raw, row, x, 0, bitDepth, channels), max);
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = g;
                        rgba[o + 3] = Scale(Sample(raw, row, x, 1, bitDepth, channels), max);
                        break;
                    }
                    case 6:
                    {
                        rgba[o] = Scale(Sample(raw, row, x, 0, bitDepth, channels), max);
                        rgba[o + 1] = Scale(Sample(raw, row, x, 1, bitDepth, channels), max);
                        rgba[o + 2] = Scale(Sample(raw, row, x, 2, bitDepth, channels), max);
                        rgba[o + 3] = Scale(Sample(raw, row, x, 3, bitDepth, channels), max);
                        break;
                    }
                }
            }
        }
        return new TuiPixels(width, height, rgba);
    }

    /// <summary>Reads one channel sample, unpacking sub-byte depths and narrowing 16-bit to its high byte.</summary>
    static int Sample(byte[] raw, int row, int x, int channel, int bitDepth, int channels)
    {
        switch (bitDepth)
        {
            case 8: return raw[row + x * channels + channel];
            case 16: return (raw[row + (x * channels + channel) * 2] << 8) | raw[row + (x * channels + channel) * 2 + 1];
            default:
            {
                var bitIndex = (x * channels + channel) * bitDepth;
                var b = raw[row + bitIndex / 8];
                var shift = 8 - bitDepth - bitIndex % 8;
                return (b >> shift) & ((1 << bitDepth) - 1);
            }
        }
    }

    /// <summary>Normalises a sample of any depth to 0-255.</summary>
    static byte Scale(int value, int max) => max == 255 ? (byte)value
        : max == 65535 ? (byte)(value >> 8)
        : (byte)(value * 255 / max);
}
