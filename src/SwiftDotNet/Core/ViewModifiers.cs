using System.Runtime.Versioning;

namespace SwiftDotNet;

/// <summary>
/// SwiftUI-style modifiers as fluent extension methods. Generic <c>T</c> preserves the concrete
/// view type so chaining reads like SwiftUI: <c>new Text("hi").Font(Font.Title).Padding()</c>.
/// </summary>
public static class ViewModifiers
{
    public static T Font<T>(this T view, SwiftFont font) where T : View
    {
        view.Modifiers.Add(new FontModifier(font.Value));
        return view;
    }

    public static T ForegroundColor<T>(this T view, SwiftColor color) where T : View
    {
        view.Modifiers.Add(new ForegroundColorModifier(color.Value));
        return view;
    }

    public static T Background<T>(this T view, SwiftColor color) where T : View
    {
        view.Modifiers.Add(new BackgroundModifier(color.Value));
        return view;
    }

    /// <summary>Fills the view's background with a gradient <see cref="Brush"/> (<c>LinearGradient</c>/<c>RadialGradient</c>).</summary>
    public static T Background<T>(this T view, Brush brush) where T : View
    {
        view.Modifiers.Add(new BackgroundModifier(brush));
        return view;
    }

    /// <summary>
    /// F6 — a frosted-glass <see cref="MaterialStyle"/> background: a translucent blurred backdrop where the
    /// backend supports it (Web/SwiftUI), a translucent tint elsewhere. Pass <paramref name="dark"/> true for
    /// a dark-on-light glass. Mirrors SwiftUI's <c>.background(.thinMaterial)</c>.
    /// </summary>
    public static T Material<T>(this T view, MaterialStyle style = MaterialStyle.Regular, bool dark = false) where T : View
    {
        view.Modifiers.Add(new MaterialModifier(style.Token(), dark));
        return view;
    }

    public static T Padding<T>(this T view, double all = 16) where T : View
    {
        view.Modifiers.Add(new PaddingModifier(all));
        return view;
    }

    /// <summary>Per-edge padding: <c>.Padding(Edge.Horizontal, 20)</c>.</summary>
    public static T Padding<T>(this T view, Edge edges, double amount) where T : View
    {
        view.Modifiers.Add(new PaddingModifier(edges, amount));
        return view;
    }

    /// <summary>Asymmetric padding in one modifier: <c>.Padding(horizontal: 12, vertical: 4)</c>.</summary>
    public static T Padding<T>(this T view, double horizontal, double vertical) where T : View
    {
        view.Modifiers.Add(new PaddingModifier(horizontal, vertical));
        return view;
    }

