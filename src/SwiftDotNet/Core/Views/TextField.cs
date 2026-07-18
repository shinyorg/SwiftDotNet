namespace SwiftDotNet;

/// <summary>
/// A text entry field with two-way binding. Pass a <see cref="State{T}"/> of string; edits in the
/// SwiftUI field flow back and update the state, which re-renders anything derived from it.
/// </summary>
public sealed class TextField : View
{
    readonly string _placeholder;
    readonly State<string> _text;

    public TextField(string placeholder, State<string> text)
    {
        _placeholder = placeholder;
        _text = text;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("TextField", path);
        node.Props["placeholder"] = _placeholder;
        node.Props["text"] = _text.Value;
        ctx.RegisterAction(node.Id, value => _text.Value = value ?? "");
        return node;
    }
}
