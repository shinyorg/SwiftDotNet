using Android.Graphics;
using SwiftDotNet.Graphics;
using GColor = SwiftDotNet.Graphics.Color;
using GFont = SwiftDotNet.Graphics.Font;
using GPaint = SwiftDotNet.Graphics.Paint;
using GRect = SwiftDotNet.Graphics.Rect;
using APaint = Android.Graphics.Paint;
using ARect = Android.Graphics.RectF;
// SwiftDotNet.LinearGradient / RadialGradient (the core Brush types) shadow the android.graphics shaders
// of the same name inside this namespace, so the platform shaders are always spelled with the A prefix.
using ALinearGradient = Android.Graphics.LinearGradient;
using ARadialGradient = Android.Graphics.RadialGradient;
using AndroidColor = Android.Graphics.Color;

namespace SwiftDotNet;

/// <summary>
/// An <see cref="ICanvas"/> over <c>android.graphics.Canvas</c>.
///
/// This is the whole of what Android has to supply for <see cref="LiveRenderMode.Bitmap"/> to work. The
/// renderer seam was extracted out of the Skia backend precisely so that a new rasterizer costs one small
/// adapter rather than a second layout engine, and this is the first place that claim gets tested on a
/// platform toolkit rather than another Skia binding.
///
/// The mapping is close to one-to-one because <c>android.graphics</c> and Skia are the same library
/// underneath. The two places it is not: <c>saveLayerAlpha</c> takes bounds (passed as the full surface,
/// since the engine only ever uses it for group opacity), and gradients have to be rebuilt as shaders per
/// draw because <see cref="GPaint"/> is a value type carrying a description rather than a native object.
/// </summary>
public sealed class AndroidCanvas : ICanvas
{
    readonly Canvas _canvas;
    readonly float _width;
    readonly float _height;
    readonly APaint _paint = new() { AntiAlias = true };

    public AndroidCanvas(Canvas canvas, float width, float height)
    {
        _canvas = canvas;
        _width = width;
        _height = height;
    }

    public void Clear(GColor color) => _canvas.DrawColor(AndroidColor(color), PorterDuff.Mode.Src!);

    public int Save() => _canvas.Save();

    public void RestoreToCount(int count) => _canvas.RestoreToCount(count);

    public void SaveLayer(float opacity) =>
        _canvas.SaveLayerAlpha(0, 0, _width, _height, (int)(opacity * 255));

    public void Translate(float dx, float dy) => _canvas.Translate(dx, dy);

    public void Scale(float sx, float sy) => _canvas.Scale(sx, sy);

    public void RotateDegrees(float degrees, float pivotX, float pivotY) =>
        _canvas.Rotate(degrees, pivotX, pivotY);

