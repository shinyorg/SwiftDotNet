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
}
