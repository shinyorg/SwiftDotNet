namespace SwiftDotNet;

/// <summary>
/// A view modifier, mirroring SwiftUI's modifier chain. Order is preserved and honored on the
/// Swift side (SwiftUI modifier order is significant). <see cref="Serialize"/> receives the render
/// context so interactive modifiers (e.g. <c>.onTapGesture</c>) can register callbacks.
/// </summary>
public abstract class Modifier
{
    internal abstract Dictionary<string, object> Serialize(RenderContext ctx, string path);
}

sealed class PaddingModifier : Modifier
{
    readonly double _top, _leading, _bottom, _trailing;

    public PaddingModifier(double all) => (_top, _leading, _bottom, _trailing) = (all, all, all, all);

    public PaddingModifier(Edge edges, double amount)
    {
        _top = edges.HasFlag(Edge.Top) ? amount : 0;
        _leading = edges.HasFlag(Edge.Leading) ? amount : 0;
        _bottom = edges.HasFlag(Edge.Bottom) ? amount : 0;
        _trailing = edges.HasFlag(Edge.Trailing) ? amount : 0;
    }

    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path) => new()
    {
        ["type"] = "padding",
        ["top"] = _top,
        ["leading"] = _leading,
        ["bottom"] = _bottom,
        ["trailing"] = _trailing,
    };
}

sealed class FontModifier : Modifier
{
    readonly string _value;
    public FontModifier(string value) => _value = value;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "font", ["value"] = _value };
}

sealed class ForegroundColorModifier : Modifier
{
    readonly string _value;
    public ForegroundColorModifier(string value) => _value = value;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "foregroundColor", ["value"] = _value };
}

sealed class BackgroundModifier : Modifier
{
    readonly string? _value;
    readonly string? _gradient;
    public BackgroundModifier(string value) => _value = value;
    public BackgroundModifier(Brush brush) => _gradient = brush.Serialize();
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        // A gradient rides as a compact string prop; a flat color as the token/hex value. A backend
        // reads `gradient` first (falling back to a tinted flat fill only if it can't paint gradients).
        var d = new Dictionary<string, object> { ["type"] = "background" };
        if (_gradient is not null) d["gradient"] = _gradient;
        else if (_value is not null) d["value"] = _value;
        return d;
    }
}

/// <summary>Shifts a view by a fixed translation without affecting layout (mirrors SwiftUI's <c>.offset(x:y:)</c>).</summary>
sealed class OffsetModifier : Modifier
{
    readonly double _x, _y;
    public OffsetModifier(double x, double y) { _x = x; _y = y; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "offset", ["x"] = _x, ["y"] = _y };
}

/// <summary>Rotates a view around its center by <c>degrees</c> (mirrors SwiftUI's <c>.rotationEffect(.degrees)</c>).</summary>
sealed class RotationModifier : Modifier
{
    readonly double _degrees;
    readonly string _anchor;
    public RotationModifier(double degrees, string anchor) { _degrees = degrees; _anchor = anchor; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "rotation", ["degrees"] = _degrees, ["value"] = _anchor };
}

sealed class FrameModifier : Modifier
{
    readonly double? _width;
    readonly double? _height;
    readonly string? _alignment;
    public FrameModifier(double? width, double? height, string? alignment)
    {
        _width = width;
        _height = height;
        _alignment = alignment;
    }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        var d = new Dictionary<string, object> { ["type"] = "frame" };
        if (_width.HasValue) d["width"] = _width.Value;
        if (_height.HasValue) d["height"] = _height.Value;
        if (_alignment is not null) d["alignment"] = _alignment;
        return d;
    }
}

sealed class BorderModifier : Modifier
{
    readonly string _color;
    readonly double _width;
    readonly double _cornerRadius;
    public BorderModifier(string color, double width, double cornerRadius)
    {
        _color = color;
        _width = width;
        _cornerRadius = cornerRadius;
    }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path) => new()
    {
        ["type"] = "border",
        ["color"] = _color,
        ["width"] = _width,
        ["cornerRadius"] = _cornerRadius,
    };
}

/// <summary>Fills available width and aligns the content (mirrors <c>.frame(maxWidth: .infinity, alignment:)</c>).</summary>
sealed class AlignModifier : Modifier
{
    readonly string _alignment;
    public AlignModifier(string alignment) => _alignment = alignment;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "align", ["value"] = _alignment };
}

sealed class CornerRadiusModifier : Modifier
{
    readonly double _radius;
    public CornerRadiusModifier(double radius) => _radius = radius;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "cornerRadius", ["radius"] = _radius };
}

sealed class ShadowModifier : Modifier
{
    readonly double _radius;
    readonly string? _color;
    readonly double _x;
    readonly double _y;
    public ShadowModifier(double radius, string? color, double x, double y)
    {
        _radius = radius;
        _color = color;
        _x = x;
        _y = y;
    }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        var d = new Dictionary<string, object> { ["type"] = "shadow", ["radius"] = _radius, ["x"] = _x, ["y"] = _y };
        if (_color is not null) d["color"] = _color;
        return d;
    }
}

