using System.Globalization;

namespace SwiftDotNet.Graphics;

public enum GradientKind { Linear, Radial }

/// <summary>One colour stop at a fractional position (0–1) along the gradient.</summary>
public readonly record struct ColorStop(Color Color, float Location);

/// <summary>
/// A gradient fill already resolved into concrete coordinates for the shape it fills.
/// </summary>
/// <remarks>
/// Resolution happens here, not in the backend, because the geometry is identical everywhere: an angle
/// projected onto the frame's half-diagonal. Each adapter then only has to turn this into its own
/// primitive — <c>SKShader.CreateLinearGradient</c>, or two endpoints and a stop table in a uniform buffer.
/// </remarks>
public sealed record Gradient
{
    public required GradientKind Kind { get; init; }
    public required ColorStop[] Stops { get; init; }

    /// <summary>Linear only: the axis the gradient runs along.</summary>
    public Point Start { get; init; }
    public Point End { get; init; }

    /// <summary>Radial only.</summary>
    public Point Center { get; init; }
    public float Radius { get; init; }

    /// <summary>
    /// Parses Core's <c>Brush</c> wire string (see <c>Brush.cs</c> for the grammar) and resolves it against
    /// the frame it will fill. Returns null for a malformed spec — a bad gradient falls back to the node's
    /// flat background rather than failing the frame.
    /// </summary>
    public static Gradient? Parse(string spec, Rect frame, bool dark)
    {
        var firstColon = spec.IndexOf(':');
        if (firstColon < 0) return null;
        var kind = spec[..firstColon];
        var rest = spec[(firstColon + 1)..];

        if (kind == "linear")
        {
            var secondColon = rest.IndexOf(':');
            if (secondColon < 0) return null;
            var angle = double.TryParse(rest[..secondColon], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 90;
            if (ParseStops(rest[(secondColon + 1)..], dark) is not { } stops) return null;

            // Angle: 0° = left→right, 90° = top→bottom. Project the frame's half-diagonal onto that axis.
            var rad = angle * Math.PI / 180.0;
            var dx = (float)Math.Cos(rad) * frame.Width / 2f;
            var dy = (float)Math.Sin(rad) * frame.Height / 2f;
            return new Gradient
            {
                Kind = GradientKind.Linear,
                Stops = stops,
                Start = new Point(frame.MidX - dx, frame.MidY - dy),
                End = new Point(frame.MidX + dx, frame.MidY + dy),
            };
        }

        if (kind == "radial")
        {
            if (ParseStops(rest, dark) is not { } stops) return null;
            return new Gradient
            {
                Kind = GradientKind.Radial,
                Stops = stops,
                Center = new Point(frame.MidX, frame.MidY),
                Radius = Math.Max(frame.Width, frame.Height) / 2f,
            };
        }

        return null;
    }

    static ColorStop[]? ParseStops(string spec, bool dark)
    {
        var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;

        var stops = new ColorStop[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            // Colours never contain '@', so the LAST one separates colour from location.
            var at = parts[i].LastIndexOf('@');
            if (at < 0) return null;
            var location = float.TryParse(parts[i][(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
            stops[i] = new ColorStop(Theme.Color(parts[i][..at], dark), location);
        }
        return stops;
    }
}
