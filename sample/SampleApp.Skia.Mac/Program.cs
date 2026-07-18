using AppKit;
using CoreGraphics;
using Foundation;
using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace SampleApp.Skia.Mac;

/// <summary>
/// Interactive macOS host for the SkiaSharp self-drawing backend. There is no off-the-shelf AppKit
/// SkiaSharp view, so this renders the scene to an off-screen <see cref="SKSurface"/> each frame and
/// blits it into an <see cref="NSView"/>, translating mouse / scroll / keyboard into the bridge. A
/// timer drives the implicit-animation clock. Run: <c>dotnet build -t:Run</c> (a real GUI session).
/// </summary>
static class Program
{
    static void Main(string[] args)
    {
        NSApplication.Init();
        SkiaRenderers.Register("Map", new MapRenderer()); // custom Map renderer (registry seam)
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        app.Delegate = new AppDelegate();
        app.Run();
    }
}

sealed class AppDelegate : NSApplicationDelegate
{
    NSWindow? _window;

    public override void DidFinishLaunching(NSNotification notification)
    {
        var rect = new CGRect(0, 0, 440, 820);
        _window = new NSWindow(rect,
            NSWindowStyle.Titled | NSWindowStyle.Closable | NSWindowStyle.Resizable | NSWindowStyle.Miniaturizable,
            NSBackingStore.Buffered, false)
        {
            Title = "SwiftDotNet · Skia",
        };
        var view = new SkiaMacView(rect, new ContentView());
        _window.ContentView = view;
        _window.MakeKeyAndOrderFront(null);
        _window.MakeFirstResponder(view);
        NSApplication.SharedApplication.ActivateIgnoringOtherApps(true);
    }

    public override bool ApplicationShouldTerminateAfterLastWindowClosed(NSApplication sender) => true;
}

/// <summary>An NSView that paints the SwiftDotNet/Skia scene and forwards pointer + keyboard events.</summary>
sealed class SkiaMacView : NSView
{
    readonly SkiaBridge _bridge = new();
    NSTimer? _timer;

    public SkiaMacView(CGRect frame, View root) : base(frame)
    {
        _bridge.Invalidate += () => NeedsDisplay = true;
        SwiftApp.Run(root, _bridge);
        // ~60fps clock for implicit animations; only repaints while something is animating.
        _timer = NSTimer.CreateRepeatingScheduledTimer(1.0 / 60, _ => _bridge.Tick(1.0 / 60));
    }

    public override bool IsFlipped => true;                 // top-left origin, matching the canvas
    public override bool AcceptsFirstResponder() => true;

    public override void DrawRect(CGRect dirtyRect)
    {
        var scale = (float)(Window?.BackingScaleFactor ?? 1);
        var wPt = (float)Bounds.Width;
        var hPt = (float)Bounds.Height;
        var pw = Math.Max(1, (int)(wPt * scale));
        var ph = Math.Max(1, (int)(hPt * scale));

        var info = new SKImageInfo(pw, ph, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Scale(scale);
        _bridge.Paint(surface.Canvas, new SKSize(wPt, hPt), Dark);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var nsData = NSData.FromArray(data.ToArray());
        using var nsImage = new NSImage(nsData);
        nsImage.Draw(Bounds, new CGRect(0, 0, pw, ph), NSCompositingOperation.Copy, 1.0f);
    }

    static bool Dark => NSApplication.SharedApplication.EffectiveAppearance.Name?.ToString().Contains("Dark") ?? false;

    SKPoint Point(NSEvent e)
    {
        var p = ConvertPointFromView(e.LocationInWindow, null);
        return new SKPoint((float)p.X, (float)p.Y);
    }

    public override void MouseDown(NSEvent theEvent) { _bridge.DispatchPointer(Point(theEvent)); }
    public override void MouseDragged(NSEvent theEvent) { _bridge.DispatchPointer(Point(theEvent)); } // slider drag
    public override void ScrollWheel(NSEvent theEvent) { _bridge.Scroll(Point(theEvent), -(float)theEvent.ScrollingDeltaY); }

    public override void KeyDown(NSEvent theEvent)
    {
        var chars = theEvent.Characters ?? "";
        if (chars.Length == 1 && (chars[0] == (char)127 || chars[0] == (char)8))
            _bridge.DeleteBackward();
        else if (!string.IsNullOrEmpty(chars) && !char.IsControl(chars[0]))
            _bridge.InsertText(chars);
    }
}

/// <summary>Custom Skia renderer for the Map CustomView — stylized map instead of the ⚠️ placeholder.</summary>
sealed class MapRenderer : ISkiaRenderer
{
    public SKSize Measure(SkiaRenderContext ctx, SKSize available) => available;

    public void Paint(SkiaRenderContext ctx, SKCanvas c, SKRect r)
    {
        using var bg = new SKPaint { Color = new SKColor(0xDD, 0xEC, 0xE0), IsAntialias = true };
        c.DrawRoundRect(r, 10, 10, bg);
        var save = c.Save();
        c.ClipRoundRect(new SKRoundRect(r, 10));
        using var grid = new SKPaint { Color = new SKColor(0xB4, 0xCC, 0xBC), StrokeWidth = 1 };
        for (var x = r.Left; x < r.Right; x += 40) c.DrawLine(x, r.Top, x, r.Bottom, grid);
        for (var y = r.Top; y < r.Bottom; y += 40) c.DrawLine(r.Left, y, r.Right, y, grid);
        using var pin = new SKPaint { Color = new SKColor(0xFF, 0x3B, 0x30), IsAntialias = true };
        c.DrawCircle(r.MidX, r.MidY, 9, pin);
        c.RestoreToCount(save);
    }
}
