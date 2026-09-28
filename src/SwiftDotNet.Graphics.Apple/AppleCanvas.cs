using CoreGraphics;
using CoreText;
using Foundation;
using SwiftDotNet.Graphics;
using UIKit;
using GColor = SwiftDotNet.Graphics.Color;
using GFont = SwiftDotNet.Graphics.Font;
using GPaint = SwiftDotNet.Graphics.Paint;
using GRect = SwiftDotNet.Graphics.Rect;

namespace SwiftDotNet;

/// <summary>
/// An <see cref="ICanvas"/> over a CoreGraphics context in UIKit's flipped (top-left origin) space — what
/// <c>UIView.Draw</c> and <c>UIGraphicsImageRenderer</c> hand you.
/// </summary>
/// <remarks>
/// <para>The Apple counterpart of <see cref="AndroidCanvas"/>: the one adapter UIKit has to supply so a
/// surface that is not SwiftUI (the CarPlay map window is a plain <c>UIWindow</c>) can still draw through the
/// renderer seam instead of a second rasterizer.</para>
/// <para>Two places are not one-to-one. CoreGraphics has no save-depth token, so <see cref="Save"/> keeps its
/// own stack — and that stack also records which entries opened a transparency layer, because a layer has
/// to be ended before the state it was opened in is restored. And text is drawn with CoreText, whose text
/// matrix has to be flipped back in a flipped context or every glyph renders upside down.</para>
/// </remarks>
public sealed class AppleCanvas : ICanvas
{
    readonly CGContext _ctx;
    readonly CGRect _bounds;
    // true = the entry opened a transparency layer (SaveLayer); false = a plain Save.
    readonly Stack<bool> _states = new();

    public AppleCanvas(CGContext context, CGRect bounds)
    {
        _ctx = context;
        _bounds = bounds;
    }

    public void Clear(GColor color)
    {
        _ctx.SaveState();
        _ctx.SetBlendMode(CGBlendMode.Copy);
        _ctx.SetFillColor(ToCG(color));
        _ctx.FillRect(_bounds);
        _ctx.RestoreState();
    }

    public int Save()
    {
        // Returns the depth *before* the save — the SkCanvas/android.graphics contract, so
        // RestoreToCount(Save()) balances exactly one save.
        var depth = _states.Count;
        _ctx.SaveState();
        _states.Push(false);
        return depth;
    }

    public void RestoreToCount(int count)
    {
        while (_states.Count > Math.Max(count, 0))
        {
            if (_states.Pop())
                _ctx.EndTransparencyLayer();
            _ctx.RestoreState();
        }
    }

    public void SaveLayer(float opacity)
    {
        _ctx.SaveState();
        _ctx.SetAlpha(opacity);
        _ctx.BeginTransparencyLayer((NSDictionary?)null);
        _states.Push(true);
    }

    public void Translate(float dx, float dy) => _ctx.TranslateCTM(dx, dy);

    public void Scale(float sx, float sy) => _ctx.ScaleCTM(sx, sy);

    public void RotateDegrees(float degrees, float pivotX, float pivotY)
    {
        _ctx.TranslateCTM(pivotX, pivotY);
        _ctx.RotateCTM(degrees * (nfloat)Math.PI / 180);
        _ctx.TranslateCTM(-pivotX, -pivotY);
    }

    public void ClipRect(GRect rect) => _ctx.ClipToRect(ToCG(rect));

    public void DrawRect(GRect rect, in GPaint paint) => DrawShape(paint, rect, () => _ctx.AddRect(ToCG(rect)));

    public void DrawRoundRect(GRect rect, float radiusX, float radiusY, in GPaint paint)
    {
        var r = ToCG(rect);
        DrawShape(paint, rect, () =>
        {
            using var path = CGPath.FromRoundedRect(r, Math.Min(radiusX, (float)r.Width / 2), Math.Min(radiusY, (float)r.Height / 2));
            _ctx.AddPath(path);
        });
    }

    public void DrawOval(GRect rect, in GPaint paint) => DrawShape(paint, rect, () => _ctx.AddEllipseInRect(ToCG(rect)));

    public void DrawCircle(float centerX, float centerY, float radius, in GPaint paint) =>
        DrawOval(GRect.Create(centerX - radius, centerY - radius, radius * 2, radius * 2), paint);

