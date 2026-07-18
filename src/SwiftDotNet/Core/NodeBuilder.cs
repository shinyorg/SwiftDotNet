namespace SwiftDotNet;

internal static class NodeBuilder
{
    public static void AddChildren(Node node, View[] children, RenderContext ctx, string path)
    {
        for (var i = 0; i < children.Length; i++)
            node.Children.Add(children[i].ToNode(ctx, path + "." + i));
    }
}
