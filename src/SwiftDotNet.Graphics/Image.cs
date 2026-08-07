namespace SwiftDotNet.Graphics;

/// <summary>
/// An opaque handle to a decoded image. Backends subclass or implement this to carry their native object
/// (an <c>SKImage</c>, a GPU texture, a Unity <c>Texture2D</c>); the engine only reads the dimensions,
/// which it needs to compute aspect-preserving fit/fill rects during layout.
/// </summary>
public interface IImage
{
    int Width { get; }
    int Height { get; }
}

/// <summary>
/// Decodes encoded image bytes into something the paired <see cref="ICanvas"/> can draw. Supplied by the
/// backend because the decode target is rasterizer-specific (a raster bitmap for Skia, an uploaded texture
/// for a GPU backend).
/// </summary>
public interface IImageDecoder
{
    /// <summary>Decodes PNG/JPEG/etc. bytes, returning null when the data is not a supported image.</summary>
    IImage? Decode(byte[] bytes);

    /// <summary>Decodes from a file path, returning null when missing or unsupported.</summary>
    IImage? DecodeFile(string path);
}
