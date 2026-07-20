using SkiaSharp;

namespace SwiftDotNet.Sample.Skia;

/// <summary>
/// Registers the Skia renderers for the sample's custom native primitives. Every Skia head calls this
/// once at startup, so the headless harness, the AppKit window and the Silk/GLFW window all draw the
/// same thing.
///
/// This is the <see cref="SkiaRenderers"/> registry seam: a <c>CustomView</c> whose type has no
/// registered renderer paints the "⚠️ unknown view" placeholder; registering one makes it draw itself.
/// </summary>
public static class SkiaSampleRenderers
{
    public static void RegisterAll()
    {
        SkiaRenderers.Register("Map", new MapRenderer());
        SkiaRenderers.Register("CameraView", new CameraPlaceholderRenderer());
    }
}

/// <summary>A custom Skia renderer for the Map CustomView: draws a stylized map with a grid and a pin.</summary>
public sealed class MapRenderer : ISkiaRenderer
{
    public SKSize Measure(SkiaRenderContext ctx, SKSize available) => available; // greedy fill

    public void Paint(SkiaRenderContext ctx, SKCanvas c, SKRect r)
    {
        using var bg = new SKPaint { Color = new SKColor(0xDD, 0xEC, 0xE0), IsAntialias = true };
        c.DrawRoundRect(r, 10, 10, bg);
        var save = c.Save();
        c.ClipRoundRect(new SKRoundRect(r, 10));
        using var grid = new SKPaint { Color = new SKColor(0xB4, 0xCC, 0xBC), StrokeWidth = 1 };
        for (var x = r.Left; x < r.Right; x += 40) c.DrawLine(x, r.Top, x, r.Bottom, grid);
        for (var y = r.Top; y < r.Bottom; y += 40) c.DrawLine(r.Left, y, r.Right, y, grid);
        using var road = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), StrokeWidth = 8, IsAntialias = true };
        c.DrawLine(r.Left, r.MidY + 30, r.Right, r.MidY - 40, road);
        using var pin = new SKPaint { Color = new SKColor(0xFF, 0x3B, 0x30), IsAntialias = true };
        c.DrawCircle(r.MidX, r.MidY, 9, pin);
        c.RestoreToCount(save);
        using var f = new SKFont(SKTypeface.Default, 13);
        using var label = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        c.DrawText("Custom SkiaRenderer ✓  (registry seam)", r.Left + 12, r.Top + 24, f, label);
    }
}

/// <summary>
/// A viewfinder-shaped placeholder for <c>CameraView</c>.
///
/// <b>It deliberately does not fake a camera feed.</b> Skia is a self-drawing canvas with no capture
/// stack, so there is no preview to show; a live preview needs a backend with a native camera renderer
/// (AVFoundation on Apple, CameraX on Android). Without this the control paints the generic
/// "⚠️ unknown view" box, which reads as a bug rather than an unsupported capability — so this draws an
/// explicit, honestly-labelled stand-in that still occupies the control's real layout box.
/// </summary>
public sealed class CameraPlaceholderRenderer : ISkiaRenderer
{
    public SKSize Measure(SkiaRenderContext ctx, SKSize available) => available; // greedy fill, like a real preview

    public void Paint(SkiaRenderContext ctx, SKCanvas c, SKRect r)
    {
        using var bg = new SKPaint { Color = new SKColor(0x1C, 0x1C, 0x1E), IsAntialias = true };
        c.DrawRoundRect(r, 12, 12, bg);

        // Viewfinder corner brackets.
        using var bracket = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0xFF, 0x8C),
            StrokeWidth = 3,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeCap = SKStrokeCap.Round,
        };
        var inset = 18f;
        var len = Math.Min(34f, Math.Min(r.Width, r.Height) / 4);
        var (l, t, rt, b) = (r.Left + inset, r.Top + inset, r.Right - inset, r.Bottom - inset);
        c.DrawLine(l, t, l + len, t, bracket); c.DrawLine(l, t, l, t + len, bracket);
        c.DrawLine(rt, t, rt - len, t, bracket); c.DrawLine(rt, t, rt, t + len, bracket);
        c.DrawLine(l, b, l + len, b, bracket); c.DrawLine(l, b, l, b - len, bracket);
        c.DrawLine(rt, b, rt - len, b, bracket); c.DrawLine(rt, b, rt, b - len, bracket);

        // Shutter ring, for the viewfinder read.
        using var ring = new SKPaint
        {
            Color = new SKColor(0xFF, 0xFF, 0xFF, 0x66),
            StrokeWidth = 2,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
        };
        c.DrawCircle(r.MidX, r.MidY - 6, 26, ring);

        using var title = new SKFont(SKTypeface.Default, 15);
        using var sub = new SKFont(SKTypeface.Default, 12);
        using var ink = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0xE0), IsAntialias = true };
        using var dim = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x99), IsAntialias = true };

        // No emoji here: SKTypeface.Default has no emoji coverage, so one would paint as tofu.
        Centered("Camera preview", title, ink, r.MidY + 44);
        Centered($"no capture stack on Skia · facing: {Facing(ctx)}", sub, dim, r.MidY + 64);

        void Centered(string text, SKFont font, SKPaint paint, float y)
        {
            var w = font.MeasureText(text);
            c.DrawText(text, r.MidX - w / 2, y, font, paint);
        }
    }

    /// <summary>Echo the bound prop, so the control's state is visibly live even without a feed.</summary>
    static string Facing(SkiaRenderContext ctx) => ctx.String("facing") is { Length: > 0 } f ? f : "back";
}
