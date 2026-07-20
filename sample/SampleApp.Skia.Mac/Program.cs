using AppKit;
using CoreGraphics;
using Foundation;
using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;

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
        SkiaSampleRenderers.RegisterAll();   // Map + CameraView renderers (registry seam)
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
        var swiftApp = SwiftProgram.CreateSwiftApp();
        var view = new SkiaMacView(rect, swiftApp.CreateRoot(), swiftApp.Services);
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

    public SkiaMacView(CGRect frame, View root, IServiceProvider? services = null) : base(frame)
    {
        _bridge.Invalidate += () => NeedsDisplay = true;
        SwiftApp.Run(root, _bridge, services);
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
