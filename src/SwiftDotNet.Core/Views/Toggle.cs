namespace SwiftDotNet;

/// <summary>
/// An on/off switch with two-way binding, rendered as a native SwiftUI <c>Toggle</c>. Pass a
/// <see cref="State{T}"/> of bool; flipping the switch updates the state and re-renders.
/// </summary>
public sealed class Toggle : View
{
    readonly string _label;
    readonly State<bool> _isOn;

    public Toggle(string label, State<bool> isOn)
    {
        _label = label;
        _isOn = isOn;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Toggle", path);
        node.Props["label"] = _label;
        node.Props["value"] = _isOn.Value;
        ctx.RegisterAction(node.Id, value => _isOn.Value = value == "true");
        return node;
    }
}