    public void ClipRect(GRect rect) =>
        _canvas.ClipRect(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public void DrawRect(GRect rect, in GPaint paint)
    {
        Apply(paint, rect);
        _canvas.DrawRect(rect.Left, rect.Top, rect.Right, rect.Bottom, _paint);
    }

    public void DrawRoundRect(GRect rect, float radiusX, float radiusY, in GPaint paint)
    {
        Apply(paint, rect);
        _canvas.DrawRoundRect(new ARect(rect.Left, rect.Top, rect.Right, rect.Bottom), radiusX, radiusY, _paint);
    }

    public void DrawOval(GRect rect, in GPaint paint)
    {
        Apply(paint, rect);
        _canvas.DrawOval(new ARect(rect.Left, rect.Top, rect.Right, rect.Bottom), _paint);
    }

    public void DrawCircle(float centerX, float centerY, float radius, in GPaint paint)
    {
        Apply(paint, GRect.Create(centerX - radius, centerY - radius, radius * 2, radius * 2));
        _canvas.DrawCircle(centerX, centerY, radius, _paint);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, in GPaint paint)
    {
        Apply(paint, new GRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)));
        _paint.SetStyle(APaint.Style.Stroke);
        _canvas.DrawLine(x0, y0, x1, y1, _paint);
    }

    public void DrawImage(IImage image, GRect dest)
    {
        if (image is not AndroidImage android) return;

        _paint.Reset();
        _paint.AntiAlias = true;
        _paint.FilterBitmap = true;
        _canvas.DrawBitmap(android.Bitmap,
            null,
            new ARect(dest.Left, dest.Top, dest.Right, dest.Bottom),
            _paint);
    }

    public void DrawText(string text, float x, float baselineY, GFont font, GColor color)
    {
        if (font is not AndroidFont androidFont) return;

        _canvas.DrawText(text, x, baselineY, androidFont.Configure(color));
    }

    void Apply(in GPaint paint, GRect frame)
    {
        _paint.Reset();
        _paint.AntiAlias = paint.IsAntialias;
        _paint.Color = AndroidColor(paint.Color);
        _paint.SetStyle(paint.Style == PaintStyle.Stroke ? APaint.Style.Stroke : APaint.Style.Fill);
        _paint.StrokeWidth = paint.StrokeWidth;
        _paint.StrokeCap = paint.StrokeCap switch
        {
            SwiftDotNet.Graphics.StrokeCap.Round => APaint.Cap.Round!,
            SwiftDotNet.Graphics.StrokeCap.Square => APaint.Cap.Square!,
            _ => APaint.Cap.Butt!,
        };

        if (paint.Gradient is { } gradient) _paint.SetShader(ShaderFor(gradient));
        if (paint.Shadow is { } shadow)
            _paint.SetShadowLayer(shadow.Radius, shadow.Dx, shadow.Dy, AndroidColor(shadow.Color));
    }

    static Shader ShaderFor(Gradient gradient)
    {
        var colors = new int[gradient.Stops.Length];
        var positions = new float[gradient.Stops.Length];
        for (var i = 0; i < gradient.Stops.Length; i++)
        {
            colors[i] = unchecked((int)gradient.Stops[i].Color.ToArgb());
            positions[i] = gradient.Stops[i].Location;
        }

        return gradient.Kind == GradientKind.Radial
            ? new ARadialGradient(gradient.Center.X, gradient.Center.Y, Math.Max(gradient.Radius, 0.01f),
                colors, positions, Shader.TileMode.Clamp!)
            : new ALinearGradient(gradient.Start.X, gradient.Start.Y, gradient.End.X, gradient.End.Y,
                colors, positions, Shader.TileMode.Clamp!);
    }

    /// <summary>Engine colour to platform colour. The engine keeps RGBA; Android wants packed ARGB.</summary>
    internal static AndroidColor AndroidColor(GColor c) =>
        new(unchecked((int)c.ToArgb()));
}

/// <summary>A decoded bitmap behind the engine's opaque <see cref="IImage"/>.</summary>
public sealed class AndroidImage : IImage
{
    public AndroidImage(Bitmap bitmap) => Bitmap = bitmap;

    public Bitmap Bitmap { get; }

    public int Width => Bitmap.Width;

    public int Height => Bitmap.Height;
}

/// <summary>Decodes PNG/JPEG bytes with <c>BitmapFactory</c>.</summary>
public sealed class AndroidImageDecoder : IImageDecoder
{
    public IImage? Decode(byte[] bytes)
    {
        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        return bitmap is null ? null : new AndroidImage(bitmap);
    }

    public IImage? DecodeFile(string path)
    {
        var bitmap = BitmapFactory.DecodeFile(path);
        return bitmap is null ? null : new AndroidImage(bitmap);
    }
}

/// <summary>A font backed by a configured <c>android.graphics.Paint</c>.</summary>
public sealed class AndroidFont : GFont
{
    readonly APaint _paint;

    internal AndroidFont(float size, bool bold)
    {
        _paint = new APaint
        {
            AntiAlias = true,
            TextSize = size,
        };
        _paint.SetTypeface(Typeface.Create(Typeface.Default, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal));

        var metrics = _paint.GetFontMetrics()!;
        Size = size;
        Metrics = new FontMetrics(metrics.Ascent, metrics.Descent, metrics.Leading);
    }

    public override float Size { get; }

    public override FontMetrics Metrics { get; }

    internal float Measure(string text) => _paint.MeasureText(text);

    internal APaint Configure(GColor color)
    {
        _paint.Color = new AndroidColor(unchecked((int)color.ToArgb()));
        return _paint;
    }
}

/// <summary>
/// Supplies <see cref="AndroidFont"/>s, cached per size/weight.
///
/// The cache is not an optimization detail: <c>Paint.getFontMetrics</c> and <c>Typeface.create</c> are
/// both comparatively expensive, and the engine asks for a font on every measured run.
/// </summary>
public sealed class AndroidFontProvider : IFontProvider
{
    readonly Dictionary<(float, bool), AndroidFont> _cache = new();

    public GFont Get(float size, bool bold)
    {
        if (_cache.TryGetValue((size, bold), out var cached)) return cached;
        var font = new AndroidFont(size, bold);
        _cache[(size, bold)] = font;
        return font;
    }

    public float Measure(string text, GFont font) =>
        font is AndroidFont android ? android.Measure(text) : 0;
}
