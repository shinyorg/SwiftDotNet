using SkiaSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Adapts a SkiaSharp <see cref="SKCanvas"/> to the engine's <see cref="ICanvas"/> seam.
/// </summary>
/// <remarks>
/// This is the whole Skia backend now: the layout, hit-testing, gesture and paint logic all live in
/// <c>SwiftDotNet.Graphics</c>, and this type only translates the closed set of primitives that seam
/// exposes into Skia calls. Wrapping is cheap — allocate one per frame around the host's canvas.
/// </remarks>
public sealed class SkiaCanvas : ICanvas
{
    readonly SKCanvas _canvas;
    readonly SkiaFonts _fonts;

    public SkiaCanvas(SKCanvas canvas, SkiaFonts fonts)
    {
        _canvas = canvas;
        _fonts = fonts;
    }

    /// <summary>The underlying Skia canvas, for a custom renderer that wants full Skia access.</summary>
    public SKCanvas Native => _canvas;

    public void Clear(Graphics.Color color) => _canvas.Clear(ToSk(color));

    public int Save() => _canvas.Save();
    public void RestoreToCount(int count) => _canvas.RestoreToCount(count);

    public void SaveLayer(float opacity)
    {
        using var layer = new SKPaint { Color = SKColors.White.WithAlpha((byte)(Math.Clamp(opacity, 0, 1) * 255)) };
        _canvas.SaveLayer(layer);
    }

    public void Translate(float dx, float dy) => _canvas.Translate(dx, dy);
    public void Scale(float sx, float sy) => _canvas.Scale(sx, sy);
    public void RotateDegrees(float degrees, float pivotX, float pivotY) => _canvas.RotateDegrees(degrees, pivotX, pivotY);
    public void ClipRect(Graphics.Rect rect) => _canvas.ClipRect(ToSk(rect));

    public void DrawRect(Graphics.Rect rect, in Graphics.Paint paint)
    {
        using var p = ToSk(paint);
        _canvas.DrawRect(ToSk(rect), p);
    }

    public void DrawRoundRect(Graphics.Rect rect, float radiusX, float radiusY, in Graphics.Paint paint)
    {
        using var p = ToSk(paint);
        _canvas.DrawRoundRect(ToSk(rect), radiusX, radiusY, p);
    }

    public void DrawOval(Graphics.Rect rect, in Graphics.Paint paint)
    {
        using var p = ToSk(paint);
        _canvas.DrawOval(ToSk(rect), p);
    }

    public void DrawCircle(float centerX, float centerY, float radius, in Graphics.Paint paint)
    {
        using var p = ToSk(paint);
        _canvas.DrawCircle(centerX, centerY, radius, p);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, in Graphics.Paint paint)
    {
        // DrawLine always strokes regardless of Style, which is what the engine assumes — several separator
        // paints are built without an explicit Stroke style.
        using var p = ToSk(paint);
        p.Style = SKPaintStyle.Stroke;
        _canvas.DrawLine(x0, y0, x1, y1, p);
    }

    public void DrawImage(IImage image, Graphics.Rect dest)
    {
        if (image is not SkiaImage { Native: { } native }) return;
        _canvas.DrawImage(native, ToSk(dest), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>
    /// Draws one line, splitting it into runs by fallback face so emoji and non-Latin script render through
    /// a matched typeface instead of tofu. Advances are taken from the same per-run measurement
    /// <see cref="SkiaFonts.Measure"/> uses, so painted and measured widths agree.
    /// </summary>
    public void DrawText(string text, float x, float baselineY, Graphics.Font font, Graphics.Color color)
    {
        if (string.IsNullOrEmpty(text) || font is not SkiaFont sf) return;

        using var paint = new SKPaint { Color = ToSk(color), IsAntialias = true };
        foreach (var (run, face) in _fonts.Runs(text, sf))
        {
            using var runFont = new SKFont(face, sf.Size);
            _canvas.DrawText(run, x, baselineY, runFont, paint);
            x += runFont.MeasureText(run);
        }
    }

    // ---- conversions ---------------------------------------------------------

    internal static SKColor ToSk(Graphics.Color c) => new(c.R, c.G, c.B, c.A);

    internal static SKRect ToSk(Graphics.Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    internal static SKPoint ToSk(Graphics.Point p) => new(p.X, p.Y);

    internal static Graphics.Point FromSk(SKPoint p) => new(p.X, p.Y);

    internal static Graphics.Rect FromSk(SKRect r) => new(r.Left, r.Top, r.Right, r.Bottom);

    internal static Graphics.Size FromSk(SKSize s) => new(s.Width, s.Height);

    static SKPaint ToSk(in Graphics.Paint paint)
    {
        var p = new SKPaint
        {
            Color = ToSk(paint.Color),
            IsAntialias = paint.IsAntialias,
            Style = paint.Style == PaintStyle.Stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = paint.StrokeWidth,
            StrokeCap = paint.StrokeCap switch
            {
                Graphics.StrokeCap.Round => SKStrokeCap.Round,
                Graphics.StrokeCap.Square => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt,
            },
        };

        if (paint.Gradient is { } g) p.Shader = ToShader(g);

        // The seam carries a shadow as four numbers; Skia wants a filter. Rebuilding it here keeps the
        // engine free of Skia's filter graph and lets a GPU backend render the same spec as an SDF falloff.
        if (paint.Shadow is { } s)
            p.ImageFilter = SKImageFilter.CreateDropShadow(s.Dx, s.Dy, s.Radius, s.Radius, ToSk(s.Color));

        return p;
    }

    static SKShader ToShader(Gradient g)
    {
        var colors = new SKColor[g.Stops.Length];
        var positions = new float[g.Stops.Length];
        for (var i = 0; i < g.Stops.Length; i++)
        {
            colors[i] = ToSk(g.Stops[i].Color);
            positions[i] = g.Stops[i].Location;
        }

        return g.Kind == GradientKind.Radial
            ? SKShader.CreateRadialGradient(ToSk(g.Center), g.Radius, colors, positions, SKShaderTileMode.Clamp)
            : SKShader.CreateLinearGradient(ToSk(g.Start), ToSk(g.End), colors, positions, SKShaderTileMode.Clamp);
    }
}
