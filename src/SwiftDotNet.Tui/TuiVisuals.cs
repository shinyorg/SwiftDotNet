using System.Text;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;
using XenoAtom.Terminal.UI.Styling;

// `Rectangle` is also a SwiftDotNet DSL shape view, and `Style` is a DSL concept too — alias the
// terminal ones so the layout overrides bind to the right types.
using TStyle = XenoAtom.Terminal.UI.Style;
using TRect = XenoAtom.Terminal.UI.Geometry.Rectangle;

namespace SwiftDotNet;

/// <summary>
/// The one visual every node is wrapped in: a single-child decorator carrying the node's
/// <c>padding</c> / <c>background</c> / <c>border</c> / <c>opacity</c> modifiers.
///
/// <para>Terminal.UI's own <c>Padder</c> and <c>Border</c> can't fill this role between them, because
/// <c>Border</c> <em>always</em> costs a cell on every edge — so a node whose <c>.Border()</c> is added by
/// a later state change couldn't be upgraded in place without restructuring the tree. The wire's diff
/// (see <see cref="TreeDiffer"/>) reports a modifier appearing as an <c>updateProps</c>, never a
/// <c>replace</c>, so the wrapper has to be present from the start and fully mutable afterwards. One
/// surface per node, every visual modifier a settable property on it, is what makes that work.</para>
/// </summary>
sealed class TuiSurface : Visual
{
    Visual? _child;

    /// <summary>Inner padding, in cells. Includes the one-cell inset a border needs.</summary>
    public Thickness Padding { get; set; } = Thickness.Zero;

    /// <summary>Background fill, or null to let whatever is behind show through.</summary>
    public TStyle? Fill { get; set; }

    /// <summary>Line glyphs for the border box, or null for no border.</summary>
    public LineGlyphs? BorderGlyphs { get; set; }

    /// <summary>Style for the border glyphs.</summary>
    public TStyle BorderStyle { get; set; } = TStyle.None;

    public Visual? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value)) return;
            if (_child is not null) DetachChild(_child);
            _child = value;
            if (_child is not null) AttachChild(_child);
            // No Invalidate() here: this surface is driven imperatively by the patch stream, not by
            // bindable properties, so TuiBridge asks the app for a full render once per patch instead.
        }
    }

    protected override int ChildrenCount => _child is null ? 0 : 1;

    protected override Visual GetChild(int index) => _child!;

    protected override SizeHints MeasureCore(in LayoutConstraints constraints)
    {
        var padH = Padding.Horizontal;
        var padV = Padding.Vertical;
        if (_child is null)
            return SizeHints.Fixed(new Size(padH, padV));

        var inner = new LayoutConstraints(
            0, constraints.IsWidthBounded ? Math.Max(0, constraints.MaxWidth - padH) : int.MaxValue,
            0, constraints.IsHeightBounded ? Math.Max(0, constraints.MaxHeight - padV) : int.MaxValue);
        var hints = _child.Measure(inner);

        return SizeHints.Flex(
            new Size(hints.Min.Width + padH, hints.Min.Height + padV),
            new Size(hints.Natural.Width + padH, hints.Natural.Height + padV),
            new Size(Grow(hints.Max.Width, padH), Grow(hints.Max.Height, padV)),
            hints.FlexGrowX, hints.FlexGrowY, hints.FlexShrinkX, hints.FlexShrinkY).Normalize();

        // An unbounded child stays unbounded — adding padding to int.MaxValue would wrap to negative.
        static int Grow(int value, int pad) => value == int.MaxValue ? int.MaxValue : value + pad;
    }

    protected override void ArrangeCore(in TRect finalRect)
        => _child?.Arrange(new TRect(
            finalRect.X + Padding.Left,
            finalRect.Y + Padding.Top,
            Math.Max(0, finalRect.Width - Padding.Horizontal),
            Math.Max(0, finalRect.Height - Padding.Vertical)));

    protected override void RenderOverride(CellBuffer buffer)
    {
        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        if (Fill is { } fill)
        {
            var blank = new Rune(' ');
            for (var y = b.Top; y < b.Bottom; y++)
                for (var x = b.Left; x < b.Right; x++)
                    buffer.SetCell(x, y, blank, fill);
        }

        if (BorderGlyphs is { } g && b.Width >= 2 && b.Height >= 2)
            DrawBox(buffer, b, g, BorderStyle);
    }

    static void DrawBox(CellBuffer buffer, TRect b, LineGlyphs g, TStyle style)
    {
        int l = b.Left, t = b.Top, r = b.Right - 1, bo = b.Bottom - 1;
        buffer.SetCell(l, t, g.TopLeft, style);
        buffer.SetCell(r, t, g.TopRight, style);
        buffer.SetCell(l, bo, g.BottomLeft, style);
        buffer.SetCell(r, bo, g.BottomRight, style);
        for (var x = l + 1; x < r; x++)
        {
            buffer.SetCell(x, t, g.Horizontal, style);
            buffer.SetCell(x, bo, g.Horizontal, style);
        }
        for (var y = t + 1; y < bo; y++)
        {
            buffer.SetCell(l, y, g.Vertical, style);
            buffer.SetCell(r, y, g.Vertical, style);
        }
    }
}

