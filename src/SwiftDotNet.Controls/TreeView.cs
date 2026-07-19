namespace SwiftDotNet.Controls;

/// <summary>A node in a <see cref="TreeView"/>: a label, an optional icon, and child nodes.</summary>
public sealed class TreeNode
{
    public string Label { get; }
    public string? Icon { get; }
    public IReadOnlyList<TreeNode> Children { get; }

    public TreeNode(string label, params TreeNode[] children) : this(label, null, children) { }

    public TreeNode(string label, string? icon, params TreeNode[] children)
    {
        Label = label;
        Icon = icon;
        Children = children;
    }

    internal bool HasChildren => Children.Count > 0;
}

/// <summary>
/// A hierarchical tree with expand/collapse — ported from Shiny's <c>TreeView</c>. A composite: branch
/// nodes use the built-in <see cref="DisclosureGroup"/> (each with its own persisted expand state), leaf
/// nodes are tappable rows. Renders on every backend with no native code.
/// </summary>
public sealed class TreeView : View
{
    readonly IReadOnlyList<TreeNode> _roots;
    Action<TreeNode>? _onSelect;

    // One expand-State per branch node, kept on the instance so it persists across renders.
    readonly Dictionary<TreeNode, State<bool>> _expanded = new();

    public TreeView(params TreeNode[] roots) => _roots = roots;

    /// <summary>Fires when a node is tapped.</summary>
    public TreeView OnSelect(Action<TreeNode> onSelect) { _onSelect = onSelect; return this; }

    public override View Body =>
        new VStack(_roots.Select(r => Render(r, 0)).ToArray())
            .Spacing(2)
            .Alignment(HorizontalAlignment.Leading);

    View Render(TreeNode node, int depth)
    {
        var indent = depth * 16;

        if (!node.HasChildren)
        {
            View leaf = new HStack(Label(node)).Spacing(6)
                .Padding(Edge.Leading, indent + 20)   // align under the disclosure caret
                .OnTapGesture(() => _onSelect?.Invoke(node));
            return leaf;
        }

        var state = _expanded.TryGetValue(node, out var s) ? s : (_expanded[node] = new State<bool>(false));
        var children = node.Children.Select(c => Render(c, depth + 1)).ToArray();
        return new DisclosureGroup(node.Label, state, children)
            .Padding(Edge.Leading, indent);
    }

    View Label(TreeNode node) => node.Icon is { } icon
        ? new HStack(Image.System(icon), new Text(node.Label)).Spacing(8).Alignment(VerticalAlignment.Center)
        : new Text(node.Label);
}