    /// <summary>
    /// Insets the view by the platform's safe area so it stays clear of the status bar, cutout, home
    /// indicator, and — with <see cref="SafeAreaRegions.Keyboard"/> — the soft keyboard. Mirrors SwiftUI's
    /// <c>.safeAreaPadding(_:)</c>.
    ///
    /// <b>iOS and Android only.</b> Guard the call with <see cref="SafeArea.IsSupported"/> from a
    /// platform-neutral project; every other backend ignores the modifier if one reaches it anyway.
    /// </summary>
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("android")]
    [UnsupportedOSPlatform("maccatalyst")]
    public static T SafeAreaPadding<T>(this T view, Edge edges = Edge.All, SafeAreaRegions regions = SafeAreaRegions.Container) where T : View
    {
        view.Modifiers.Add(new SafeAreaPaddingModifier(edges, regions));
        return view;
    }

    /// <summary>
    /// Lets the view extend under the safe area on <paramref name="edges"/> — a full-bleed background or
    /// header. Mirrors SwiftUI's <c>.ignoresSafeArea(_:edges:)</c>.
    ///
    /// <b>iOS and Android only.</b> On Compose, where content is already edge-to-edge, this *consumes*
    /// the insets so descendants don't re-apply them. Guard with <see cref="SafeArea.IsSupported"/>.
    /// </summary>
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("android")]
    [UnsupportedOSPlatform("maccatalyst")]
    public static T IgnoresSafeArea<T>(this T view, Edge edges = Edge.All, SafeAreaRegions regions = SafeAreaRegions.All) where T : View
    {
        view.Modifiers.Add(new IgnoresSafeAreaModifier(edges, regions));
        return view;
    }

    public static T Frame<T>(this T view, double? width = null, double? height = null, Alignment? alignment = null) where T : View
    {
        view.Modifiers.Add(new FrameModifier(width, height, alignment?.Token()));
        return view;
    }

    public static T CornerRadius<T>(this T view, double radius) where T : View
    {
        view.Modifiers.Add(new CornerRadiusModifier(radius));
        return view;
    }

    public static T Shadow<T>(this T view, double radius = 4, SwiftColor? color = null, double x = 0, double y = 0) where T : View
    {
        view.Modifiers.Add(new ShadowModifier(radius, color?.Value, x, y));
        return view;
    }

    /// <summary>A stroked border, optionally rounded: <c>.Border(Color.Blue, 2, cornerRadius: 8)</c>.</summary>
    public static T Border<T>(this T view, SwiftColor color, double width = 1, double cornerRadius = 0) where T : View
    {
        view.Modifiers.Add(new BorderModifier(color.Value, width, cornerRadius));
        return view;
    }

    /// <summary>Fills available width and aligns content — <c>.Align(Alignment.Leading)</c> left-aligns a control.</summary>
    public static T Align<T>(this T view, Alignment alignment) where T : View
    {
        view.Modifiers.Add(new AlignModifier(alignment.Token()));
        return view;
    }

    public static T Opacity<T>(this T view, double opacity) where T : View
    {
        view.Modifiers.Add(new OpacityModifier(opacity));
        return view;
    }

    /// <summary>
    /// Dims the view and blocks interaction on it (and its subtree) — the "greyed-out" state, mirroring
    /// SwiftUI's <c>.disabled()</c>. Maps to each platform's native disabled semantics where available
    /// (SwiftUI <c>.disabled</c>, GTK <c>Sensitive=false</c>, WinUI <c>IsEnabled=false</c>) and to
    /// dim + no-hit-testing where not (Compose, Web).
    /// </summary>
    public static T Disabled<T>(this T view, bool disabled = true) where T : View
    {
        view.Modifiers.Add(new DisabledModifier(disabled));
        return view;
    }

    /// <summary>Uniformly scales the view around <paramref name="anchor"/> (mirrors <c>.scaleEffect(_:anchor:)</c>).</summary>
    public static T ScaleEffect<T>(this T view, double scale, Alignment anchor = Alignment.Center) where T : View
    {
        view.Modifiers.Add(new ScaleEffectModifier(scale, scale, anchor.Token()));
        return view;
    }

    /// <summary>Scales the view non-uniformly around <paramref name="anchor"/> (mirrors <c>.scaleEffect(x:y:anchor:)</c>).</summary>
    public static T ScaleEffect<T>(this T view, double x, double y, Alignment anchor = Alignment.Center) where T : View
    {
        view.Modifiers.Add(new ScaleEffectModifier(x, y, anchor.Token()));
        return view;
    }

    /// <summary>Shifts the view by <paramref name="x"/>/<paramref name="y"/> points without affecting layout (mirrors <c>.offset(x:y:)</c>).</summary>
    public static T Offset<T>(this T view, double x = 0, double y = 0) where T : View
    {
        view.Modifiers.Add(new OffsetModifier(x, y));
        return view;
    }

    // ---- Grid placement -------------------------------------------------------------------------
    // Both helpers merge into a single `gridCell` wire modifier so they can be chained in either order.

    /// <summary>
    /// Makes this child cover <paramref name="columns"/> columns and <paramref name="rows"/> rows of its
    /// enclosing <see cref="Grid"/>. Row spans are honored on Skia/GTK/WinUI/TUI/Web/Compose/SwiftUI;
    /// see the docs for the per-backend table.
    /// </summary>
    public static T GridSpan<T>(this T view, int columns = 1, int rows = 1) where T : View
    {
        var m = view.EnsureGridCell();
        m.ColumnSpan = Math.Max(1, columns);
        m.RowSpan = Math.Max(1, rows);
        return view;
    }

    /// <summary>
    /// Pins this child to an explicit zero-based cell of its enclosing <see cref="Grid"/>, instead of
    /// letting it flow into the next free one. Children without an explicit cell flow around pinned ones.
    /// </summary>
    public static T GridCell<T>(this T view, int column, int row, int columnSpan = 1, int rowSpan = 1) where T : View
    {
        var m = view.EnsureGridCell();
        m.Column = Math.Max(0, column);
        m.Row = Math.Max(0, row);
        m.ColumnSpan = Math.Max(1, columnSpan);
        m.RowSpan = Math.Max(1, rowSpan);
        return view;
    }

    static GridCellModifier EnsureGridCell(this View view)
    {
        foreach (var m in view.Modifiers)
            if (m is GridCellModifier g) return g;
        var added = new GridCellModifier();
        view.Modifiers.Add(added);
        return added;
    }

    // ---- AbsoluteLayout placement ---------------------------------------------------------------

    /// <summary>
    /// Positions this child's top-left corner inside its enclosing <see cref="AbsoluteLayout"/>, leaving
    /// it to size itself. Ignored by every other container.
    /// </summary>
    public static T LayoutBounds<T>(this T view, double x, double y) where T : View
    {
        view.Modifiers.Add(new LayoutBoundsModifier(x, y, null, null, LayoutFlags.None));
        return view;
    }

    /// <summary>
    /// Positions and sizes this child inside its enclosing <see cref="AbsoluteLayout"/>. Pass
    /// <see cref="AbsoluteLayout.AutoSize"/> for a width/height the child should decide for itself, and
    /// <paramref name="flags"/> to read any of x/y/width/height as a fraction of the layout's own size.
    /// </summary>
    public static T LayoutBounds<T>(this T view, double x, double y, double width, double height,
        LayoutFlags flags = LayoutFlags.None) where T : View
    {
        view.Modifiers.Add(new LayoutBoundsModifier(
            x, y,
            width < 0 ? null : width,
            height < 0 ? null : height,
            flags));
        return view;
    }

    /// <summary>Rotates the view by <paramref name="degrees"/> around <paramref name="anchor"/> (mirrors <c>.rotationEffect(.degrees(_:))</c>).</summary>
    public static T Rotation<T>(this T view, double degrees, Alignment anchor = Alignment.Center) where T : View
    {
        view.Modifiers.Add(new RotationModifier(degrees, anchor.Token()));
        return view;
    }

    /// <summary>
    /// Animates this view's animatable modifiers (opacity, scale, frame, offset, color) whenever the
    /// <paramref name="on"/> value changes — mirrors SwiftUI's <c>.animation(_:value:)</c>. Pass the state
    /// you're binding to (e.g. <c>on: _expanded.Value</c>) so the change arms the animation; a change to any
    /// other modifier in the same render then interpolates instead of snapping.
    /// </summary>
    public static T Animation<T>(this T view, AnimationSpec spec, object? on = null) where T : View
    {
        view.Modifiers.Add(new AnimationModifier(spec, Convert.ToString(on, System.Globalization.CultureInfo.InvariantCulture) ?? ""));
        return view;
    }

    /// <summary>
    /// Drives this view along a multi-track keyframe timeline — mirrors SwiftUI's
    /// <c>keyframeAnimator(initialValue:content:keyframes:)</c>. Each <see cref="Prop"/> gets its own
    /// track of stops with its own curves, so several properties can animate on independent shapes over
    /// one shared clock:
    /// <code>
    /// view.Keyframes(k => k
    ///     .Track(Prop.Opacity, t => t.At(0, 1).At(0.5, 0.3, Anim.EaseOut()).At(1, 1))
    ///     .Track(Prop.Scale,   t => t.At(0, 1).At(0.6, 1.2, Anim.Spring()).At(1, 1))
    ///     .Duration(1.2)
    ///     .Repeating());
    /// </code>
    /// Stop times are fractions of <c>Duration</c>, and values are <em>absolute</em> — a track overrides
    /// any static <c>.Opacity()</c>/<c>.ScaleEffect()</c>/<c>.Rotation()</c> on the same view while it
    /// plays. Without <c>.Repeating()</c> the timeline plays once, replaying whenever <c>.On(value)</c>
    /// changes.
    /// </summary>
    public static T Keyframes<T>(this T view, Action<KeyframeTimeline> build) where T : View
    {
        var timeline = new KeyframeTimeline();
        build(timeline);
        // An empty timeline would serialize to a modifier every backend has to special-case; drop it here.
        if (!timeline.IsEmpty) view.Modifiers.Add(new KeyframesModifier(timeline));
        return view;
    }

    public static T NavigationTitle<T>(this T view, string title) where T : View
    {
        view.Modifiers.Add(new NavigationTitleModifier(title));
        return view;
    }

    /// <summary>
    /// Fires <paramref name="action"/> on tap. Pass <paramref name="count"/> = 2 for a double-tap
    /// (mirrors <c>.onTapGesture(count:)</c>).
    /// </summary>
    public static T OnTapGesture<T>(this T view, Action action, int count = 1) where T : View
    {
        view.Modifiers.Add(new OnTapGestureModifier(action, count));
        return view;
    }

    /// <summary>
    /// Fires <paramref name="action"/> after a press-and-hold of at least <paramref name="minimumDuration"/>
    /// seconds (mirrors <c>.onLongPressGesture(minimumDuration:)</c>).
    /// </summary>
    public static T OnLongPress<T>(this T view, Action action, double minimumDuration = 0.5) where T : View
    {
        view.Modifiers.Add(new OnLongPressModifier(action, minimumDuration));
        return view;
    }

    /// <summary>
    /// Fires <paramref name="action"/> when the view is swiped in <paramref name="direction"/> — a
    /// directional drag committed on release. One-shot; add multiple calls for multiple directions.
    /// </summary>
    public static T OnSwipe<T>(this T view, SwipeDirection direction, Action action) where T : View
    {
        view.Modifiers.Add(new OnSwipeModifier(action, direction.Token()));
        return view;
    }

    /// <summary>
    /// F1 — a continuous drag/pan gesture. <paramref name="handler"/> fires on began/changed/ended with the
    /// cumulative translation, current location, and release velocity (mirrors SwiftUI's <c>DragGesture</c>).
    /// Each event modifier gets its own event id, so a view may carry both <c>OnDrag</c> and <c>OnMagnify</c>
    /// (e.g. a pinch-and-pan zoomable image).
    /// </summary>
    public static T OnDrag<T>(this T view, Action<DragInfo> handler, double minimumDistance = 0) where T : View
    {
        view.Modifiers.Add(new OnDragModifier(handler, minimumDistance));
        return view;
    }

    /// <summary>
    /// F1 — a continuous pinch/magnify gesture. <paramref name="handler"/> fires with the cumulative scale
    /// factor (1.0 = unchanged) as the pinch updates (mirrors SwiftUI's <c>MagnificationGesture</c>).
    /// </summary>
    public static T OnMagnify<T>(this T view, Action<double> handler) where T : View
    {
        view.Modifiers.Add(new OnMagnifyModifier(handler));
        return view;
    }
}
