using Android.Util;
// SwiftDotNet.Color (the semantic token factory in Values.cs) shadows Android.Graphics.Color inside this
// namespace, so the platform type is always spelled AColor here.
using AColor = Android.Graphics.Color;

namespace SwiftDotNet;

/// <summary>
/// Resolves the shared wire tokens (<see cref="SwiftColor"/>, <see cref="SwiftFont"/>) into Android
/// values.
///
/// The live vocabulary reuses the core tokens deliberately rather than inventing surface-specific ones,
/// so <c>Color.Red</c> means the same thing in an app view and on a lock screen. The resolution differs
/// though: a notification or widget has no <see cref="Theme"/> to consult and no ambient environment
/// cascade — it is rendered by SystemUI against a background we do not control — so the semantic tokens
/// resolve against the *system* palette here, not the app's.
/// </summary>
static class LiveTokens
{
    /// <summary>
    /// Semantic color → ARGB. <paramref name="dark"/> selects the night variant for the two tokens whose
    /// whole job is to contrast with the surface (<c>primary</c> and <c>secondary</c>).
    /// </summary>
    public static AColor Resolve(string token, bool dark)
    {
        if (token.Length > 0 && token[0] == '#') return ParseHex(token);

        return token switch
        {
            "primary" => dark ? AColor.White : AColor.Black,
            "secondary" => dark ? AColor.Argb(0xB3, 0xFF, 0xFF, 0xFF)
                                : AColor.Argb(0x8A, 0x00, 0x00, 0x00),
            "red" => AColor.Argb(0xFF, 0xFF, 0x3B, 0x30),
            "green" => AColor.Argb(0xFF, 0x34, 0xC7, 0x59),
            "blue" => AColor.Argb(0xFF, 0x00, 0x7A, 0xFF),
            // There is no app accent color available to SystemUI; the platform blue is the honest stand-in.
            "accentColor" => AColor.Argb(0xFF, 0x00, 0x7A, 0xFF),
            _ => dark ? AColor.White : AColor.Black,
        };
    }

    static AColor ParseHex(string hex)
    {
        var s = hex.AsSpan(1);
        // #RGB / #RRGGBB / #AARRGGBB, matching what the other backends accept.
        if (s.Length == 3)
            return AColor.Argb(0xFF, Nibble(s[0]), Nibble(s[1]), Nibble(s[2]));
        if (s.Length == 6)
            return AColor.Argb(0xFF, Byte(s, 0), Byte(s, 2), Byte(s, 4));
        if (s.Length == 8)
            return AColor.Argb(Byte(s, 0), Byte(s, 2), Byte(s, 4), Byte(s, 6));
        return AColor.Black;

        static int Nibble(char c) => Hex(c) * 17;
        static int Byte(ReadOnlySpan<char> s, int i) => Hex(s[i]) * 16 + Hex(s[i + 1]);
        static int Hex(char c) => c switch
        {
            >= '0' and <= '9' => c - '0',
            >= 'a' and <= 'f' => c - 'a' + 10,
            >= 'A' and <= 'F' => c - 'A' + 10,
            _ => 0,
        };
    }

    /// <summary>
    /// Semantic font → sp. Deliberately absolute rather than tied to a text appearance: a
    /// <c>RemoteViews</c> tree can set a text *size* (<c>setTextViewTextSize</c>) but cannot swap a text
    /// appearance at runtime, so a token has to become a number somewhere and this is that place.
    /// </summary>
    public static float FontSizeSp(string token) => token switch
    {
        "largeTitle" => 34f,
        "title" => 28f,
        "headline" => 17f,
        "caption" => 12f,
        _ => 17f,   // body
    };

    /// <summary>Whether a semantic font implies bold weight, matching how the Swift side maps it.</summary>
    public static bool IsBold(string token) => token == "headline";

    /// <summary>Points (the vocabulary's unit, matching SwiftUI) → device pixels.</summary>
    public static int Px(double points, DisplayMetrics? metrics) =>
        (int)Math.Round(points * (metrics?.Density ?? 1f));
}
