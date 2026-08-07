using System.Numerics;
using System.Runtime.InteropServices;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>What an instance draws. Kept in sync with <c>KIND_*</c> in the WGSL source.</summary>
enum InstanceKind
{
    FillRoundRect = 0,
    StrokeRoundRect = 1,
    FillEllipse = 2,
    StrokeEllipse = 3,
    Shadow = 4,
    Glyph = 5,
    Image = 6,
}

/// <summary>
/// One GPU instance — a quad plus everything the fragment shader needs to resolve it into a shape.
/// </summary>
/// <remarks>
/// Everything the engine draws is one of these, which is why the whole UI collapses into a single
/// instanced draw call per texture batch. Field packing is vec4-aligned to match WGSL's storage-buffer
/// layout rules; do not reorder without changing the shader struct.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
struct Instance
{
    /// <summary>Shape bounds in local (pre-transform) space: minX, minY, maxX, maxY.</summary>
    public Vector4 Bounds;

    /// <summary>x = corner radius, y = stroke width, z = shadow blur radius, w = <see cref="InstanceKind"/>.</summary>
    public Vector4 Shape;

    public Vector4 Color;

    /// <summary>Affine transform, row-major 2×2 part: a, b, c, d.</summary>
    public Vector4 Xform0;

    /// <summary>x, y = translation; z = first gradient stop index; w = stop count (0 = flat colour).</summary>
    public Vector4 Xform1;

    /// <summary>Device-space scissor: minX, minY, maxX, maxY.</summary>
    public Vector4 Clip;

    /// <summary>Atlas/image sub-rect: u0, v0, u1, v1.</summary>
    public Vector4 Uv;

    /// <summary>Linear: x0, y0, x1, y1. Radial: cx, cy, radius, 1.</summary>
    public Vector4 Gradient;
}

/// <summary>A gradient colour stop as the shader sees it.</summary>
[StructLayout(LayoutKind.Sequential)]
struct GpuStop
{
    public Vector4 Color;
    public float Location;
    public float Pad0, Pad1, Pad2;
}

/// <summary>A run of instances that share a bound image texture.</summary>
readonly record struct DrawBatch(int Start, int Count, WebGpuImage? Image);

/// <summary>
/// Records the engine's paint pass into GPU instance data. Nothing is rasterized here — this is a
/// recorder, and <see cref="WebGpuRenderer"/> turns the result into one draw call per batch.
/// </summary>
/// <remarks>
/// <para>Transforms are applied on the CPU by composing an affine matrix per instance, and shapes are
/// evaluated in local space by the fragment shader. That combination is what makes rotation exact:
/// rotating a rounded rectangle rotates its distance field rather than skewing a pre-rasterized bitmap.</para>
///
/// <para>Two deliberate approximations, both documented in <c>docs/backends/webgpu.md</c>: a clip is
/// tracked as a device-space axis-aligned box (the engine only ever clips scroll viewports, which are
/// unrotated), and <see cref="SaveLayer"/> multiplies alpha rather than compositing a real offscreen
/// layer.</para>
/// </remarks>
public sealed class WebGpuCanvas : ICanvas
{
    readonly List<Instance> _instances = new();
    readonly List<GpuStop> _stops = new();
    readonly List<DrawBatch> _batches = new();
    readonly WebGpuFonts _fonts;

    readonly record struct StateEntry(Matrix3x2 Ctm, Vector4 Clip, float Opacity);
    readonly List<StateEntry> _stack = new();

    Matrix3x2 _ctm = Matrix3x2.Identity;
    Vector4 _clip;
    float _opacity = 1;

    WebGpuImage? _batchImage;
    int _batchStart;

    /// <summary>
    /// Starts a recording for a surface of <paramref name="surface"/> logical pixels. The fonts must be the
    /// same provider the bridge measured with, or glyph quads will not match the layout.
    /// </summary>
    public WebGpuCanvas(WebGpuFonts fonts, Graphics.Size surface)
    {
        _fonts = fonts;
        _clip = new Vector4(0, 0, surface.Width, surface.Height);
        Surface = surface;
    }

    internal WebGpuFonts Fonts => _fonts;
    internal Graphics.Size Surface { get; }
    internal Graphics.Color ClearColor { get; private set; } = Colors.Black;

    internal IReadOnlyList<Instance> Instances => _instances;
    internal IReadOnlyList<GpuStop> Stops => _stops;

    /// <summary>Closes the in-flight batch and returns every batch to draw, in submission order.</summary>
    internal IReadOnlyList<DrawBatch> Finish()
    {
        FlushBatch();
        return _batches;
    }