    public void DrawLine(float x0, float y0, float x1, float y1, in GPaint paint)
    {
        var stroke = paint with { Style = PaintStyle.Stroke };
        DrawShape(stroke, new GRect(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1)), () =>
        {
            _ctx.MoveTo(x0, y0);
            _ctx.AddLineToPoint(x1, y1);
        });
    }

    public void DrawImage(IImage image, GRect dest)
    {
        if (image is not AppleImage apple) return;
        _ctx.SaveState();
        _ctx.InterpolationQuality = CGInterpolationQuality.Medium;
        // UIImage.Draw honours orientation and the flipped UIKit space; CGContext.DrawImage would not.
        UIGraphics.PushContext(_ctx);
        apple.Image.Draw(ToCG(dest));
        UIGraphics.PopContext();
        _ctx.RestoreState();
    }

    public void DrawText(string text, float x, float baselineY, GFont font, GColor color)
    {
        if (font is not AppleFont apple || string.IsNullOrEmpty(text)) return;
        using var line = apple.Line(text, color);
        _ctx.SaveState();
        // A flipped CTM would draw glyphs upside down; CoreText reads the text matrix, so flip it back.
        _ctx.TextMatrix = CGAffineTransform.MakeScale(1, -1);
        _ctx.TextPosition = new CGPoint(x, baselineY);
        line.Draw(_ctx);
        _ctx.RestoreState();
    }

    void DrawShape(in GPaint paint, GRect frame, Action addPath)
    {
        _ctx.SaveState();
        _ctx.SetShouldAntialias(paint.IsAntialias);
        if (paint.Shadow is { } shadow)
            _ctx.SetShadow(new CGSize(shadow.Dx, shadow.Dy), shadow.Radius, ToCG(shadow.Color));

        addPath();
        if (paint.Style == PaintStyle.Stroke)
        {
            _ctx.SetStrokeColor(ToCG(paint.Color));
            _ctx.SetLineWidth(Math.Max(paint.StrokeWidth, 1f));
            _ctx.SetLineCap(paint.StrokeCap switch
            {
                StrokeCap.Round => CGLineCap.Round,
                StrokeCap.Square => CGLineCap.Square,
                _ => CGLineCap.Butt,
            });
            _ctx.StrokePath();
        }
        else if (paint.Gradient is { } gradient)
        {
            _ctx.Clip();
            FillGradient(gradient);
        }
        else
        {
            _ctx.SetFillColor(ToCG(paint.Color));
            _ctx.FillPath();
        }
        _ctx.RestoreState();
    }

    void FillGradient(Gradient gradient)
    {
        var colors = new CGColor[gradient.Stops.Length];
        var locations = new nfloat[gradient.Stops.Length];
        for (var i = 0; i < gradient.Stops.Length; i++)
        {
            colors[i] = ToCG(gradient.Stops[i].Color);
            locations[i] = gradient.Stops[i].Location;
        }
        using var space = CGColorSpace.CreateSrgb()!;
        using var cg = new CGGradient(space, colors, locations);
        const CGGradientDrawingOptions extend = CGGradientDrawingOptions.DrawsBeforeStartLocation | CGGradientDrawingOptions.DrawsAfterEndLocation;
        if (gradient.Kind == GradientKind.Radial)
        {
            var c = new CGPoint(gradient.Center.X, gradient.Center.Y);
            _ctx.DrawRadialGradient(cg, c, 0, c, Math.Max(gradient.Radius, 0.01f), extend);
        }
        else
        {
            _ctx.DrawLinearGradient(cg, new CGPoint(gradient.Start.X, gradient.Start.Y), new CGPoint(gradient.End.X, gradient.End.Y), extend);
        }
    }

    static CGRect ToCG(GRect r) => new(r.Left, r.Top, r.Width, r.Height);

    internal static CGColor ToCG(GColor c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);
}

/// <summary>A decoded <see cref="UIImage"/> behind the engine's opaque <see cref="IImage"/>.</summary>
public sealed class AppleImage : IImage
{
    public AppleImage(UIImage image) => Image = image;

    public UIImage Image { get; }

    public int Width => (int)(Image.Size.Width * Image.CurrentScale);

    public int Height => (int)(Image.Size.Height * Image.CurrentScale);
}

/// <summary>Decodes PNG/JPEG bytes with <see cref="UIImage"/>.</summary>
public sealed class AppleImageDecoder : IImageDecoder
{
    public IImage? Decode(byte[] bytes) =>
        UIImage.LoadFromData(NSData.FromArray(bytes)) is { } image ? new AppleImage(image) : null;

    public IImage? DecodeFile(string path) =>
        UIImage.FromFile(path) is { } image ? new AppleImage(image) : null;
}

/// <summary>A system font at a concrete size, backed by a <see cref="CTFont"/>.</summary>
public sealed class AppleFont : GFont
{
    readonly CTFont _font;

    internal AppleFont(float size, bool bold)
    {
        var ui = UIFont.SystemFontOfSize(size, bold ? UIFontWeight.Semibold : UIFontWeight.Regular);
        _font = new CTFont(ui.Name, size);
        Size = size;
        // The engine's convention: Ascent is negative (above the baseline).
        Metrics = new FontMetrics(-(float)_font.AscentMetric, (float)_font.DescentMetric, (float)_font.LeadingMetric);
    }

    public override float Size { get; }

    public override FontMetrics Metrics { get; }

    internal CTLine Line(string text, GColor color)
    {
        var attributes = new CTStringAttributes { Font = _font, ForegroundColor = AppleCanvas.ToCG(color) };
        using var attributed = new NSAttributedString(text, attributes);
        return new CTLine(attributed);
    }

    internal float Measure(string text)
    {
        // Measured through the same CTLine the canvas draws, so per-run font fallback (emoji, CJK) is
        // accounted for identically — the IFontProvider contract.
        using var line = Line(text, Colors.Black);
        return (float)line.GetTypographicBounds();
    }
}

/// <summary>Supplies <see cref="AppleFont"/>s, cached per size/weight.</summary>
public sealed class AppleFontProvider : IFontProvider
{
    readonly Dictionary<(float, bool), AppleFont> _cache = new();

    public GFont Get(float size, bool bold)
    {
        if (_cache.TryGetValue((size, bold), out var cached)) return cached;
        var font = new AppleFont(size, bold);
        _cache[(size, bold)] = font;
        return font;
    }

    public float Measure(string text, GFont font) =>
        font is AppleFont apple ? apple.Measure(text) : 0;
}
