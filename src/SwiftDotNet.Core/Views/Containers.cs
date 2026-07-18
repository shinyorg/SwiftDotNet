namespace SwiftDotNet;

/// <summary>A scrolling container (vertical by default; call <see cref="Horizontal"/> for horizontal).</summary>
public sealed class ScrollView : View
{
    readonly View[] _children;
    bool _horizontal;

    public ScrollView(params View[] children) => _children = children;
    public ScrollView Horizontal() { _horizontal = true; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("ScrollView", path);
        if (_horizontal) node.Props["axis"] = "horizontal";
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>Depth-stacked layout (children overlaid back-to-front).</summary>
public sealed class ZStack : View
{
    readonly View[] _children;
    Alignment? _alignment;

    public ZStack(params View[] children) => _children = children;

    public ZStack Alignment(Alignment alignment) { _alignment = alignment; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("ZStack", path);
        if (_alignment.HasValue) node.Props["alignment"] = _alignment.Value.Token();
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A fixed-column grid (SwiftUI <c>LazyVGrid</c> with N flexible columns).</summary>
public sealed class Grid : View
{
    readonly int _columns;
    readonly View[] _children;
    double? _spacing;

    public Grid(int columns, params View[] children) { _columns = columns; _children = children; }
    public Grid Spacing(double spacing) { _spacing = spacing; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Grid", path);
        node.Props["columns"] = (double)_columns;
        if (_spacing.HasValue) node.Props["spacing"] = _spacing.Value;
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A grouped section with an optional header, for use inside <see cref="Form"/> or <see cref="List"/>.</summary>
public sealed class Section : View
{
    readonly string? _header;
    readonly View[] _children;

    public Section(string header, params View[] children) { _header = header; _children = children; }
    public Section(params View[] children) { _children = children; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Section", path);
        if (_header is not null) node.Props["header"] = _header;
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A settings-style grouped form.</summary>
public sealed class Form : View
{
    readonly View[] _children;
    public Form(params View[] children) => _children = children;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Form", path);
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A transparent grouping of views.</summary>
public sealed class Group : View
{
    readonly View[] _children;
    public Group(params View[] children) => _children = children;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Group", path);
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A collapsible section with a two-way bound expanded state.</summary>
public sealed class DisclosureGroup : View
{
    readonly string _label;
    readonly State<bool> _isExpanded;
    readonly View[] _children;

    public DisclosureGroup(string label, State<bool> isExpanded, params View[] children)
    {
        _label = label;
        _isExpanded = isExpanded;
        _children = children;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("DisclosureGroup", path);
        node.Props["label"] = _label;
        node.Props["expanded"] = _isExpanded.Value;
        ctx.RegisterAction(node.Id, value => _isExpanded.Value = value == "true");
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>A tabbed container. Children should be <see cref="Tab"/>s. Call <see cref="Paged"/> for a swipe carousel.</summary>
public sealed class TabView : View
{
    readonly View[] _tabs;
    bool _paged;

    public TabView(params View[] tabs) => _tabs = tabs;
    public TabView Paged() { _paged = true; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("TabView", path);
        if (_paged) node.Props["style"] = "page";
        NodeBuilder.AddChildren(node, _tabs, ctx, path);
        return node;
    }
}

/// <summary>One tab in a <see cref="TabView"/>: a title, an SF Symbol, and content.</summary>
public sealed class Tab : View
{
    readonly string _title;
    readonly string _systemImage;
    readonly View _content;

    public Tab(string title, string systemImage, View content)
    {
        _title = title;
        _systemImage = systemImage;
        _content = content;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Tab", path);
        node.Props["title"] = _title;
        node.Props["systemImage"] = _systemImage;
        node.Children.Add(_content.ToNode(ctx, path + ".0"));
        return node;
    }
}
