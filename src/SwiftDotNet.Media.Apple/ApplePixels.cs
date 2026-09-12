using CoreVideo;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Turns a <see cref="CVPixelBuffer"/> (what every Apple video path hands you) into the engine's
/// straight-RGBA <see cref="Pixels"/>.
/// </summary>
/// <remarks>
/// <para>Apple gives BGRA and the engine wants RGBA, so this is a byte swap, not a colour conversion —
/// and it is done here, once, rather than asking the rasterizer to grow a second pixel format.</para>
///
/// <para>Buffers are double-buffered and reused across frames: at 30fps a fresh <c>byte[]</c> per frame
/// is several hundred megabytes a minute of pure garbage, which shows up as periodic hitching long
/// before it shows up as memory.</para>
/// </remarks>
sealed class PixelBufferConverter
{
    byte[]?[] _buffers = new byte[]?[2];
    int _next;
    int _width, _height;

    /// <summary>
    /// Convert the buffer's current contents, returning a <see cref="Pixels"/> over a reused array, or
    /// null if the buffer is not a format this understands.
    /// </summary>
    public unsafe Pixels? Convert(CVPixelBuffer buffer)
    {
        var width = (int)buffer.Width;
        var height = (int)buffer.Height;
        if (width <= 0 || height <= 0) return null;

        if (width != _width || height != _height)
        {
            _width = width;
            _height = height;
            _buffers = new byte[]?[2];
        }

        var destination = _buffers[_next] ??= new byte[width * height * 4];
        _next ^= 1;

        if (buffer.Lock(CVPixelBufferLock.ReadOnly) != CVReturn.Success) return null;
        try
        {
            var source = (byte*)buffer.BaseAddress;
            if (source is null) return null;
            var stride = (int)buffer.BytesPerRow;

            fixed (byte* target = destination)
            {
                for (var y = 0; y < height; y++)
                {
                    var src = source + y * stride;
                    var dst = target + y * width * 4;
                    for (var x = 0; x < width; x++)
                    {
                        // BGRA → RGBA.
                        dst[0] = src[2];
                        dst[1] = src[1];
                        dst[2] = src[0];
                        dst[3] = src[3];
                        src += 4;
                        dst += 4;
                    }
                }
            }
        }
        finally
        {
            buffer.Unlock(CVPixelBufferLock.ReadOnly);
        }

        return new Pixels(width, height, destination);
    }
}
