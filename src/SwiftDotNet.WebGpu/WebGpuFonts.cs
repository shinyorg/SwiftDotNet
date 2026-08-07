using System.Numerics;
using System.Runtime.InteropServices;
using StbTrueTypeSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>A font face at a concrete size, backed by a stb_truetype face.</summary>
public sealed class WebGpuFont : Graphics.Font
{
    internal WebGpuFont(FontFace face, float size, FontMetrics metrics)
    {
        Face = face;
        Size = size;
        Metrics = metrics;
    }

    internal FontFace Face { get; }
    public override float Size { get; }
    public override FontMetrics Metrics { get; }
}

/// <summary>
/// A loaded TrueType file. Owns the pinned bytes stb_truetype reads through for the process lifetime.
/// </summary>
sealed class FontFace : IDisposable
{
    readonly GCHandle _pin;

    unsafe FontFace(byte[] data, string path)
    {
        // stb_truetype keeps the pointer, not a copy, so the array has to stay put for the face's life.
        _pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        Path = path;
        Info = new StbTrueType.stbtt_fontinfo();

        var ok = StbTrueType.stbtt_InitFont(Info, (byte*)_pin.AddrOfPinnedObject(), 0) != 0;
        if (!ok) throw new InvalidOperationException($"Not a usable TrueType font: {path}");

        int ascent, descent, lineGap;
        StbTrueType.stbtt_GetFontVMetrics(Info, &ascent, &descent, &lineGap);
        UnitsAscent = ascent;
        UnitsDescent = descent;
        UnitsLineGap = lineGap;
    }

    public string Path { get; }
    public StbTrueType.stbtt_fontinfo Info { get; }
    public int UnitsAscent { get; }
    public int UnitsDescent { get; }
    public int UnitsLineGap { get; }

    public static FontFace Load(string path) => new(File.ReadAllBytes(path), path);

    public void Dispose()
    {
        if (_pin.IsAllocated) _pin.Free();
    }
}

/// <summary>A glyph's placement within a laid-out run.</summary>
readonly record struct GlyphQuad(Graphics.Rect Bounds, Vector4 Uv);

/// <summary>
/// The WebGPU <see cref="IFontProvider"/>: measures text with stb_truetype and rasterizes glyphs into a
/// single-channel atlas the shader samples as coverage.
/// </summary>
/// <remarks>
/// <para>This is the piece that keeps the backend genuinely Skia-free. Glyph rasterization is the one
/// thing a GPU cannot do for you — everything else the UI draws is a distance field — so it is done on
/// the CPU once per (face, size, codepoint) and cached in the atlas forever after.</para>
///
/// <para>Font discovery is by well-known path, because there is no cross-platform font enumeration API
/// without dragging in the very native dependency this backend exists to avoid. Set
/// <see cref="RegularPath"/> / <see cref="BoldPath"/> before first use to supply your own; shipping a
/// font with the app is the reliable option.</para>
/// </remarks>
public sealed class WebGpuFonts : IFontProvider, IDisposable
{
    static readonly string[] RegularCandidates =
    [
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/System/Library/Fonts/Supplemental/Helvetica.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/TTF/DejaVuSans.ttf",
        @"C:\Windows\Fonts\segoeui.ttf",
        @"C:\Windows\Fonts\arial.ttf",
    ];

    static readonly string[] BoldCandidates =
    [
        "/System/Library/Fonts/Supplemental/Arial Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSans-Bold.ttf",
        @"C:\Windows\Fonts\segoeuib.ttf",
        @"C:\Windows\Fonts\arialbd.ttf",
    ];

    /// <summary>Explicit path to the regular face. Set before the first <see cref="Get"/>.</summary>
    public static string? RegularPath { get; set; }

    /// <summary>Explicit path to the bold face; falls back to the regular one when unset or missing.</summary>
    public static string? BoldPath { get; set; }

    readonly Dictionary<(float, bool), WebGpuFont> _fonts = new();
    readonly Dictionary<(string, float, int), GlyphEntry> _glyphs = new();
    FontFace? _regular;
    FontFace? _bold;

    internal GlyphAtlas Atlas { get; } = new(1024, 1024);

    readonly record struct GlyphEntry(Vector4 Uv, float XOffset, float YOffset, float Width, float Height, float Advance);

    public Graphics.Font Get(float size, bool bold)
    {
        if (_fonts.TryGetValue((size, bold), out var cached)) return cached;

        var face = bold ? BoldFace : RegularFace;
        var scale = StbTrueType.stbtt_ScaleForPixelHeight(face.Info, size);

        // The engine's convention matches Skia's: ascent is negative (up from the baseline).
        var metrics = new FontMetrics(
            -face.UnitsAscent * scale,
            -face.UnitsDescent * scale,
            face.UnitsLineGap * scale);

        var font = new WebGpuFont(face, size, metrics);
        _fonts[(size, bold)] = font;
        return font;
    }

