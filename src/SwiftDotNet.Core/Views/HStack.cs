namespace SwiftDotNet;

public sealed class HStack : View
{
    readonly View[] _children;
    double? _spacing;
    VerticalAlignment? _alignment;

    public HStack(params View[] children) => _children = children;

    public HStack Spacing(double spacing) { _spacing = spacing; return this; }
    public HStack Alignment(VerticalAlignment alignment) { _alignment = alignment; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("HStack", path);
        if (_spacing.HasValue) node.Props["spacing"] = _spacing.Value;
        if (_alignment.HasValue) node.Props["alignment"] = _alignment.Value.Token();
        for (var i = 0; i < _children.Length; i++)
            node.Children.Add(_children[i].ToNode(ctx, path + "." + i));
        return node;
    }
}
