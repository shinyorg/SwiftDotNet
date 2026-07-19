using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// A fill for <c>.Background(Brush)</c> — a gradient rather than a flat color. Serializes to a compact
/// string prop (the same "rich value rides as a string" trick <c>Map</c> uses) that each backend parses
/// with <see cref="Parse"/> and maps to its native gradient (SwiftUI <c>LinearGradient</c>, Compose
/// <c>Brush.linearGradient</c>, CSS <c>linear-gradient</c>, GTK/Skia gradient shaders).
///
/// Wire grammar (colon-sectioned, no JSON so every backend parses it cheaply):
/// <code>
///   linear:&lt;angleDeg&gt;:&lt;color&gt;@&lt;loc&gt;;&lt;color&gt;@&lt;loc&gt;;…
///   radial:&lt;color&gt;@&lt;loc&gt;;&lt;color&gt;@&lt;loc&gt;;…
/// </code>
/// where <c>color</c> is a semantic token or <c>#hex</c> and <c>loc</c> is 0–1. Colors never contain
/// <c>: @ ;</c> so the split is unambiguous.
/// </summary>
public abstract class Brush
{
    internal abstract string Serialize();
}

/// <summary>One color stop of a <see cref="Brush"/> gradient at a fractional <paramref name="Location"/> (0–1).</summary>
public readonly record struct GradientStop(SwiftColor Color, double Location);

/// <summary>
/// A linear gradient sweeping across the view at <paramref name="Angle"/> degrees
/// (0 = left→right, 90 = top→bottom), through the given <see cref="GradientStop"/>s.
/// </summary>
public sealed class LinearGradient : Brush
{
    readonly double _angle;
    readonly GradientStop[] _stops;

    public LinearGradient(double angle, params GradientStop[] stops)
    {
        _angle = angle;
        _stops = stops;
    }

    /// <summary>Convenience for the common two-color, top→bottom gradient.</summary>
    public LinearGradient(SwiftColor from, SwiftColor to, double angle = 90)
        : this(angle, new GradientStop(from, 0), new GradientStop(to, 1)) { }

    internal override string Serialize() =>
        "linear:" + _angle.ToString(CultureInfo.InvariantCulture) + ":" + BrushJson.Stops(_stops);
}

/// <summary>A radial gradient from the center outward through the given <see cref="GradientStop"/>s.</summary>
public sealed class RadialGradient : Brush
{
    readonly GradientStop[] _stops;

    public RadialGradient(params GradientStop[] stops) => _stops = stops;

    public RadialGradient(SwiftColor from, SwiftColor to)
        : this(new GradientStop(from, 0), new GradientStop(to, 1)) { }

    internal override string Serialize() => "radial:" + BrushJson.Stops(_stops);
}

static class BrushJson
{
    public static string Stops(GradientStop[] stops)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < stops.Length; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(stops[i].Color.Value).Append('@')
              .Append(stops[i].Location.ToString(CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }
}
