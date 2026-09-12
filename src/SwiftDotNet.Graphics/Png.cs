using System.Buffers.Binary;
using System.IO.Compression;

namespace SwiftDotNet.Graphics;

/// <summary>
/// A decoded image: straight (non-premultiplied) RGBA8, row-major, no padding. Deliberately the simplest
/// possible shape — it is the handoff between "bytes were decoded" and "a backend uploads them", whether
/// that upload is a GPU texture, a terminal cell grid, or a raster bitmap.
/// </summary>
public sealed class Pixels(int width, int height, byte[] rgba)
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
/// A pure-managed PNG decoder — the reason a backend can render images without taking a dependency on a
/// native imaging library. Inflate comes from <see cref="ZLibStream"/> in the BCL; everything above it —
/// chunk walking, bit unpacking, the five scanline filters — is here. No reflection, so it stays
/// trim/AOT-safe alongside the rest of the shared layer.
///
/// <para>Covers the non-interlaced baseline: bit depths 1/2/4/8/16, and all five colour types
/// (grayscale, RGB, palette, grayscale+alpha, RGBA), honouring <c>tRNS</c> transparency. Adam7
/// interlacing is rejected rather than half-decoded — the caller then falls back to alt text or a
/// placeholder, which is honest, and progressive PNGs are vanishingly rare as app assets.</para>
/// </summary>
public static class PngDecoder
{
    static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>True when <paramref name="bytes"/> starts with the PNG signature.</summary>
    public static bool IsPng(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 && bytes[..8].SequenceEqual(Signature);

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out Pixels? pixels)
    {
        pixels = null;
        if (!IsPng(bytes)) return false;

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
#if NETSTANDARD2_1
            // ZLibStream is .NET 6+. A zlib stream is a 2-byte header, a raw deflate payload and a 4-byte
            // Adler-32 — so skipping the header and inflating with DeflateStream is equivalent here. The
            // checksum goes unverified, which matters less than it sounds: a corrupt payload fails the
            // expected-length check below and the image degrades to a placeholder either way.
            compressed.ReadByte();
            compressed.ReadByte();
            using var zlib = new DeflateStream(compressed, CompressionMode.Decompress);
#else
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress);
#endif
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

    static Pixels? ToRgba(byte[] raw, int width, int height, int bitDepth, int colorType,
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
        return new Pixels(width, height, rgba);
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

/// <summary>
/// A minimal PNG <em>encoder</em> — the counterpart to <see cref="PngDecoder"/>, for handing pixels back
/// out of the engine (a camera still, a screenshot of a surface) without a platform imaging library.
/// </summary>
/// <remarks>
/// Always writes 8-bit RGBA, non-interlaced, with no filtering. Filtering would compress better, but a
/// still capture is written once and read once; the simplicity is worth more than the bytes. Deflate
/// comes from <see cref="DeflateStream"/> with a hand-written zlib wrapper, because <c>ZLibStream</c>
/// does not exist on the netstandard2.1 (Unity) target this assembly also builds for.
/// </remarks>
public static class PngEncoder
{
    /// <summary>Encode straight RGBA pixels as a PNG.</summary>
    public static byte[] Encode(Pixels pixels)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A], 0, 8);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), pixels.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), pixels.Height);
        header[8] = 8;      // bit depth
        header[9] = 6;      // colour type: truecolour with alpha
        Chunk(output, "IHDR", header);

        Chunk(output, "IDAT", Compress(pixels));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    static byte[] Compress(Pixels pixels)
    {
        var stride = pixels.Width * 4;
        using var raw = new MemoryStream((stride + 1) * pixels.Height);
        for (var y = 0; y < pixels.Height; y++)
        {
            raw.WriteByte(0);                                   // filter type 0 (none)
            raw.Write(pixels.Rgba, y * stride, stride);
        }
        var scanlines = raw.ToArray();

        using var zlib = new MemoryStream();
        zlib.WriteByte(0x78);                                   // deflate, 32K window
        zlib.WriteByte(0x01);                                   // no preset dictionary, fastest
        using (var deflate = new DeflateStream(zlib, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(scanlines, 0, scanlines.Length);

        var adler = Adler32(scanlines);
        zlib.WriteByte((byte)(adler >> 24));
        zlib.WriteByte((byte)(adler >> 16));
        zlib.WriteByte((byte)(adler >> 8));
        zlib.WriteByte((byte)adler);
        return zlib.ToArray();
    }

    static void Chunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length, 0, 4);

        var body = new byte[4 + data.Length];
        for (var i = 0; i < 4; i++) body[i] = (byte)type[i];
        Buffer.BlockCopy(data, 0, body, 4, data.Length);
        output.Write(body, 0, body.Length);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        output.Write(crc, 0, 4);
    }

    static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        return (b << 16) | a;
    }

    static readonly uint[] CrcTable = BuildCrcTable();

    static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    static uint Crc32(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var value in data) c = CrcTable[(c ^ value) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
