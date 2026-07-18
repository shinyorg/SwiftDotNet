namespace SwiftDotNet;

/// <summary>An SF Symbol image: <c>Image.System("star.fill")</c>.</summary>
public sealed class Image : View
{
    readonly string _systemName;

    Image(string systemName) => _systemName = systemName;

    public static Image System(string systemName) => new(systemName);

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Image", path);
        node.Props["system"] = _systemName;
        return node;
    }
}

/// <summary>A title paired with a leading SF Symbol.</summary>
public sealed class Label : View
{
    readonly string _title;
    readonly string _systemImage;

    public Label(string title, string systemImage)
    {
        _title = title;
        _systemImage = systemImage;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Label", path);
        node.Props["title"] = _title;
        node.Props["systemImage"] = _systemImage;
        return node;
    }
}

/// <summary>A progress indicator: indeterminate spinner, or determinate when given a 0..1 value.</summary>
public sealed class ProgressView : View
{
    readonly string? _label;
    readonly double? _value;

    public ProgressView(string? label = null) => _label = label;
    public ProgressView(double value, string? label = null) { _value = value; _label = label; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("ProgressView", path);
        if (_label is not null) node.Props["label"] = _label;
        if (_value.HasValue) node.Props["value"] = _value.Value;
        return node;
    }
}

/// <summary>A thin separator line.</summary>
public sealed class Divider : View
{
    internal override Node BuildNode(RenderContext ctx, string path) => ctx.NewNode("Divider", path);
}

/// <summary>A gauge showing a value within a range.</summary>
public sealed class Gauge : View
{
    readonly string? _label;
    readonly double _value;
    readonly double _min;
    readonly double _max;

    public Gauge(double value, double min = 0, double max = 1, string? label = null)
    {
        _value = value;
        _min = min;
        _max = max;
        _label = label;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Gauge", path);
        node.Props["value"] = _value;
        node.Props["min"] = _min;
        node.Props["max"] = _max;
        if (_label is not null) node.Props["label"] = _label;
        return node;
    }
}

/// <summary>A tappable hyperlink that opens a URL.</summary>
public sealed class Link : View
{
    readonly string _title;
    readonly string _url;

    public Link(string title, string url)
    {
        _title = title;
        _url = url;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Link", path);
        node.Props["title"] = _title;
        node.Props["url"] = _url;
        return node;
    }
}

// Shapes -------------------------------------------------------------------

public sealed class Rectangle : View
{
    internal override Node BuildNode(RenderContext ctx, string path) => ctx.NewNode("Rectangle", path);
}

public sealed class Circle : View
{
    internal override Node BuildNode(RenderContext ctx, string path) => ctx.NewNode("Circle", path);
}

public sealed class Capsule : View
{
    internal override Node BuildNode(RenderContext ctx, string path) => ctx.NewNode("Capsule", path);
}

public sealed class RoundedRectangle : View
{
    readonly double _cornerRadius;
    public RoundedRectangle(double cornerRadius) => _cornerRadius = cornerRadius;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("RoundedRectangle", path);
        node.Props["cornerRadius"] = _cornerRadius;
        return node;
    }
}
