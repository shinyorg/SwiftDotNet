using System.Text;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Text layout + drawing for the Skia backend: greedy word-wrap and per-run font fallback so emoji and
/// non-Latin scripts (which the default typeface lacks) render via a matched face instead of tofu boxes.
/// A single base typeface can't cover 👋/🍎/你好, so <see cref="Runs"/> splits a string into runs by the
/// face that can draw each rune (matched through <see cref="SKFontManager"/>), and measure/draw walk them.
/// </summary>
static class SkiaText
{
    static readonly SKFontManager FontManager = SKFontManager.Default;
    static readonly Dictionary<string, SKTypeface> FallbackCache = new();

    /// <summary>Greedy word-wrap to <paramref name="maxWidth"/>; also honors explicit newlines.</summary>
    public static List<string> Wrap(string text, SKFont font, float maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

        foreach (var para in text.Split('\n'))
        {
            if (maxWidth <= 0) { lines.Add(para); continue; }
            var current = "";
            foreach (var word in para.Split(' '))
            {
                var trial = current.Length == 0 ? word : current + " " + word;
                if (current.Length == 0 || Measure(trial, font) <= maxWidth)
                    current = trial;
                else { lines.Add(current); current = word; }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>Measures a single line, accounting for fallback-face advances (matches what <see cref="DrawLine"/> paints).</summary>
    public static float Measure(string text, SKFont baseFont)
    {
        float x = 0;
        foreach (var (run, tf) in Runs(text, baseFont))
        {
            using var f = new SKFont(tf, baseFont.Size);
            x += f.MeasureText(run);
        }
        return x;
    }

    /// <summary>Draws a single line at baseline <paramref name="baseline"/>, splitting into fallback runs.</summary>
    public static void DrawLine(SKCanvas canvas, string text, float x, float baseline, SKFont baseFont, SKColor color)
    {
        if (string.IsNullOrEmpty(text)) return;
        using var paint = new SKPaint { Color = color, IsAntialias = true };
        foreach (var (run, tf) in Runs(text, baseFont))
        {
            using var f = new SKFont(tf, baseFont.Size);
            canvas.DrawText(run, x, baseline, f, paint);
            x += f.MeasureText(run);
        }
    }

    static IEnumerable<(string run, SKTypeface tf)> Runs(string text, SKFont baseFont)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        var baseTf = baseFont.Typeface ?? SKTypeface.Default;
        var sb = new StringBuilder();
        SKTypeface? current = null;
        foreach (var rune in text.EnumerateRunes())
        {
            var tf = Resolve(baseFont, baseTf, rune.Value);
            if (current is null) current = tf;
            else if (!ReferenceEquals(tf, current)) { yield return (sb.ToString(), current); sb.Clear(); current = tf; }
            sb.Append(rune.ToString());
        }
        if (sb.Length > 0 && current is not null) yield return (sb.ToString(), current);
    }

    static SKTypeface Resolve(SKFont baseFont, SKTypeface baseTf, int codepoint)
    {
        if (baseFont.ContainsGlyph(codepoint)) return baseTf;
        var match = FontManager.MatchCharacter(codepoint);
        if (match is null) return baseTf;
        if (!FallbackCache.TryGetValue(match.FamilyName, out var cached))
            FallbackCache[match.FamilyName] = cached = match;
        return cached;
    }
}
