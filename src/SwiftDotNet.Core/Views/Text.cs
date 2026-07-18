namespace SwiftDotNet;

public sealed class Text : View
{
    readonly string _text;

    public Text(string text) => _text = text;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Text", path);
        node.Props["text"] = _text;
        return node;
    }
}
