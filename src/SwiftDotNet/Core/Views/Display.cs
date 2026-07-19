namespace SwiftDotNet;

/// <summary>How a raster <see cref="Image"/> fills its frame (mirrors SwiftUI's <c>.resizable().scaledToFit/Fill</c>).</summary>
public enum ImageContentMode { Fit, Fill }

/// <summary>
/// An image. <see cref="System"/> is an SF Symbol (mapped to an emoji glyph on backends without SF
/// Symbols); <see cref="FromUrl"/>/<see cref="FromFile"/>/<see cref="FromBytes"/> are real raster images
/// (F3) loaded by each backend's native image pipeline. Bytes cross the wire as a base64 string prop.
/// </summary>
public sealed class Image : View
{
    readonly string _kind;   // "system" | "url" | "file" | "bytes"
    readonly string _value;
    ImageContentMode _mode = ImageContentMode.Fit;

    Image(string kind, string value) { _kind = kind; _value = value; }

    /// <summary>An SF Symbol by name: <c>Image.System("star.fill")</c>.</summary>
    public static Image System(string systemName) => new("system", systemName);

    /// <summary>A remote image loaded from a URL.</summary>
    public static Image FromUrl(string url) => new("url", url);

    /// <summary>A local image loaded from a file path (or a server-relative path on Web).</summary>
    public static Image FromFile(string path) => new("file", path);

    /// <summary>An in-memory image (e.g. PNG bytes); crosses the bridge as a base64 string.</summary>
    public static Image FromBytes(byte[] bytes) => new("bytes", Convert.ToBase64String(bytes));

    /// <summary>Sets how the raster image fills its frame (no effect on SF Symbols).</summary>
    public Image ContentMode(ImageContentMode mode) { _mode = mode; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Image", path);
        node.Props[_kind] = _value;
        if (_kind != "system") node.Props["contentMode"] = _mode == ImageContentMode.Fill ? "fill" : "fit";
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
