using Android.Media;
using SwiftDotNet.Graphics;

// `Image` in this namespace is the DSL's own view type, so the Android one is spelled out.
using AndroidImage = Android.Media.Image;

namespace SwiftDotNet;

/// <summary>
/// Converts an Android <see cref="AndroidImage"/> in <c>YUV_420_888</c> — what both Camera2 and MediaCodec hand
/// back on the CPU path — into the engine's straight RGBA <see cref="Pixels"/>.
/// </summary>
/// <remarks>
/// <para>There is no way around doing this in software on the CPU path: <c>YUV_420_888</c> is a
/// <em>family</em> of layouts (planar I420, semi-planar NV21/NV12, padded rows, interleaved chroma) and
/// the only portable reader is one that respects each plane's row and pixel stride. Devices differ, and
/// code that assumes one layout works on the emulator and tears on hardware.</para>
///
/// <para>Buffers are double-buffered and reused: at 30fps a new array per frame is tens of megabytes a
/// second of garbage on a device that can least afford it.</para>
/// </remarks>
sealed class YuvConverter
{
    byte[]?[] _buffers = new byte[]?[2];
    byte[]? _y, _u, _v;
    int _next;
    int _width, _height;

    public Pixels? Convert(AndroidImage image)
    {
        var width = image.Width;
        var height = image.Height;
        if (width <= 0 || height <= 0) return null;

        var planes = image.GetPlanes();
        if (planes is null || planes.Length < 3) return null;

        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _buffers = new byte[]?[2];
            _y = _u = _v = null;
        }

        var rgba = _buffers[_next] ??= new byte[width * height * 4];
        _next ^= 1;

        var yPlane = Read(planes[0], ref _y);
        var uPlane = Read(planes[1], ref _u);
        var vPlane = Read(planes[2], ref _v);
        if (yPlane is null || uPlane is null || vPlane is null) return null;

        int yStride = planes[0].RowStride;
        int uStride = planes[1].RowStride, uPixel = planes[1].PixelStride;
        int vStride = planes[2].RowStride, vPixel = planes[2].PixelStride;

        for (var row = 0; row < height; row++)
        {
            var yRow = row * yStride;
            var chromaRow = row / 2;
            var uRow = chromaRow * uStride;
            var vRow = chromaRow * vStride;
            var outRow = row * width * 4;

            for (var col = 0; col < width; col++)
            {
                var yIndex = yRow + col;
                var chromaCol = col / 2;
                var uIndex = uRow + chromaCol * uPixel;
                var vIndex = vRow + chromaCol * vPixel;
                if (yIndex >= yPlane.Length || uIndex >= uPlane.Length || vIndex >= vPlane.Length) continue;

                int y = yPlane[yIndex] & 0xFF;
                int u = (uPlane[uIndex] & 0xFF) - 128;
                int v = (vPlane[vIndex] & 0xFF) - 128;

                // BT.601 full-range, in fixed point — the conversion every Android CPU path uses.
                var r = y + ((91881 * v) >> 16);
                var g = y - ((22554 * u + 46802 * v) >> 16);
                var b = y + ((116130 * u) >> 16);

                var o = outRow + col * 4;
                rgba[o] = Clamp(r);
                rgba[o + 1] = Clamp(g);
                rgba[o + 2] = Clamp(b);
                rgba[o + 3] = 0xFF;
            }
        }

        return new Pixels(width, height, rgba);
    }

    static byte[]? Read(AndroidImage.Plane plane, ref byte[]? cache)
    {
        var buffer = plane.Buffer;
        if (buffer is null) return null;
        buffer.Rewind();
        var length = buffer.Remaining();
        if (length <= 0) return null;
        if (cache is null || cache.Length < length) cache = new byte[length];
        buffer.Get(cache, 0, length);
        return cache;
    }

    static byte Clamp(int value) => value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
}
