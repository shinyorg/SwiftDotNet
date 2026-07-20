using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;

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
SkiaPointerRouter? pointer = null;
var mousePos = new SKPoint(0, 0);
var clock = 0.0;                 // seconds since start; the router's long-press timer runs off it
var uiPump = new RenderLoopSyncContext();

window.Load += () =>
{
    gl = window.CreateOpenGL();
    using var glInterface = GRGlInterface.Create();
    grContext = GRContext.CreateGl(glInterface);

    // Silk/GLFW has no SynchronizationContext of its own, so install one that queues onto the render
    // loop *before* SwiftApp.Run captures it. Without this, anything that mutates State off-thread —
    // a timer, a socket, or the hot-reload agent applying an edit — would rebuild the scene tree
    // concurrently with the paint below. See SwiftApp.RequestRender / SwiftApp.Invalidate.
    SynchronizationContext.SetSynchronizationContext(uiPump);

    SkiaSampleRenderers.RegisterAll();
    bridge = new SkiaBridge();
    pointer = new SkiaPointerRouter(bridge);
    var swiftApp = SwiftProgram.CreateSwiftApp();
    SwiftApp.Run(swiftApp.CreateRoot(), bridge, swiftApp.Services);

    var input = window.CreateInput();
    foreach (var mouse in input.Mice)
    {
        // Raw down/move/up into the router, which resolves tap / long-press / swipe / continuous drag.
        // Feeding Click alone (as this used to) means .OnDrag and .OnMagnify never fire.
        mouse.MouseMove += (_, p) => { mousePos = new SKPoint(p.X, p.Y); pointer!.Move(mousePos, clock); };
        mouse.MouseDown += (_, btn) => { if (btn == MouseButton.Left) pointer!.Down(mousePos, clock); };
        mouse.MouseUp += (_, btn) => { if (btn == MouseButton.Left) pointer!.Up(mousePos, clock); };
        mouse.Scroll += (_, wheel) =>
        {
            // GLFW has no pinch event; ctrl+wheel is the conventional desktop zoom.
            if (input.Keyboards.Any(k => k.IsKeyPressed(Key.ControlLeft) || k.IsKeyPressed(Key.ControlRight)))
                pointer!.PinchDelta(mousePos, 1 + wheel.Y * 0.05f);
            else
            {
                pointer!.EndPinch(mousePos);
                bridge!.Scroll(mousePos, -wheel.Y * 40);
            }
        };
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
    uiPump.Drain();   // apply any renders posted from another thread, before we paint
    bridge.Tick(dt);
    clock += dt;
    pointer?.Poll(clock);   // resolves a held press into a long-press

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

// Hot reload: `dotnet watch run --project sample/SampleApp.Skia.Silk`, then edit any Body in SharedUI and
// save — the window redraws in place, keeping the page you pushed and everything you typed. The runtime
// calls SwiftDotNet's MetadataUpdateHandler, which re-renders; this is only here to print proof.
HotReload.Reloaded += types =>
    Console.WriteLine($"[hot reload] applied ({types?.Length ?? 0} updated types) — re-rendered");

window.Closing += () => { grContext?.Dispose(); };
window.Run();

/// <summary>
/// Queues posted callbacks and runs them on the render-loop thread. The minimum a windowing host needs
/// to give <see cref="SwiftApp"/> a UI thread to marshal onto.
/// </summary>
file sealed class RenderLoopSyncContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue) _queue.Enqueue((d, state));
    }

    /// <summary>Run everything queued so far. Call once per frame, before painting.</summary>
    public void Drain()
    {
        while (true)
        {
            (SendOrPostCallback cb, object? state) item;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                item = _queue.Dequeue();
            }
            item.cb(item.state);
        }
    }
}

/// <summary>Custom Skia renderer for the Map CustomView — stylized map instead of the ⚠️ placeholder.</summary>
