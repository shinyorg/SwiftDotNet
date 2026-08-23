using Godot;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Adapts Godot's own 2D renderer to the engine's <see cref="ICanvas"/> seam — no SkiaSharp, no native
/// library, no pixel buffer. The UI is drawn by the same renderer that draws the game.
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The obvious way to host the engine in Godot is the way it is hosted in
/// Unity: let Skia paint into a texture and blit it. That works (see
/// <c>SwiftDotNet.Godot.Skia</c>), but it costs a native dependency on every export target and a
/// full-surface upload per repaint. Godot's canvas API covers the entire closed vocabulary of
/// <see cref="ICanvas"/>, so the UI can instead be real Godot draw commands.</para>
///
/// <para><b>The one structural mismatch.</b> <see cref="ICanvas"/> is immediate-mode with a save/restore
/// stack; Godot's canvas is <em>retained</em> — clipping and group opacity are properties of a canvas
/// <em>item</em>, not of a draw call. So each frame builds a small tree of canvas items under the host
/// control:</para>
/// <list type="bullet">
///   <item><description><see cref="ClipRect"/> pushes a child item with
///   <c>canvas_item_set_clip</c> + a custom rect. Godot intersects a clip with its ancestors', so nested
///   clips (a list inside a scroll view) compose correctly.</description></item>
///   <item><description><see cref="SaveLayer"/> pushes a child item in
///   <see cref="RenderingServer.CanvasGroupMode.Transparent"/> with a modulate alpha. That composites the
///   group off-screen and fades the result — <em>not</em> the same as fading each child, which is the whole
///   reason the seam has a layer call.</description></item>
///   <item><description>Because a Godot item always draws its own commands <em>before</em> its children,
///   a group never carries commands of its own: every primitive goes into a leaf child, and a fresh leaf is
///   allocated whenever the group changes. Draw indices are assigned in issue order, so painter's order is
///   preserved across clips and layers.</description></item>
/// </list>
///
/// <para>Items are pooled across frames — a repaint clears and reuses them rather than churning RIDs.</para>
/// </remarks>
public sealed class GodotCanvas : ICanvas
{
    readonly GodotFonts _fonts;
    readonly List<Rid> _pool = [];
    readonly List<StyleBoxFlat> _boxes = [];
    readonly List<GradientTexture2D> _gradients = [];
    readonly Stack<State> _stack = new();
    readonly Dictionary<ulong, int> _lastChild = new();

    Rid _root;
    State _current;
    int _used;
    int _boxesUsed;
    int _gradientsUsed;
    int _order;
    int _depth;
    int _lastOrder;
    Transform2D? _leafTransform;

    public GodotCanvas(GodotFonts fonts) => _fonts = fonts;

    /// <param name="LeafOrder">
    /// The draw index <see cref="Leaf"/> was created with. A restore can only reuse a saved leaf while it
    /// is still the newest child of its group — see <see cref="RestoreToCount"/>.
    /// </param>
    readonly record struct State(Transform2D Transform, Rid Group, Rid Leaf, int LeafOrder);

    /// <summary>
    /// Begins a frame against a host canvas item (a <c>Control</c>'s own RID). Clears everything the
    /// previous frame drew.
    /// </summary>
    public void Begin(Rid root)
    {
        _root = root;
        _used = 0;
        _boxesUsed = 0;
        _gradientsUsed = 0;
        _order = 0;
        _depth = 1;
        _stack.Clear();
        _lastChild.Clear();
        _current = new State(Transform2D.Identity, root, default, -1);
        _leafTransform = null;
        RenderingServer.CanvasItemClear(root);
    }

    /// <summary>Ends a frame. Pooled items the frame did not use are hidden rather than freed.</summary>
    public void End()
    {
        for (var i = _used; i < _pool.Count; i++) RenderingServer.CanvasItemSetVisible(_pool[i], false);
    }

    /// <summary>Frees every pooled canvas item. Call from the host's <c>_ExitTree</c>.</summary>
    public void Dispose()
    {
        foreach (var rid in _pool) RenderingServer.FreeRid(rid);
        _pool.Clear();
        _boxes.Clear();
        _gradients.Clear();
    }

    // ---- item pool -----------------------------------------------------------

