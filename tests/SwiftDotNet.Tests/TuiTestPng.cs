using System.Buffers.Binary;
using System.IO.Compression;

namespace SwiftDotNet.Tests;

/// <summary>A minimal PNG encoder, just enough to build decoder fixtures in-test.</summary>
internal static class TuiTestPng
{
    public static byte[] Encode(int width, int height, int colorType, int bitDepth, byte[] samples,
        int[]? filters = null, byte interlace = 0)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        var stride = width * channels * bitDepth / 8;

        // Apply the requested per-row filter to the raw samples (default: none).
        var raw = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            var filter = filters is null ? 0 : filters[y];
            raw[y * (stride + 1)] = (byte)filter;
            for (var x = 0; x < stride; x++)
            {
                var value = samples[y * stride + x];
                var left = x >= channels ? samples[y * stride + x - channels] : 0;
                var up = y > 0 ? samples[(y - 1) * stride + x] : 0;
                raw[y * (stride + 1) + 1 + x] = filter switch
                {
                    1 => (byte)(value - left),
                    2 => (byte)(value - up),
                    _ => value,
                };
            }
        }

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = (byte)bitDepth;
        ihdr[9] = (byte)colorType;
        ihdr[12] = interlace;

        using var deflated = new MemoryStream();
        using (var zlib = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(raw);

        var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR"u8, ihdr);
        WriteChunk(png, "IDAT"u8, deflated.ToArray());
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);

        var body = new byte[type.Length + data.Length];
        type.CopyTo(body);
        data.CopyTo(body, type.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        stream.Write(crc);
    }

    static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
