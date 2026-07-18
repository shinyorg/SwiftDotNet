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
    readonly string _value;
    public BackgroundModifier(string value) => _value = value;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
        => new() { ["type"] = "background", ["value"] = _value };
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
    public OnTapGestureModifier(Action action) => _action = action;
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        ctx.RegisterAction(path, _ => _action());
        return new() { ["type"] = "onTapGesture", ["event"] = path };
    }
}