    public unsafe float Measure(string text, Graphics.Font font)
    {
        if (string.IsNullOrEmpty(text) || font is not WebGpuFont f) return 0;

        var scale = StbTrueType.stbtt_ScaleForPixelHeight(f.Face.Info, f.Size);
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            int advance, lsb;
            StbTrueType.stbtt_GetCodepointHMetrics(f.Face.Info, rune.Value, &advance, &lsb);
            width += advance * scale;
        }
        return width;
    }

    /// <summary>Places each glyph of a run, rasterizing any not already in the atlas.</summary>
    internal unsafe IEnumerable<GlyphQuad> LayoutRun(string text, Graphics.Font font, float x, float baselineY)
    {
        if (font is not WebGpuFont f) yield break;

        var scale = StbTrueType.stbtt_ScaleForPixelHeight(f.Face.Info, f.Size);
        foreach (var rune in text.EnumerateRunes())
        {
            var entry = GetGlyph(f, scale, rune.Value);
            if (entry.Width > 0 && entry.Height > 0)
            {
                var left = x + entry.XOffset;
                var top = baselineY + entry.YOffset;
                yield return new GlyphQuad(
                    new Graphics.Rect(left, top, left + entry.Width, top + entry.Height),
                    entry.Uv);
            }
            x += entry.Advance;
        }
    }

    unsafe GlyphEntry GetGlyph(WebGpuFont font, float scale, int codepoint)
    {
        var key = (font.Face.Path, font.Size, codepoint);
        if (_glyphs.TryGetValue(key, out var cached)) return cached;

        int advance, lsb;
        StbTrueType.stbtt_GetCodepointHMetrics(font.Face.Info, codepoint, &advance, &lsb);

        int x0, y0, x1, y1;
        StbTrueType.stbtt_GetCodepointBitmapBox(font.Face.Info, codepoint, scale, scale, &x0, &y0, &x1, &y1);

        var w = x1 - x0;
        var h = y1 - y0;

        GlyphEntry entry;
        if (w <= 0 || h <= 0 || !Atlas.TryReserve(w, h, out var slot))
        {
            // Whitespace, an unmapped codepoint, or a full atlas: still advance the pen.
            entry = new GlyphEntry(default, 0, 0, 0, 0, advance * scale);
        }
        else
        {
            fixed (byte* pixels = Atlas.Pixels)
                StbTrueType.stbtt_MakeCodepointBitmap(
                    font.Face.Info,
                    pixels + slot.Y * Atlas.Width + slot.X,
                    w, h, Atlas.Width, scale, scale, codepoint);

            Atlas.Dirty = true;
            entry = new GlyphEntry(
                new Vector4(
                    (float)slot.X / Atlas.Width,
                    (float)slot.Y / Atlas.Height,
                    (float)(slot.X + w) / Atlas.Width,
                    (float)(slot.Y + h) / Atlas.Height),
                x0, y0, w, h, advance * scale);
        }

        _glyphs[key] = entry;
        return entry;
    }

    FontFace RegularFace => _regular ??= FontFace.Load(Resolve(RegularPath, RegularCandidates, "regular"));

    FontFace BoldFace
    {
        get
        {
            if (_bold is not null) return _bold;
            var path = BoldPath ?? FirstExisting(BoldCandidates);
            return _bold = path is null ? RegularFace : FontFace.Load(path);
        }
    }

    static string Resolve(string? explicitPath, string[] candidates, string what)
    {
        if (explicitPath is not null)
        {
            if (File.Exists(explicitPath)) return explicitPath;
            throw new FileNotFoundException($"WebGpuFonts: the {what} font path does not exist.", explicitPath);
        }

        return FirstExisting(candidates)
            ?? throw new FileNotFoundException(
                $"WebGpuFonts: no {what} system font found. Set WebGpuFonts.{(what == "regular" ? "RegularPath" : "BoldPath")} " +
                "to a .ttf, or ship one with the app.");
    }

    static string? FirstExisting(string[] paths)
    {
        foreach (var path in paths)
            if (File.Exists(path)) return path;
        return null;
    }

    public void Dispose()
    {
        _regular?.Dispose();
        _bold?.Dispose();
    }
}

/// <summary>
/// A single-channel glyph atlas with a shelf packer — glyphs are placed left to right on rows whose
/// height is set by the first glyph on them. Crude, but glyph boxes at a handful of UI sizes are close
/// enough in height that the waste is small, and it never needs to move a glyph once placed.
/// </summary>
sealed class GlyphAtlas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;

    /// <summary>Coverage, one byte per texel.</summary>
    public byte[] Pixels { get; } = new byte[width * height];

    /// <summary>Set when a glyph was added; the renderer re-uploads and clears it.</summary>
    public bool Dirty { get; set; } = true;

    int _penX, _penY, _shelfHeight;

    public bool TryReserve(int w, int h, out (int X, int Y) slot)
    {
        const int padding = 1;   // keeps bilinear sampling from bleeding a neighbour into a glyph edge

        if (_penX + w + padding > Width)
        {
            _penX = 0;
            _penY += _shelfHeight + padding;
            _shelfHeight = 0;
        }

        if (_penY + h + padding > Height)
        {
            slot = default;
            return false;
        }

        slot = (_penX, _penY);
        _penX += w + padding;
        _shelfHeight = Math.Max(_shelfHeight, h);
        return true;
    }
}