    Rid Acquire(Rid parent)
    {
        Rid rid;
        if (_used < _pool.Count)
        {
            rid = _pool[_used];
            RenderingServer.CanvasItemClear(rid);

            // A reused item carries last frame's role. Reset every property this class ever sets, or a
            // leaf that was a clip group two frames ago silently keeps clipping.
            RenderingServer.CanvasItemSetVisible(rid, true);
            RenderingServer.CanvasItemSetClip(rid, false);
            RenderingServer.CanvasItemSetCustomRect(rid, false);
            RenderingServer.CanvasItemSetModulate(rid, Godot.Colors.White);
            RenderingServer.CanvasItemSetCanvasGroupMode(rid, RenderingServer.CanvasGroupMode.Disabled);
        }
        else
        {
            rid = RenderingServer.CanvasItemCreate();
            _pool.Add(rid);
            RenderingServer.CanvasItemSetDefaultTextureFilter(rid, RenderingServer.CanvasItemTextureFilter.LinearWithMipmaps);
        }

        _used++;
        _lastOrder = _order++;
        RenderingServer.CanvasItemSetParent(rid, parent);
        RenderingServer.CanvasItemSetDrawIndex(rid, _lastOrder);
        _lastChild[parent.Id] = _lastOrder;
        return rid;
    }

    /// <summary>The item commands go into, allocated lazily so a Save that draws nothing costs nothing.</summary>
    Rid Leaf()
    {
        if (_current.Leaf.IsValid) return _current.Leaf;
        var leaf = Acquire(_current.Group);
        _current = _current with { Leaf = leaf, LeafOrder = _lastOrder };
        _leafTransform = null;
        return leaf;
    }

    /// <summary>
    /// Emits the current transform into the leaf's command stream when it has changed. Godot's
    /// <c>add_set_transform</c> applies to every subsequent command in that item, so this is per-change
    /// rather than per-primitive.
    /// </summary>
    Rid Target()
    {
        var leaf = Leaf();
        if (_leafTransform is { } last && last.IsEqualApprox(_current.Transform)) return leaf;
        RenderingServer.CanvasItemAddSetTransform(leaf, _current.Transform);
        _leafTransform = _current.Transform;
        return leaf;
    }

    // ---- state ---------------------------------------------------------------

    public void Clear(Graphics.Color color)
    {
        // There is no "clear" on a canvas item — the host's own background does that. A transparent clear
        // (the HUD case) must draw nothing at all, or it would punch a hole in the scene behind it.
        if (color.A == 0) return;
        RenderingServer.CanvasItemAddRect(Target(), new Rect2(-1e5f, -1e5f, 2e5f, 2e5f), ToGd(color));
    }

    public int Save()
    {
        _stack.Push(_current);
        return _depth++;
    }

    public void RestoreToCount(int count)
    {
        while (_depth > count && _stack.Count > 0)
        {
            var group = _current.Group;
            _current = _stack.Pop();
            _depth--;

            // A saved leaf goes stale the moment anything else is added to its group: Godot sorts a
            // group's children by draw index, so drawing into an older leaf would paint *underneath* the
            // clip or layer that was pushed after it. This is not hypothetical — it is what put a pushed
            // navigation page's background behind the list it was supposed to cover.
            if (_current.Leaf.IsValid &&
                _lastChild.TryGetValue(_current.Group.Id, out var newest) && newest > _current.LeafOrder)
                _current = _current with { Leaf = default };
        }
        _leafTransform = null;
    }

    public void SaveLayer(float opacity)
    {
        Save();
        var layer = Acquire(_current.Group);

        // Transparent group mode renders the subtree to its own buffer first. Without it, modulate would
        // fade each child independently and overlapping children would show through one another.
        RenderingServer.CanvasItemSetCanvasGroupMode(layer, RenderingServer.CanvasGroupMode.Transparent);
        RenderingServer.CanvasItemSetModulate(layer, new Godot.Color(1, 1, 1, Math.Clamp(opacity, 0, 1)));
        _current = _current with { Group = layer, Leaf = default, LeafOrder = -1 };
    }

    public void Translate(float dx, float dy) =>
        _current = _current with { Transform = _current.Transform.TranslatedLocal(new Vector2(dx, dy)) };