    public void Clear(Graphics.Color color) => ClearColor = color;

    // ---- state stack ---------------------------------------------------------

    public int Save()
    {
        _stack.Add(new StateEntry(_ctm, _clip, _opacity));
        return _stack.Count;
    }

    public void RestoreToCount(int count)
    {
        if (count < 0 || count > _stack.Count) return;
        if (count == _stack.Count && count > 0)
        {
            // Restoring to the depth Save() returned means undoing that Save.
            var top = _stack[count - 1];
            _ctm = top.Ctm;
            _clip = top.Clip;
            _opacity = top.Opacity;
            _stack.RemoveRange(count - 1, _stack.Count - (count - 1));
            return;
        }

        if (count == 0) { _stack.Clear(); _ctm = Matrix3x2.Identity; _opacity = 1; return; }

        var entry = _stack[count - 1];
        _ctm = entry.Ctm;
        _clip = entry.Clip;
        _opacity = entry.Opacity;
        _stack.RemoveRange(count - 1, _stack.Count - (count - 1));
    }

    /// <summary>
    /// Approximates a compositing layer by scaling the alpha of everything drawn until the matching
    /// restore. Correct for the common case of a faded subtree whose children do not overlap; where they
    /// do, they show through one another instead of fading as one composite.
    /// </summary>
    public void SaveLayer(float opacity) => _opacity *= Math.Clamp(opacity, 0, 1);

    public void Translate(float dx, float dy) => _ctm = Matrix3x2.CreateTranslation(dx, dy) * _ctm;

    public void Scale(float sx, float sy) => _ctm = Matrix3x2.CreateScale(sx, sy) * _ctm;

    public void RotateDegrees(float degrees, float pivotX, float pivotY) =>
        _ctm = Matrix3x2.CreateRotation(degrees * MathF.PI / 180f, new Vector2(pivotX, pivotY)) * _ctm;

    public void ClipRect(Graphics.Rect rect)
    {
        var box = DeviceBounds(rect);
        _clip = new Vector4(
            Math.Max(_clip.X, box.X),
            Math.Max(_clip.Y, box.Y),
            Math.Min(_clip.Z, box.Z),
            Math.Min(_clip.W, box.W));
    }

    // ---- primitives ----------------------------------------------------------

    public void DrawRect(Graphics.Rect rect, in Graphics.Paint paint) => DrawRoundRect(rect, 0, 0, paint);

    public void DrawRoundRect(Graphics.Rect rect, float radiusX, float radiusY, in Graphics.Paint paint)
    {
        var radius = Math.Min(
            Math.Min(radiusX, radiusY) <= 0 ? Math.Max(radiusX, radiusY) : Math.Min(radiusX, radiusY),
            Math.Min(rect.Width, rect.Height) / 2f);

        if (paint.Shadow is { } shadow) Emit(rect, radius, InstanceKind.Shadow, paint, shadow);

        Emit(rect, radius,
            paint.Style == PaintStyle.Stroke ? InstanceKind.StrokeRoundRect : InstanceKind.FillRoundRect,
            paint);
    }

    public void DrawOval(Graphics.Rect rect, in Graphics.Paint paint)
    {
        if (paint.Shadow is { } shadow) Emit(rect, 0, InstanceKind.Shadow, paint, shadow);
        Emit(rect, 0, paint.Style == PaintStyle.Stroke ? InstanceKind.StrokeEllipse : InstanceKind.FillEllipse, paint);
    }

    public void DrawCircle(float centerX, float centerY, float radius, in Graphics.Paint paint) =>
        DrawOval(new Graphics.Rect(centerX - radius, centerY - radius, centerX + radius, centerY + radius), paint);

