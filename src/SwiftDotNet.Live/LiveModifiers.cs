namespace SwiftDotNet;

/// <summary>
/// The fluent modifier surface for <see cref="LiveView"/> — the intersection of what a widget-subset
/// SwiftUI view and a <c>RemoteViews</c> tree can both express.
///
/// This is much smaller than <see cref="ViewModifiers"/>, and the omissions are deliberate rather than
/// unfinished. There is no <c>.Rotation</c>, <c>.ScaleEffect</c>, <c>.Shadow</c>, <c>.Blur</c>,
/// <c>.Animation</c> or gesture modifier here, because a <c>RemoteViews</c> tree has no transform,
/// filter or animation vocabulary at all and a widget's SwiftUI cannot animate on demand. Adding them
/// would produce a modifier that works on one platform and is silently dropped on the other — precisely
/// the failure mode this vocabulary exists to prevent.
///
/// Several modifiers below *are* asymmetric, and each is annotated with what actually happens.
/// </summary>
public static class LiveModifiers
{
    /// <summary>Semantic font. Both hosts map the token; Android resolves it to an sp size.</summary>
    public static T Font<T>(this T view, SwiftFont font) where T : LiveView
    {
        view.AddMod("font", ("value", font.Value));
        return view;
    }

    /// <summary>Text / symbol color.</summary>
    public static T ForegroundColor<T>(this T view, SwiftColor color) where T : LiveView
    {
        view.AddMod("foregroundColor", ("value", color.Value));
        return view;
    }

    /// <summary>Flat background fill.</summary>
    public static T Background<T>(this T view, SwiftColor color) where T : LiveView
    {
        // Key name matches the core wire ("value" for a flat fill, "gradient" for a Brush) so both
        // interpreters read backgrounds the same way they already do everywhere else.
        view.AddMod("background", ("value", color.Value));
        return view;
    }

    /// <summary>
    /// Gradient background, reusing the core <see cref="Brush"/> wire grammar verbatim.
    /// **Apple only.** A <c>RemoteViews</c> tree has no gradient drawable it can build at runtime, so
    /// Android falls back to the gradient's *first* stop as a flat fill. Use
    /// <see cref="LiveRenderMode.Bitmap"/> if the gradient matters on Android.
    /// </summary>
    public static T Background<T>(this T view, Brush brush) where T : LiveView
    {
        view.AddMod("background", ("gradient", BrushWire.Of(brush)));
        return view;
    }

    /// <summary>Uniform padding in points.</summary>
    public static T Padding<T>(this T view, double all) where T : LiveView
        => view.Padding(all, all, all, all);

    /// <summary>Per-edge padding in points.</summary>
    public static T Padding<T>(this T view, double top, double leading, double bottom, double trailing)
        where T : LiveView
    {
        view.AddMod("padding", ("top", top), ("leading", leading), ("bottom", bottom), ("trailing", trailing));
        return view;
    }

    /// <summary>
    /// Fixed size in points. **Requires API 31 on Android** — <c>setViewLayoutWidth</c> /
    /// <c>setViewLayoutHeight</c> are the only runtime sizing a <c>RemoteViews</c> tree has, and they do
    /// not exist before then. On API 24–30 the modifier is a no-op and
    /// <see cref="LiveValidator"/> reports it when the target API is declared lower.
    /// </summary>
    public static T Frame<T>(this T view, double? width = null, double? height = null) where T : LiveView
    {
        var vals = new List<(string, object)>();
        if (width is { } w) vals.Add(("width", w));
        if (height is { } h) vals.Add(("height", h));
        view.AddMod("frame", vals.ToArray());
        return view;
    }

    /// <summary>Rounds the view's corners. Clips on Apple; sets a rounded background drawable on Android.</summary>
    public static T CornerRadius<T>(this T view, double radius) where T : LiveView
    {
        view.AddMod("cornerRadius", ("value", radius));
        return view;
    }

    /// <summary>0–1 opacity.</summary>
    public static T Opacity<T>(this T view, double opacity) where T : LiveView
    {
        view.AddMod("opacity", ("value", Math.Clamp(opacity, 0, 1)));
        return view;
    }

    /// <summary>Tints a progress bar, gauge or symbol.</summary>
    public static T Tint<T>(this T view, SwiftColor color) where T : LiveView
    {
        view.AddMod("tint", ("value", color.Value));
        return view;
    }

    /// <summary>Caps the number of rendered lines. 1 is the norm on every constrained surface.</summary>
    public static T LineLimit<T>(this T view, int lines) where T : LiveView
    {
        view.AddMod("lineLimit", ("value", (double)lines));
        return view;
    }

    /// <summary>Bold weight.</summary>
    public static T Bold<T>(this T view, bool bold = true) where T : LiveView
    {
        view.AddMod("bold", ("value", bold));
        return view;
    }

    /// <summary>
    /// The accessibility label. **Not optional on a bitmap.** A <see cref="LiveBitmap"/> — and any tree
    /// published with <see cref="LiveRenderMode.Bitmap"/> — is one opaque image to VoiceOver and TalkBack,
    /// so this is the *only* thing a screen-reader user gets. <see cref="LiveValidator"/> fails a bitmap
    /// that has none.
    /// </summary>
    public static T AccessibilityLabel<T>(this T view, string label) where T : LiveView
    {
        view.AddMod("a11yLabel", ("value", label));
        return view;
    }

    /// <summary>
    /// The URL opened when the *whole* surface is tapped. Applied to the root of a tree.
    /// Apple: <c>widgetURL</c>. Android: the notification/widget content <c>PendingIntent</c>.
    /// </summary>
    public static T OnTapUrl<T>(this T view, string url) where T : LiveView
    {
        view.AddMod("tapUrl", ("value", url));
        return view;
    }
}

/// <summary>
/// Reaches the core <see cref="Brush"/> wire string from outside the core assembly.
///
/// <c>Brush.Serialize</c> is <c>internal</c> on purpose — it is a wire detail, not public API — so this
/// assembly is granted <c>InternalsVisibleTo</c> by the core project rather than reflecting into it.
/// Reflection would defeat the trimming/AOT guarantee the whole framework is built around.
/// </summary>
static class BrushWire
{
    public static string Of(Brush brush) => brush.Serialize();
}