    public void Scale(float sx, float sy) =>
        _current = _current with { Transform = _current.Transform.ScaledLocal(new Vector2(sx, sy)) };

    public void RotateDegrees(float degrees, float pivotX, float pivotY)
    {
        var pivot = new Vector2(pivotX, pivotY);
        _current = _current with
        {
            Transform = _current.Transform.TranslatedLocal(pivot)
                                          .RotatedLocal(Mathf.DegToRad(degrees))
                                          .TranslatedLocal(-pivot),
        };
    }

    public void ClipRect(Graphics.Rect rect)
    {
        Save();
        var clip = Acquire(_current.Group);

        // Every item in the tree has an identity transform (the CTM rides on the commands instead), so the
        // clip rect must be expressed in the host control's space. Under rotation that degrades to the
        // rotated rect's bounding box — Godot's canvas clip is a scissor, not a stencil.
        RenderingServer.CanvasItemSetCustomRect(clip, true, Bounds(rect, _current.Transform));
        RenderingServer.CanvasItemSetClip(clip, true);
        _current = _current with { Group = clip, Leaf = default, LeafOrder = -1 };
    }

    // ---- primitives ----------------------------------------------------------

    public void DrawRect(Graphics.Rect rect, in Graphics.Paint paint) => Box(rect, 0, 0, paint);

    public void DrawRoundRect(Graphics.Rect rect, float radiusX, float radiusY, in Graphics.Paint paint) =>
        Box(rect, radiusX, radiusY, paint);

    public void DrawOval(Graphics.Rect rect, in Graphics.Paint paint)
    {
        if (Gradient(rect, paint, 0, ellipse: true)) return;
        var target = Target();
        Shade(target, rect, paint);

        // RenderingServer's ellipse command is fill-only (the filled/width overload lives on CanvasItem,
        // which needs a node rather than a RID), so a stroked oval is a closed polyline around the same
        // ellipse. 48 segments is smooth at the sizes the DSL draws — progress rings and avatars.
        if (paint.Style == PaintStyle.Stroke)
            RenderingServer.CanvasItemAddPolyline(target, EllipseOutline(rect),
                new[] { ToGd(paint.Color) }, Math.Max(paint.StrokeWidth, 1f), paint.IsAntialias);
        else
            RenderingServer.CanvasItemAddEllipse(target, Center(rect), rect.Width / 2, rect.Height / 2,
                ToGd(paint.Color), paint.IsAntialias);
    }

    static Vector2[] EllipseOutline(Graphics.Rect rect, int segments = 48)
    {
        var points = new Vector2[segments + 1];
        var center = Center(rect);
        for (var i = 0; i <= segments; i++)
        {
            var angle = i / (float)segments * Mathf.Tau;
            points[i] = center + new Vector2(Mathf.Cos(angle) * rect.Width / 2, Mathf.Sin(angle) * rect.Height / 2);
        }
        return points;
    }

    public void DrawCircle(float centerX, float centerY, float radius, in Graphics.Paint paint)
    {
        var rect = new Graphics.Rect(centerX - radius, centerY - radius, centerX + radius, centerY + radius);
        if (Gradient(rect, paint, 0, ellipse: true)) return;
        var target = Target();
        Shade(target, rect, paint);
        if (paint.Style == PaintStyle.Stroke)
            RenderingServer.CanvasItemAddPolyline(target, EllipseOutline(rect),
                new[] { ToGd(paint.Color) }, Math.Max(paint.StrokeWidth, 1f), paint.IsAntialias);
        else
            RenderingServer.CanvasItemAddCircle(target, new Vector2(centerX, centerY), radius,
                ToGd(paint.Color), paint.IsAntialias);
    }

    public void DrawLine(float x0, float y0, float x1, float y1, in Graphics.Paint paint) =>
        // Always a stroke regardless of Style — several of the engine's separator paints are built without
        // an explicit Stroke style, the same assumption the Skia adapter makes.
        RenderingServer.CanvasItemAddLine(Target(), new Vector2(x0, y0), new Vector2(x1, y1),
            ToGd(paint.Color), Math.Max(paint.StrokeWidth, 1f), paint.IsAntialias);

    public void DrawImage(IImage image, Graphics.Rect dest)
    {
        if (image is not GodotImage gi) return;
        RenderingServer.CanvasItemAddTextureRect(Target(), ToGd(dest), gi.Texture.GetRid());
    }

