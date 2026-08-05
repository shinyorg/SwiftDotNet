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

/// <summary>
/// A two-dimensional grid. In its simplest form it is N equal columns filled in reading order
/// (SwiftUI's <c>LazyVGrid</c>); call <see cref="Columns(GridTrack[])"/> / <see cref="Rows"/> for
/// per-track sizing, and place children with <c>.GridSpan(...)</c> / <c>.GridCell(...)</c>.
/// </summary>
/// <example>
/// <code>
/// new Grid(3,
///     new Text("Header").GridSpan(columns: 3),
///     new Text("Name"), new TextField(name), new Button("Save", Save))
///     .Columns(GridTrack.Fixed(80), GridTrack.Star(), GridTrack.Auto)
///     .ColumnSpacing(12)
///     .RowSpacing(6)
///     .Alignment(Alignment.Leading);
/// </code>
/// </example>
public sealed class Grid : View
{
    readonly int _columns;
    readonly View[] _children;
    GridTrack[]? _columnTracks;
    GridTrack[]? _rowTracks;
    double? _spacing;
    double? _rowSpacing;
    double? _columnSpacing;
    Alignment? _alignment;

    public Grid(int columns, params View[] children) { _columns = Math.Max(1, columns); _children = children; }

    /// <summary>A grid whose column count comes from <see cref="Columns(GridTrack[])"/>.</summary>
    public Grid(params View[] children) { _columns = 1; _children = children; }

    /// <summary>Spacing between cells on both axes. <see cref="RowSpacing"/>/<see cref="ColumnSpacing"/> override it per axis.</summary>
    public Grid Spacing(double spacing) { _spacing = spacing; return this; }

    /// <summary>Vertical gap between rows.</summary>
    public Grid RowSpacing(double spacing) { _rowSpacing = spacing; return this; }

    /// <summary>Horizontal gap between columns.</summary>
    public Grid ColumnSpacing(double spacing) { _columnSpacing = spacing; return this; }

    /// <summary>Sizes each column explicitly. The track count replaces the constructor's column count.</summary>
    public Grid Columns(params GridTrack[] tracks) { _columnTracks = tracks; return this; }

    /// <summary>Sizes rows explicitly. Rows past the last track fall back to <see cref="GridTrack.Auto"/>.</summary>
    public Grid Rows(params GridTrack[] tracks) { _rowTracks = tracks; return this; }

    /// <summary>How each child sits inside its cell when the cell is bigger than the child (default centered).</summary>
    public Grid Alignment(Alignment alignment) { _alignment = alignment; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Grid", path);
        // `columns` stays the single source of truth for the column count — explicit tracks just
        // redefine it — so a backend that only understands uniform grids still lays out the right shape.
        var cols = _columnTracks is { Length: > 0 } ? _columnTracks.Length : _columns;
        node.Props["columns"] = (double)cols;
        if (_columnTracks is { Length: > 0 }) node.Props["columnTracks"] = GridTrack.Join(_columnTracks);
        if (_rowTracks is { Length: > 0 }) node.Props["rowTracks"] = GridTrack.Join(_rowTracks);
        if (_spacing.HasValue) node.Props["spacing"] = _spacing.Value;
        if (_rowSpacing.HasValue) node.Props["rowSpacing"] = _rowSpacing.Value;
        if (_columnSpacing.HasValue) node.Props["columnSpacing"] = _columnSpacing.Value;
        if (_alignment.HasValue) node.Props["alignment"] = _alignment.Value.Token();
        NodeBuilder.AddChildren(node, _children, ctx, path);
        return node;
    }
}

/// <summary>
/// A layout that positions each child at coordinates you give it, rather than flowing them. Children
/// carry their rect via <c>.LayoutBounds(...)</c>; coordinates and sizes can be points or fractions of
/// the layout's own size (see <see cref="LayoutFlags"/>). Mirrors MAUI's <c>AbsoluteLayout</c> — SwiftUI
/// has no direct analog, so on Apple it lowers to a <c>GeometryReader</c> + offset overlay.
/// </summary>
/// <example>
/// <code>
/// new AbsoluteLayout(
///     new Rectangle().ForegroundColor(SwiftColor.Blue).LayoutBounds(0, 0, 1, 1, LayoutFlags.SizeProportional),
///     new Text("badge").LayoutBounds(12, 12),
///     new Button("Close", Close).LayoutBounds(1, 0, 80, 32, LayoutFlags.XProportional))
///     .Frame(height: 200);
/// </code>
/// </example>
public sealed class AbsoluteLayout : View
{
    /// <summary>Pass as a <c>.LayoutBounds</c> width/height to let the child size itself.</summary>
    public const double AutoSize = -1;

    readonly View[] _children;

    public AbsoluteLayout(params View[] children) => _children = children;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("AbsoluteLayout", path);
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
    State<int>? _selectedIndex;
    bool _hidePageIndicator;

    public TabView(params View[] tabs) => _tabs = tabs;

    /// <summary>Render as a swipeable carousel (paged) rather than a tab bar.</summary>
    public TabView Paged() { _paged = true; return this; }

    /// <summary>Two-way binding of the current tab/page. Assign the state to switch pages programmatically;
    /// it updates when the user selects a tab or swipes the carousel.</summary>
    public TabView SelectedIndex(State<int> index) { _selectedIndex = index; return this; }

    /// <summary>Hide the paged carousel's page-indicator dots.</summary>
    public TabView HidePageIndicator() { _hidePageIndicator = true; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("TabView", path);
        if (_paged) node.Props["style"] = "page";
        if (_hidePageIndicator) node.Props["pageIndicator"] = false;
        if (_selectedIndex is not null)
        {
            node.Props["selectedIndex"] = (double)_selectedIndex.Value;
            // Host emits the newly selected index as the value; map it back to the bound state.
            ctx.RegisterAction(node.Id, val => { if (int.TryParse(val, out var i)) _selectedIndex.Value = i; });
        }
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