sealed class OpacityModifier : Modifier
{
    readonly double _opacity;
    // Clamp to the valid 0–1 range so an out-of-range value degrades identically on every backend
    // (SwiftUI/Compose/GTK/WinUI/CSS otherwise each clamp — or don't — differently).
    public OpacityModifier(double opacity) => _opacity = Math.Clamp(opacity, 0, 1);
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "opacity", ["amount"] = _opacity };
}

/// <summary>Scales a view (and its subtree) around an anchor, mirroring SwiftUI's <c>.scaleEffect(x:y:anchor:)</c>.</summary>
sealed class ScaleEffectModifier : Modifier
{
    readonly double _x, _y;
    readonly string _anchor;
    public ScaleEffectModifier(double x, double y, string anchor) { _x = x; _y = y; _anchor = anchor; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path) => new()
    {
        ["type"] = "scaleEffect",
        ["x"] = _x,
        ["y"] = _y,
        ["value"] = _anchor,
    };
}

/// <summary>
/// Attaches an implicit animation, mirroring SwiftUI's <c>.animation(_:value:)</c>. When the
/// <c>trigger</c> string changes (the stringified <c>on:</c> value), animatable modifiers earlier in
/// the chain (opacity/scale/frame/offset/color) interpolate to their new values instead of snapping.
/// </summary>
sealed class AnimationModifier : Modifier
{
    readonly AnimationSpec _spec;
    readonly string _trigger;
    public AnimationModifier(AnimationSpec spec, string trigger) { _spec = spec; _trigger = trigger; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        var d = new Dictionary<string, object>
        {
            ["type"] = "animation",
            ["curve"] = _spec.Curve.Token(),
            ["duration"] = _spec.Duration,
            ["delay"] = _spec.Delay,
            ["trigger"] = _trigger,
        };
        if (_spec.SpringStiffness is { } s) d["stiffness"] = s;
        if (_spec.SpringDamping is { } dm) d["damping"] = dm;
        // A repeating animation (shimmer/pulse) has no external `trigger` — it plays on its own clock.
        // repeatCount -1 = forever; autoreverse yo-yos each cycle. Backends without a repeat concept
        // fall back to the single value-triggered transition (documented degradation).
        if (_spec.RepeatCount is { } rc) { d["repeatCount"] = (double)rc; d["autoreverse"] = _spec.AutoReverse ? "true" : "false"; }
        return d;
    }
}

/// <summary>Dims and blocks interaction on a view (and its subtree), mirroring SwiftUI's <c>.disabled()</c>.</summary>
sealed class DisabledModifier : Modifier
{
    readonly bool _disabled;
    public DisabledModifier(bool disabled) => _disabled = disabled;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "disabled", ["value"] = _disabled ? "true" : "false" };
}

sealed class NavigationTitleModifier : Modifier
{
    readonly string _title;
    public NavigationTitleModifier(string title) => _title = title;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "navigationTitle", ["value"] = _title };
}

sealed class OnTapGestureModifier : Modifier
{
    readonly Action _action;
    readonly int _count;
    public OnTapGestureModifier(Action action, int count) { _action = action; _count = Math.Max(1, count); }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        ctx.RegisterAction(path, _ => _action());
        // `amount` carries the required tap count (1 = single, 2 = double-tap); reuses the existing wire field.
        return new() { ["type"] = "onTapGesture", ["event"] = path, ["amount"] = (double)_count };
    }
}

/// <summary>Fires once after a press-and-hold, mirroring SwiftUI's <c>.onLongPressGesture(minimumDuration:)</c>.</summary>
sealed class OnLongPressModifier : Modifier
{
    readonly Action _action;
    readonly double _minimumDuration;
    public OnLongPressModifier(Action action, double minimumDuration) { _action = action; _minimumDuration = minimumDuration; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        ctx.RegisterAction(path, _ => _action());
        // `amount` carries the hold threshold in seconds; each backend maps it to its recognizer's minimum duration.
        return new() { ["type"] = "onLongPress", ["event"] = path, ["amount"] = _minimumDuration };
    }
}

/// <summary>Fires once when the view is swiped in a fixed direction — a directional drag committed on release.</summary>
sealed class OnSwipeModifier : Modifier
{
    readonly Action _action;
    readonly string _direction;
    public OnSwipeModifier(Action action, string direction) { _action = action; _direction = direction; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        ctx.RegisterAction(path, _ => _action());
        // `value` carries the direction token (left/right/up/down); the native recognizer only emits on a match.
        return new() { ["type"] = "onSwipe", ["event"] = path, ["value"] = _direction };
    }
}