    public void DrawText(string text, float x, float baselineY, Graphics.Font font, Graphics.Color color)
    {
        if (string.IsNullOrEmpty(text)) return;
        var gf = (GodotFont)font;

        // Godot's own shaper and fallback chain run here, which is what makes DrawText agree with
        // GodotFonts.Measure — both go through the same Font resource.
        gf.Native.DrawString(Target(), new Vector2(x, baselineY), text, Godot.HorizontalAlignment.Left, -1,
            gf.PixelSize, ToGd(color));
    }

    // ---- fills ---------------------------------------------------------------

    /// <summary>
    /// Rects and rounded rects both go through <see cref="StyleBoxFlat"/>: it is the only Godot canvas
    /// primitive that does corner radii, borders and a drop shadow, and it is antialiased.
    /// </summary>
    void Box(Graphics.Rect rect, float radiusX, float radiusY, in Graphics.Paint paint)
    {
        if (Gradient(rect, paint, Math.Max(radiusX, radiusY), ellipse: false)) return;

        var stroke = paint.Style == PaintStyle.Stroke;
        var radius = (int)Math.Round(Math.Max(radiusX, radiusY));
        var box = NextBox();

        box.BgColor = stroke ? Godot.Colors.Transparent : ToGd(paint.Color);
        box.DrawCenter = !stroke;
        box.AntiAliasing = paint.IsAntialias;
        box.SetCornerRadiusAll(radius);

        // StyleBoxFlat tessellates each corner into CornerDetail segments and defaults to 8, which is
        // visibly polygonal on a large radius (a pill, or an animated card mid-scale). Scale it with the
        // radius instead — Godot's own recommendation.
        box.CornerDetail = Math.Clamp(radius / 2, 4, 20);
        box.SetBorderWidthAll(stroke ? Math.Max(1, (int)Math.Round(paint.StrokeWidth)) : 0);
        box.BorderColor = stroke ? ToGd(paint.Color) : Godot.Colors.Transparent;

        if (paint.Shadow is { } shadow)
        {
            // StyleBoxFlat's shadow is a real soft shadow, so the seam's four numbers map straight across.
            box.ShadowColor = ToGd(shadow.Color);
            box.ShadowSize = (int)Math.Round(shadow.Radius);
            box.ShadowOffset = new Vector2(shadow.Dx, shadow.Dy);
        }
        else
        {
            box.ShadowSize = 0;
            box.ShadowColor = Godot.Colors.Transparent;
        }

        box.Draw(Target(), ToGd(rect));
    }

    /// <summary>
    /// Gradient fills, as a Godot gradient texture masked to the shape.
    /// </summary>
    /// <remarks>
    /// The shape is drawn into a <see cref="RenderingServer.CanvasGroupMode.ClipOnly"/> group, which turns
    /// it into an alpha mask for that group's children, and the gradient texture is drawn as the child. That
    /// keeps rounded corners and ellipses correct instead of degrading a gradient fill to a plain rect.
    /// </remarks>
    bool Gradient(Graphics.Rect rect, in Graphics.Paint paint, float radius, bool ellipse)
    {
        if (paint.Gradient is not { } gradient || gradient.Stops.Length == 0) return false;

        var texture = NextGradient();
        texture.Gradient.Offsets = [.. gradient.Stops.Select(s => s.Location)];
        texture.Gradient.Colors = [.. gradient.Stops.Select(s => ToGd(s.Color))];

        if (gradient.Kind == GradientKind.Radial)
        {
            texture.Fill = GradientTexture2D.FillEnum.Radial;
            texture.FillFrom = Normalize(gradient.Center, rect);
            texture.FillTo = Normalize(new Graphics.Point(gradient.Center.X + gradient.Radius, gradient.Center.Y), rect);
        }
        else
        {
            texture.Fill = GradientTexture2D.FillEnum.Linear;
            texture.FillFrom = Normalize(gradient.Start, rect);
            texture.FillTo = Normalize(gradient.End, rect);
        }

        var depth = Save();
        var mask = Acquire(_current.Group);
        RenderingServer.CanvasItemSetCanvasGroupMode(mask, RenderingServer.CanvasGroupMode.ClipOnly);
        _current = _current with { Group = mask, Leaf = default, LeafOrder = -1 };

        // The mask item's own commands are the shape. Its radius has to be carried through — a rounded
        // card filled with a gradient must stay rounded, which is the whole reason for the mask.
        RenderingServer.CanvasItemAddSetTransform(mask, _current.Transform);
        DrawShapeInto(mask, rect, radius, ellipse);

        RenderingServer.CanvasItemAddTextureRect(Target(), ToGd(rect), texture.GetRid());
        RestoreToCount(depth);
        return true;
    }

