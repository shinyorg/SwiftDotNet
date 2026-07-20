using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;

// Dependency-free desktop host for the Skia self-drawing backend: a Silk.NET (GLFW) window with a GL
// context, onto which SkiaSharp draws via a GL-backed SKSurface. No GTK/WinUI/AppKit — one binary runs on
// Windows, macOS and Linux, and is the base for embedded/framebuffer Linux. Input (mouse/scroll/keyboard)
// is fed straight into the bridge; the continuous render loop drives the animation clock.

const int WinW = 440, WinH = 820;

var options = WindowOptions.Default with
{
    Size = new Vector2D<int>(WinW, WinH),
    Title = "SwiftDotNet · Skia (Silk.NET)",
    API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new APIVersion(3, 3)),
    PreferredDepthBufferBits = 0,
    PreferredStencilBufferBits = 8,
};

var window = Window.Create(options);
GL? gl = null;
GRContext? grContext = null;
SkiaBridge? bridge = null;
var mousePos = new SKPoint(0, 0);

window.Load += () =>
{
    gl = window.CreateOpenGL();
    using var glInterface = GRGlInterface.Create();
    grContext = GRContext.CreateGl(glInterface);

    SkiaRenderers.Register("Map", new MapRenderer());
    bridge = new SkiaBridge();
    var swiftApp = SwiftProgram.CreateSwiftApp();
    SwiftApp.Run(swiftApp.CreateRoot(), bridge, swiftApp.Services);

    var input = window.CreateInput();
    foreach (var mouse in input.Mice)
    {
        mouse.MouseMove += (_, p) => mousePos = new SKPoint(p.X, p.Y);
        mouse.Click += (_, _, p) => bridge!.DispatchPointer(new SKPoint(p.X, p.Y));
        mouse.Scroll += (_, wheel) => bridge!.Scroll(mousePos, -wheel.Y * 40);
    }
    foreach (var keyboard in input.Keyboards)
    {
        keyboard.KeyChar += (_, ch) => { if (!char.IsControl(ch)) bridge!.InsertText(ch.ToString()); };
        keyboard.KeyDown += (_, key, _) => { if (key is Key.Backspace) bridge!.DeleteBackward(); };
    }
};

window.Render += dt =>
{
    if (gl is null || grContext is null || bridge is null) return;
    bridge.Tick(dt);

    var fb = window.FramebufferSize;
    var scale = fb.X / (float)Math.Max(1, window.Size.X); // HiDPI: pixels ÷ points

    var glInfo = new GRGlFramebufferInfo(0, 0x8058u); // FBO 0, GL_RGBA8
    using var target = new GRBackendRenderTarget(fb.X, fb.Y, 0, 8, glInfo);
    using var surface = SKSurface.Create(grContext, target, GRSurfaceOrigin.BottomLeft, SKColorType.Rgba8888);

    var canvas = surface.Canvas;
    canvas.Scale(scale);
    bridge.Paint(canvas, new SKSize(window.Size.X, window.Size.Y), dark: false);
    canvas.Flush();
    grContext.Flush();
};

window.Closing += () => { grContext?.Dispose(); };
window.Run();

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
