namespace SwiftDotNet;

public sealed class Button : View
{
    readonly string _title;
    readonly Action _action;

    public Button(string title, Action action)
    {
        _title = title;
        _action = action;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Button", path);
        node.Props["title"] = _title;
        ctx.RegisterAction(node.Id, _ => _action());
        return node;
    }
}