    /// <summary>Paints the mask silhouette — colour is irrelevant, only coverage is.</summary>
    void DrawShapeInto(Rid item, Graphics.Rect rect, float radius, bool ellipse)
    {
        if (ellipse)
        {
            RenderingServer.CanvasItemAddEllipse(item, Center(rect), rect.Width / 2, rect.Height / 2,
                Godot.Colors.White, true);
            return;
        }

        var box = NextBox();
        box.BgColor = Godot.Colors.White;
        box.DrawCenter = true;
        box.AntiAliasing = true;
        box.SetCornerRadiusAll((int)Math.Round(radius));
        box.CornerDetail = Math.Clamp((int)Math.Round(radius) / 2, 4, 20);
        box.SetBorderWidthAll(0);
        box.BorderColor = Godot.Colors.Transparent;
        box.ShadowSize = 0;
        box.ShadowColor = Godot.Colors.Transparent;
        box.Draw(item, ToGd(rect));
    }

    /// <summary>Draws a paint's shadow when the primitive itself cannot (ellipses and circles).</summary>
    void Shade(Rid target, Graphics.Rect rect, in Graphics.Paint paint)
    {
        if (paint.Shadow is not { } shadow) return;

        // Approximate: Godot has no blur for a canvas ellipse, so this is a solid offset ellipse at the
        // shadow colour. Rects and rounded rects (which is nearly everything that casts one) get the real
        // soft shadow from StyleBoxFlat instead.
        RenderingServer.CanvasItemAddEllipse(target,
            Center(rect) + new Vector2(shadow.Dx, shadow.Dy),
            rect.Width / 2 + shadow.Radius / 2, rect.Height / 2 + shadow.Radius / 2,
            ToGd(shadow.Color), true);
    }

    /// <summary>Gradient textures are pooled like the style boxes; each is a resource with its own RID.</summary>
    GradientTexture2D NextGradient()
    {
        if (_gradientsUsed < _gradients.Count) return _gradients[_gradientsUsed++];
        var texture = new GradientTexture2D { Gradient = new Godot.Gradient(), Width = 128, Height = 128 };
        _gradients.Add(texture);
        _gradientsUsed++;
        return texture;
    }

    /// <summary>StyleBoxFlat instances are pooled too — a frame builds dozens and they are resources.</summary>
    StyleBoxFlat NextBox()
    {
        if (_boxesUsed < _boxes.Count) return _boxes[_boxesUsed++];
        var box = new StyleBoxFlat();
        _boxes.Add(box);
        _boxesUsed++;
        return box;
    }

    // ---- conversions ---------------------------------------------------------

    internal static Godot.Color ToGd(Graphics.Color c) => new(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f);

    internal static Rect2 ToGd(Graphics.Rect r) => new(r.Left, r.Top, r.Width, r.Height);

    static Vector2 Center(Graphics.Rect r) => new(r.MidX, r.MidY);

    static Vector2 Normalize(Graphics.Point p, Graphics.Rect r) =>
        new(r.Width == 0 ? 0 : (p.X - r.Left) / r.Width, r.Height == 0 ? 0 : (p.Y - r.Top) / r.Height);

    /// <summary>The axis-aligned bounds of a rect under a transform — what a scissor clip can express.</summary>
    static Rect2 Bounds(Graphics.Rect rect, Transform2D transform)
    {
        var a = transform * new Vector2(rect.Left, rect.Top);
        var b = transform * new Vector2(rect.Right, rect.Top);
        var c = transform * new Vector2(rect.Right, rect.Bottom);
        var d = transform * new Vector2(rect.Left, rect.Bottom);
        var min = new Vector2(Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)), Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)));
        var max = new Vector2(Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)), Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        return new Rect2(min, max - min);
    }
}
