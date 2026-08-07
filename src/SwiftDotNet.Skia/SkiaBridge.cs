using SkiaSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// The SkiaSharp binding of the self-drawing engine.
/// </summary>
/// <remarks>
/// <para>All the behaviour lives in <see cref="VisualBridge"/>; this subclass pins the Skia rasterizer
/// (fonts, image decoding, canvas wrapping) and re-exposes the geometric entry points in SkiaSharp's value
/// types, so hosts and tests written against <c>SKPoint</c>/<c>SKRect</c>/<c>SKSize</c> keep working
/// unchanged.</para>
///
/// <para>New code can use the base class's <see cref="Graphics.Point"/>-typed members directly; these
/// overloads exist for continuity with the hosts that predate the renderer seam.</para>
/// </remarks>
public sealed class SkiaBridge : VisualBridge
{
    readonly SkiaFonts _fonts;

    public SkiaBridge() : this(new SkiaFonts(), new SkiaImages()) { }

    SkiaBridge(SkiaFonts fonts, SkiaImages images) : base(fonts, images) => _fonts = fonts;

    /// <summary>Wraps a Skia canvas so a custom renderer or host can draw through the engine's seam.</summary>
    public SkiaCanvas Wrap(SKCanvas canvas) => new(canvas, _fonts);

    /// <summary>Lay out then paint the current scene into <paramref name="canvas"/> filling <paramref name="size"/>.</summary>
    public void Paint(SKCanvas canvas, SKSize size, bool dark) =>
        Draw(new SkiaCanvas(canvas, _fonts), SkiaCanvas.FromSk(size), dark);

    // ---- SkiaSharp-typed overloads of the engine's pointer/gesture surface ----

    public bool DispatchPointer(SKPoint point) => DispatchPointer(SkiaCanvas.FromSk(point));

    public bool Scroll(SKPoint point, float dy) => Scroll(SkiaCanvas.FromSk(point), dy);

    public bool LongPress(SKPoint point) => LongPress(SkiaCanvas.FromSk(point));

    public bool Swipe(SKPoint point, string direction) => Swipe(SkiaCanvas.FromSk(point), direction);

    public bool Drag(SKPoint point, GesturePhase phase, float tx, float ty, float vx, float vy) =>
        Drag(SkiaCanvas.FromSk(point), phase, tx, ty, vx, vy);

    public bool Magnify(SKPoint point, GesturePhase phase, float scale) =>
        Magnify(SkiaCanvas.FromSk(point), phase, scale);

    public bool BeginScrub(SKPoint point) => BeginScrub(SkiaCanvas.FromSk(point));

    public void Scrub(SKPoint point) => Scrub(SkiaCanvas.FromSk(point));

    public bool BeginPan(SKPoint point) => BeginPan(SkiaCanvas.FromSk(point));

    /// <summary>The laid-out frame of a node by id, in canvas coordinates.</summary>
    public bool TryGetFrame(string id, out SKRect frame)
    {
        if (TryGetFrame(id, out Graphics.Rect r)) { frame = SkiaCanvas.ToSk(r); return true; }
        frame = default;
        return false;
    }

    /// <summary>Centre of a laid-out swatch in an open ColorPicker popover. For tests/tooling.</summary>
    public bool TryGetSwatchCenter(string id, int index, out SKPoint center)
    {
        if (TryGetSwatchCenter(id, index, out Graphics.Point p)) { center = SkiaCanvas.ToSk(p); return true; }
        center = default;
        return false;
    }
}

/// <summary>
/// The SkiaSharp-typed <see cref="PointerRouter"/>. Same state machine; the overloads exist so hosts and
/// tests can keep feeding <c>SKPoint</c>s.
/// </summary>
public sealed class SkiaPointerRouter : PointerRouter
{
    public SkiaPointerRouter(VisualBridge bridge) : base(bridge) { }

    public void Down(SKPoint p, double time) => Down(SkiaCanvas.FromSk(p), time);
    public void Move(SKPoint p, double time) => Move(SkiaCanvas.FromSk(p), time);
    public void Up(SKPoint p, double time) => Up(SkiaCanvas.FromSk(p), time);
    public void Pinch(SKPoint p, GesturePhase phase, float scale) => Pinch(SkiaCanvas.FromSk(p), phase, scale);
    public void PinchDelta(SKPoint p, float factor) => PinchDelta(SkiaCanvas.FromSk(p), factor);
    public void EndPinch(SKPoint p) => EndPinch(SkiaCanvas.FromSk(p));
}