/// <summary>
/// SwiftUI's <c>Spacer</c>: takes no minimum room and absorbs everything left over. Expressed as a
/// flex-grow of 1 on both axes, which is the layout protocol's own "fill the remainder" mechanism —
/// an alignment of <c>Stretch</c> would only make the visual fill a slot it was already given, not
/// claim a bigger one.
/// </summary>
sealed class TuiSpacer : Visual
{
    protected override SizeHints MeasureCore(in LayoutConstraints constraints)
        => SizeHints.Flex(Size.Zero, Size.Zero, new Size(int.MaxValue, int.MaxValue), 1, 1, 0, 0);

    protected override void ArrangeCore(in TRect finalRect) { }
}

/// <summary>
/// A filled shape — <c>Rectangle</c>, <c>Circle</c>, <c>Capsule</c>, <c>RoundedRectangle</c>. Cells are
/// square-ish blocks rather than pixels, so a circle is rasterised on the terminal's ~2:1 cell aspect
/// and a corner radius rounds to whole cells (a 1-cell radius simply clips the four corner cells).
/// </summary>
sealed class TuiShape : Visual
{
    public TuiShape()
    {
        // Shapes are greedy, the way SwiftUI's are: a bare `Rectangle()` fills what it is given, and a
        // `.Frame(80, 32)` fills the frame rather than sitting at its natural size inside it. Without
        // this, the surface would be sized correctly by the frame and the paint would still hug Natural.
        HorizontalAlignment = Align.Stretch;
        VerticalAlignment = Align.Stretch;
    }

    public TStyle Fill { get; set; } = TStyle.None;

    /// <summary>Corner rounding in cells; <see cref="IsEllipse"/> wins over it when set.</summary>
    public int CornerRadius { get; set; }

    public bool IsEllipse { get; set; }

    /// <summary>Natural size in cells when no <c>.Frame</c> pins one — matches GTK's 40×40px default box.</summary>
    public Size Natural { get; set; } = new(6, 3);

    protected override SizeHints MeasureCore(in LayoutConstraints constraints)
        => SizeHints.Flex(new Size(1, 1), Natural, new Size(int.MaxValue, int.MaxValue), 0, 0, 1, 1);

    protected override void ArrangeCore(in TRect finalRect) { }

    protected override void RenderOverride(CellBuffer buffer)
    {
        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;
        var blank = new Rune(' ');

        // A capsule is a rounded rect whose radius saturates at half the short side; a circle is the
        // ellipse case. Both fall back to the plain rect test when the shape is too small to round.
        var radius = IsEllipse ? 0 : Math.Min(CornerRadius, Math.Min(b.Width, b.Height) / 2);
        double cx = (b.Width - 1) / 2.0, cy = (b.Height - 1) / 2.0;

        for (var y = 0; y < b.Height; y++)
        for (var x = 0; x < b.Width; x++)
        {
            if (!Inside(x, y)) continue;
            buffer.SetCell(b.Left + x, b.Top + y, blank, Fill);
        }

        bool Inside(int x, int y)
        {
            if (IsEllipse)
            {
                var nx = cx == 0 ? 0 : (x - cx) / (cx + 0.5);
                var ny = cy == 0 ? 0 : (y - cy) / (cy + 0.5);
                return nx * nx + ny * ny <= 1.0;
            }
            if (radius <= 0) return true;
            // Corner cells outside the quarter-disc of `radius` are cut away.
            var dx = x < radius ? radius - x : x >= b.Width - radius ? x - (b.Width - 1 - radius) : 0;
            var dy = y < radius ? radius - y : y >= b.Height - radius ? y - (b.Height - 1 - radius) : 0;
            return dx * dx + dy * dy <= radius * radius;
        }
    }
}