    /// <summary>
    /// Draws a stroked segment as a capsule in a rotated local frame — no dedicated shader path. The
    /// existing rounded-rect distance field already is a capsule when its radius is half its height, and
    /// routing through the transform keeps caps and antialiasing consistent with every other shape.
    /// </summary>
    public void DrawLine(float x0, float y0, float x1, float y1, in Graphics.Paint paint)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length <= 0) return;

        var width = Math.Max(paint.StrokeWidth, 0.1f);
        var half = width / 2f;

        // Local frame: the segment runs along +X from the origin, centred vertically.
        var local = new Graphics.Rect(0, -half, length, half);
        var radius = paint.StrokeCap == Graphics.StrokeCap.Round ? half : 0;

        var saved = _ctm;
        _ctm = Matrix3x2.CreateRotation(MathF.Atan2(dy, dx)) * Matrix3x2.CreateTranslation(x0, y0) * _ctm;
        Emit(local, radius, InstanceKind.FillRoundRect, paint with { Style = PaintStyle.Fill, Gradient = null });
        _ctm = saved;
    }

    public void DrawImage(IImage image, Graphics.Rect dest)
    {
        if (image is not WebGpuImage tex) return;
        if (!ReferenceEquals(tex, _batchImage)) { FlushBatch(); _batchImage = tex; }

        Emit(dest, 0, InstanceKind.Image, Graphics.Paint.Fill(Colors.White),
            uv: new Vector4(0, 0, 1, 1));
    }

    public void DrawText(string text, float x, float baselineY, Graphics.Font font, Graphics.Color color)
    {
        if (string.IsNullOrEmpty(text)) return;

        foreach (var glyph in _fonts.LayoutRun(text, font, x, baselineY))
            Emit(glyph.Bounds, 0, InstanceKind.Glyph, Graphics.Paint.Fill(color), uv: glyph.Uv);
    }

    // ---- recording -----------------------------------------------------------

    void Emit(Graphics.Rect rect, float radius, InstanceKind kind, in Graphics.Paint paint,
        Graphics.Shadow? shadow = null, Vector4 uv = default)
    {
        var color = shadow?.Color ?? paint.Color;
        var (r, g, b, a) = color.ToFloats();

        var stopIndex = 0;
        var stopCount = 0;
        var gradient = Vector4.Zero;

        // A shadow uses its own colour, never the shape's gradient.
        if (shadow is null && paint.Gradient is { } grad)
        {
            stopIndex = _stops.Count;
            foreach (var stop in grad.Stops)
            {
                var (sr, sg, sb, sa) = stop.Color.ToFloats();
                _stops.Add(new GpuStop { Color = new Vector4(sr, sg, sb, sa), Location = stop.Location });
            }

            // The sign of the stop count carries the gradient kind. A dedicated field would be cleaner, but
            // every vec4 slot is spoken for and the linear form's w component holds a real coordinate — so
            // there is no spare value to overload there.
            var isRadial = grad.Kind == GradientKind.Radial;
            stopCount = isRadial ? -grad.Stops.Length : grad.Stops.Length;

            gradient = isRadial
                ? new Vector4(grad.Center.X, grad.Center.Y, grad.Radius, 0)
                : new Vector4(grad.Start.X, grad.Start.Y, grad.End.X, grad.End.Y);
        }

        // A shadow is drawn as the shape grown by its blur, so the falloff has room to fade out.
        var bounds = shadow is { } s
            ? new Graphics.Rect(rect.Left + s.Dx, rect.Top + s.Dy, rect.Right + s.Dx, rect.Bottom + s.Dy)
            : rect;

        _instances.Add(new Instance
        {
            Bounds = new Vector4(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            Shape = new Vector4(radius, paint.StrokeWidth, shadow?.Radius ?? 0, (float)kind),
            Color = new Vector4(r, g, b, a * _opacity),
            Xform0 = new Vector4(_ctm.M11, _ctm.M12, _ctm.M21, _ctm.M22),
            Xform1 = new Vector4(_ctm.M31, _ctm.M32, stopIndex, stopCount),
            Clip = _clip,
            Uv = uv,
            Gradient = gradient,
        });
    }

    void FlushBatch()
    {
        var count = _instances.Count - _batchStart;
        if (count > 0) _batches.Add(new DrawBatch(_batchStart, count, _batchImage));
        _batchStart = _instances.Count;
    }

    /// <summary>The device-space axis-aligned bounds of a local-space rect under the current transform.</summary>
    Vector4 DeviceBounds(Graphics.Rect rect)
    {
        Span<Vector2> corners =
        [
            Vector2.Transform(new Vector2(rect.Left, rect.Top), _ctm),
            Vector2.Transform(new Vector2(rect.Right, rect.Top), _ctm),
            Vector2.Transform(new Vector2(rect.Right, rect.Bottom), _ctm),
            Vector2.Transform(new Vector2(rect.Left, rect.Bottom), _ctm),
        ];

        var min = corners[0];
        var max = corners[0];
        for (var i = 1; i < corners.Length; i++)
        {
            min = Vector2.Min(min, corners[i]);
            max = Vector2.Max(max, corners[i]);
        }
        return new Vector4(min.X, min.Y, max.X, max.Y);
    }
}
