using System.Globalization;

namespace SwiftDotNet.Graphics;

/// <summary>
/// A non-premultiplied 8-bit-per-channel RGBA colour. Mirrors <c>SKColor</c>'s shape so the engine's
/// colour arithmetic moved across unchanged.
/// </summary>
/// <remarks>
/// Distinct from Core's <c>SwiftColor</c> on purpose: that one is a <em>declarative</em> colour (a
/// semantic token that each native backend resolves against its own OS palette), whereas this is a
/// resolved literal the rasterizer can hand straight to a shader. <see cref="Theme.Color"/> is the
/// boundary between the two.
/// </remarks>
public readonly record struct Color(byte R, byte G, byte B, byte A)
{
    public Color(byte r, byte g, byte b) : this(r, g, b, 255) { }

    /// <summary>The same colour at a different alpha — the engine's most common colour operation.</summary>
    public Color WithAlpha(byte alpha) => new(R, G, B, alpha);

    /// <summary>Packed 0xAARRGGBB, for backends that want a single uint uniform.</summary>
    public uint ToArgb() => (uint)(A << 24 | R << 16 | G << 8 | B);

    /// <summary>
    /// The colour as four 0–1 floats, which is the form every GPU backend actually wants. Kept here rather
    /// than in each backend so the sRGB convention is defined in exactly one place.
    /// </summary>
    public (float r, float g, float b, float a) ToFloats() => (R / 255f, G / 255f, B / 255f, A / 255f);

    /// <summary>
    /// Parses <c>#RGB</c>, <c>#RRGGBB</c> or <c>#AARRGGBB</c> (the forms the DSL's wire format emits).
    /// Returns false rather than throwing — a malformed colour in a prop should degrade, not crash a frame.
    /// </summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(text)) return false;
        var s = text[0] == '#' ? text[1..] : text;

        // #RGB / #ARGB shorthand expands each nibble ("#f0a" → "#ff00aa"). Matches SkiaSharp's parser,
        // which the engine previously relied on — 4 chars is ARGB, not RGBA.
        if (s.Length is 3 or 4)
        {
            Span<int> n = stackalloc int[s.Length];
            for (var i = 0; i < s.Length; i++)
                if (!TryNibble(s[i], out n[i])) return false;

            color = s.Length == 3
                ? new Color((byte)(n[0] * 17), (byte)(n[1] * 17), (byte)(n[2] * 17))
                : new Color((byte)(n[1] * 17), (byte)(n[2] * 17), (byte)(n[3] * 17), (byte)(n[0] * 17));
            return true;
        }

        if (s.Length != 6 && s.Length != 8) return false;
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;

        color = s.Length == 6
            ? new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : new Color((byte)(v >> 16), (byte)(v >> 8), (byte)v, (byte)(v >> 24));
        return true;
    }

    static bool TryNibble(char c, out int value)
    {
        value = c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => -1,
        };
        return value >= 0;
    }

    public override string ToString() => $"#{A:X2}{R:X2}{G:X2}{B:X2}";
}

/// <summary>The handful of literal colours the engine names directly.</summary>
public static class Colors
{
    public static readonly Color Transparent = new(0, 0, 0, 0);
    public static readonly Color Black = new(0, 0, 0);
    public static readonly Color White = new(255, 255, 255);
}
