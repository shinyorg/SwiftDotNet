namespace SwiftDotNet;

public sealed class VStack : View
{
    readonly View[] _children;
    double? _spacing;
    HorizontalAlignment? _alignment;

    public VStack(params View[] children) => _children = children;

    public VStack Spacing(double spacing) { _spacing = spacing; return this; }
    public VStack Alignment(HorizontalAlignment alignment) { _alignment = alignment; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("VStack", path);
        if (_spacing.HasValue) node.Props["spacing"] = _spacing.Value;
        if (_alignment.HasValue) node.Props["alignment"] = _alignment.Value.Token();
        for (var i = 0; i < _children.Length; i++)
            node.Children.Add(_children[i].ToNode(ctx, path + "." + i));
        return node;
    }
}
