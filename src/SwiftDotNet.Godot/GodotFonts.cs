using Godot;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>A Godot font resource at a concrete pixel size, as the engine's opaque font handle.</summary>
public sealed class GodotFont : Graphics.Font
{
    internal GodotFont(Godot.Font native, int pixelSize)
    {
        Native = native;
        PixelSize = pixelSize;

        // The engine's convention is a negative ascent (SkFontMetrics'), Godot's is positive.
        Metrics = new FontMetrics(-native.GetAscent(pixelSize), native.GetDescent(pixelSize), 0);
    }

    /// <summary>The Godot resource, for a custom renderer that wants to draw text itself.</summary>
    public Godot.Font Native { get; }

    /// <summary>Godot sizes fonts in whole pixels, so the engine's float size is rounded once, here.</summary>
    public int PixelSize { get; }

    public override float Size => PixelSize;
    public override FontMetrics Metrics { get; }
}

/// <summary>
/// Supplies and measures text with Godot's own font system — no SkiaSharp, no HarfBuzz binding of our own.
/// </summary>
/// <remarks>
/// <para>Godot already shapes text (it embeds HarfBuzz and ICU) and already resolves per-script fallback
/// through <c>Font.fallbacks</c>, so measurement and drawing agree by construction — which is the one
/// invariant <see cref="IFontProvider.Measure"/> demands.</para>
///
/// <para>Fonts default to the editor/runtime theme's fallback font, so a project that has set none still
/// renders. A game with its own typeface assigns <see cref="Regular"/> and <see cref="Bold"/>.</para>
/// </remarks>
public sealed class GodotFonts : IFontProvider
{
    readonly Dictionary<(int Size, bool Bold), GodotFont> _cache = new();
    Godot.Font? _regular;
    Godot.Font? _bold;

    /// <summary>The body typeface. Null uses the theme's fallback font.</summary>
    public Godot.Font? Regular
    {
        get => _regular;
        set { _regular = value; _cache.Clear(); }
    }

    /// <summary>The bold typeface. Null falls back to the default theme's bold font, then to <see cref="Regular"/>.</summary>
    public Godot.Font? Bold
    {
        get => _bold;
        set { _bold = value; _cache.Clear(); }
    }

    public Graphics.Font Get(float size, bool bold)
    {
        var pixels = Math.Max(1, (int)Math.Round(size));
        var key = (pixels, bold);
        if (_cache.TryGetValue(key, out var cached)) return cached;
        return _cache[key] = new GodotFont(Resolve(bold), pixels);
    }

    public float Measure(string text, Graphics.Font font)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var gf = (GodotFont)font;

        // width: -1 means "do not wrap or justify" — this is a single-line advance, which is exactly what
        // the paint pass will lay down.
        return gf.Native.GetStringSize(text, Godot.HorizontalAlignment.Left, -1, gf.PixelSize).X;
    }

    Godot.Font Resolve(bool bold)
    {
        if (bold)
        {
            if (_bold is not null) return _bold;

            // The stock theme carries a bold face for RichTextLabel; using it means bold text is actually
            // bold rather than faux-bolded or silently regular.
            var theme = ThemeDB.GetDefaultTheme();
            if (theme is not null && theme.HasFont("bold_font", "RichTextLabel"))
                return theme.GetFont("bold_font", "RichTextLabel");
        }

        return _regular ?? ThemeDB.GetFallbackFont();
    }
}

/// <summary>A decoded image, as a Godot texture the canvas can draw.</summary>
public sealed class GodotImage : IImage
{
    internal GodotImage(Texture2D texture)
    {
        Texture = texture;
        Width = texture.GetWidth();
        Height = texture.GetHeight();
    }

    /// <summary>The uploaded texture.</summary>
    public Texture2D Texture { get; }

    public int Width { get; }
    public int Height { get; }
}

/// <summary>
/// Decodes image bytes with Godot's own decoders and uploads them as textures.
/// </summary>
/// <remarks>
/// Format is sniffed from the magic bytes rather than trusted from a file extension, because the engine's
/// image nodes also take bytes straight off the network. SVG is included since Godot decodes it natively.
/// </remarks>
public sealed class GodotImages : IImageDecoder
{
    public IImage? Decode(byte[] bytes)
    {
        if (bytes.Length < 4) return null;

        var image = new Godot.Image();
        var error = Sniff(bytes) switch
        {
            "png" => image.LoadPngFromBuffer(bytes),
            "jpg" => image.LoadJpgFromBuffer(bytes),
            "webp" => image.LoadWebpFromBuffer(bytes),
            "bmp" => image.LoadBmpFromBuffer(bytes),
            "svg" => image.LoadSvgFromBuffer(bytes, 1f),
            _ => Error.FileUnrecognized,
        };

        return error == Error.Ok ? new GodotImage(ImageTexture.CreateFromImage(image)) : null;
    }

    public IImage? DecodeFile(string path)
    {
        // Godot.Image.LoadFromFile handles both a res:// path and an OS path, and returns null rather than
        // throwing on a missing file — which is the contract this seam wants.
        var image = Godot.Image.LoadFromFile(path);
        return image is null ? null : new GodotImage(ImageTexture.CreateFromImage(image));
    }

    static string Sniff(byte[] b) =>
        b[0] == 0x89 && b[1] == 'P' && b[2] == 'N' && b[3] == 'G' ? "png"
        : b[0] == 0xFF && b[1] == 0xD8 ? "jpg"
        : b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' ? "webp"
        : b[0] == 'B' && b[1] == 'M' ? "bmp"
        : b[0] == '<' ? "svg"
        : "";
}
