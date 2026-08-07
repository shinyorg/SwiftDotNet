namespace SwiftDotNet.Graphics;

/// <summary>Fill the shape, or stroke its outline.</summary>
public enum PaintStyle { Fill, Stroke }

/// <summary>How a stroked line terminates.</summary>
public enum StrokeCap { Butt, Round, Square }

/// <summary>
/// A drop shadow, as a description rather than an opaque filter object.
/// </summary>
/// <remarks>
/// This is the one place the seam deliberately diverges from SkiaSharp. The engine used to build an
/// <c>SKImageFilter.CreateDropShadow(...)</c> and hang it off the paint, which forces every backend to own
/// a general image-filter graph. Keeping the shadow as four numbers instead lets the Skia adapter rebuild
/// exactly that filter while a GPU backend renders it as an SDF falloff in the same draw call — much
/// cheaper, and the reason shadows are not a fallback on the WebGPU backend.
/// </remarks>
public readonly record struct Shadow(float Dx, float Dy, float Radius, Color Color);

/// <summary>
/// Everything a single draw call needs beyond its geometry. A value type: the engine constructs these
/// inline per draw and never has to dispose one (the Skia engine's old <c>using var paint = new SKPaint</c>
/// per primitive was both noisy and a per-frame allocation).
/// </summary>
public readonly record struct Paint
{
    public Color Color { get; init; }
    public PaintStyle Style { get; init; }
    public float StrokeWidth { get; init; }
    public StrokeCap StrokeCap { get; init; }

    /// <summary>Antialias edges. Defaults to true via <see cref="Fill"/>/<see cref="Stroke"/>.</summary>
    public bool IsAntialias { get; init; }

    /// <summary>When set, replaces <see cref="Color"/> as the fill source.</summary>
    public Gradient? Gradient { get; init; }

    /// <summary>When set, the shape casts this shadow beneath itself.</summary>
    public Shadow? Shadow { get; init; }

    public static Paint Fill(Color color) =>
        new() { Color = color, Style = PaintStyle.Fill, IsAntialias = true };

    public static Paint Fill(Gradient gradient) =>
        new() { Color = Colors.White, Style = PaintStyle.Fill, Gradient = gradient, IsAntialias = true };

    public static Paint Stroke(Color color, float width) =>
        new() { Color = color, Style = PaintStyle.Stroke, StrokeWidth = width, IsAntialias = true };

    /// <summary>
    /// A hairline used for separators and rules. Antialiasing is off on purpose: a 1px horizontal rule on a
    /// whole-pixel boundary renders crisp when aliased and as a 2px smear when not.
    /// </summary>
    public static Paint Hairline(Color color, float width = 1f) =>
        new() { Color = color, Style = PaintStyle.Stroke, StrokeWidth = width, IsAntialias = false };

    public Paint With(Shadow? shadow) => this with { Shadow = shadow };
}
