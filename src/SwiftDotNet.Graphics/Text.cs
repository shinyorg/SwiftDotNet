using System.Text;

namespace SwiftDotNet.Graphics;

/// <summary>Vertical metrics of a font, in the engine's coordinate space. <see cref="Ascent"/> is negative.</summary>
public readonly record struct FontMetrics(float Ascent, float Descent, float Leading)
{
    /// <summary>Distance between consecutive baselines.</summary>
    public float LineHeight => Descent - Ascent;
}

/// <summary>
/// An opaque handle to a rasterizer's font at a concrete size. Backends subclass this to carry their
/// native face (an <c>SKFont</c>, a glyph atlas page, a Unity <c>Font</c>); the engine only ever reads
/// <see cref="Size"/> and <see cref="Metrics"/>.
/// </summary>
public abstract class Font
{
    public abstract float Size { get; }
    public abstract FontMetrics Metrics { get; }
}

/// <summary>
/// Supplies fonts and measures text. Separate from <see cref="ICanvas"/> because the <em>layout</em> pass
/// needs measurement long before anything is drawn — this is the one piece of the rasterizer the engine
/// depends on outside the paint pass.
/// </summary>
public interface IFontProvider
{
    /// <summary>A font at a concrete size and weight. Implementations are expected to cache.</summary>
    Font Get(float size, bool bold);

    /// <summary>
    /// Advance width of a single line, which MUST account for whatever per-run font fallback
    /// <see cref="ICanvas.DrawText"/> will apply — otherwise measured and painted widths disagree and
    /// centred text drifts as soon as a string contains emoji or non-Latin script.
    /// </summary>
    float Measure(string text, Font font);
}

/// <summary>
/// Renderer-independent text layout: greedy word wrap and block measurement, built on
/// <see cref="IFontProvider.Measure"/>. Shaping and fallback stay in the backend, since those depend on
/// the font system; line breaking does not, so it lives here and behaves identically everywhere.
/// </summary>
public static class TextLayout
{
    /// <summary>Greedy word-wrap to <paramref name="maxWidth"/>; also honors explicit newlines.</summary>
    public static List<string> Wrap(string text, Font font, float maxWidth, IFontProvider fonts)
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
                if (current.Length == 0 || fonts.Measure(trial, font) <= maxWidth)
                    current = trial;
                else { lines.Add(current); current = word; }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>Size of a single unwrapped line.</summary>
    public static Size MeasureLine(string text, Font font, IFontProvider fonts) =>
        new(fonts.Measure(text, font), font.Metrics.LineHeight);

    /// <summary>Size of <paramref name="text"/> wrapped to <paramref name="maxWidth"/>.</summary>
    public static Size MeasureWrapped(string text, Font font, float maxWidth, IFontProvider fonts)
    {
        var lines = Wrap(text, font, maxWidth, fonts);
        var width = 0f;
        foreach (var line in lines) width = Math.Max(width, fonts.Measure(line, font));
        return new Size(width, lines.Count * font.Metrics.LineHeight);
    }

    /// <summary>
    /// Splits a string into runs of consecutive runes, so a backend that resolves fallback faces per rune
    /// has a shared starting point. Backends that shape whole strings can ignore this.
    /// </summary>
    public static IEnumerable<string> RunesByFace(string text, Func<int, object?> faceOf)
    {
        if (string.IsNullOrEmpty(text)) yield break;

        var sb = new StringBuilder();
        object? current = null;
        foreach (var (codepoint, glyph) in CodePoints(text))
        {
            var face = faceOf(codepoint);
            if (current is null) current = face;
            else if (!ReferenceEquals(face, current)) { yield return sb.ToString(); sb.Clear(); current = face; }
            sb.Append(glyph);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>
    /// Walks a string as Unicode code points, keeping surrogate pairs together so an emoji resolves as one
    /// character rather than two unassigned halves.
    /// </summary>
    /// <remarks>
    /// Hand-rolled rather than <c>string.EnumerateRunes()</c>, which is not available on netstandard2.1 —
    /// and doing it in one place means every target segments text identically.
    /// </remarks>
    public static IEnumerable<(int CodePoint, string Text)> CodePoints(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                yield return (char.ConvertToUtf32(text[i], text[i + 1]), text.Substring(i, 2));
                i++;
            }
            else
            {
                yield return (text[i], text[i].ToString());
            }
        }
    }
}
