using System.Text;
using SkiaSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>A Skia-backed font at a concrete size, as handed to the engine.</summary>
public sealed class SkiaFont : Graphics.Font
{
    internal SkiaFont(SKFont native)
    {
        Native = native;
        var m = native.Metrics;
        Metrics = new FontMetrics(m.Ascent, m.Descent, m.Leading);
    }

    /// <summary>The underlying Skia font, for a custom renderer that wants full Skia access.</summary>
    public SKFont Native { get; }

    public override float Size => Native.Size;
    public override FontMetrics Metrics { get; }
}

/// <summary>
/// The Skia <see cref="IFontProvider"/>: caches faces, and resolves per-rune fallback so emoji and
/// non-Latin scripts render through a matched typeface rather than as tofu boxes.
/// </summary>
/// <remarks>
/// A single base typeface cannot cover 👋 / 🍎 / 你好, so <see cref="Runs"/> splits a string into runs by
/// the face that can draw each rune (matched through <see cref="SKFontManager"/>). Both
/// <see cref="Measure"/> and <see cref="SkiaCanvas.DrawText"/> walk those runs, which is what keeps
/// measured and painted widths in agreement — the invariant <see cref="IFontProvider"/> requires.
/// </remarks>
public sealed class SkiaFonts : IFontProvider
{
    static readonly SKFontManager FontManager = SKFontManager.Default;
    static readonly SKTypeface Regular = SKTypeface.FromFamilyName(null, SKFontStyle.Normal) ?? SKTypeface.Default;
    static readonly SKTypeface Bold = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;

    readonly Dictionary<(float, bool), SkiaFont> _cache = new();
    readonly Dictionary<string, SKTypeface> _fallback = new();

    public Graphics.Font Get(float size, bool bold)
    {
        if (_cache.TryGetValue((size, bold), out var cached)) return cached;
        var font = new SkiaFont(new SKFont(bold ? Bold : Regular, size));
        _cache[(size, bold)] = font;
        return font;
    }

    public float Measure(string text, Graphics.Font font)
    {
        if (string.IsNullOrEmpty(text) || font is not SkiaFont sf) return 0;

        var x = 0f;
        foreach (var (run, face) in Runs(text, sf))
        {
            using var runFont = new SKFont(face, sf.Size);
            x += runFont.MeasureText(run);
        }
        return x;
    }

    /// <summary>Splits a string into (run, typeface) pairs, one per contiguous span sharing a face.</summary>
    internal IEnumerable<(string run, SKTypeface face)> Runs(string text, SkiaFont font)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var baseFace = font.Native.Typeface ?? SKTypeface.Default;
        var sb = new StringBuilder();
        SKTypeface? current = null;

        foreach (var rune in text.EnumerateRunes())
        {
            var face = Resolve(font.Native, baseFace, rune.Value);
            if (current is null) current = face;
            else if (!ReferenceEquals(face, current)) { yield return (sb.ToString(), current); sb.Clear(); current = face; }
            sb.Append(rune.ToString());
        }

        if (sb.Length > 0 && current is not null) yield return (sb.ToString(), current);
    }

    SKTypeface Resolve(SKFont baseFont, SKTypeface baseFace, int codepoint)
    {
        if (baseFont.ContainsGlyph(codepoint)) return baseFace;
        var match = FontManager.MatchCharacter(codepoint);
        if (match is null) return baseFace;
        if (!_fallback.TryGetValue(match.FamilyName, out var cached))
            _fallback[match.FamilyName] = cached = match;
        return cached;
    }
}

/// <summary>A decoded Skia image, as handed to the engine.</summary>
public sealed class SkiaImage : IImage, IDisposable
{
    internal SkiaImage(SKImage native) => Native = native;

    /// <summary>The underlying Skia image.</summary>
    public SKImage Native { get; }

    public int Width => Native.Width;
    public int Height => Native.Height;

    public void Dispose() => Native.Dispose();
}

/// <summary>The Skia <see cref="IImageDecoder"/> — decodes to a raster <see cref="SKImage"/>.</summary>
public sealed class SkiaImages : IImageDecoder
{
    public IImage? Decode(byte[] bytes)
    {
        var image = SKImage.FromEncodedData(bytes);
        return image is null ? null : new SkiaImage(image);
    }

    public IImage? DecodeFile(string path)
    {
        var image = SKImage.FromEncodedData(path);
        return image is null ? null : new SkiaImage(image);
    }
}
