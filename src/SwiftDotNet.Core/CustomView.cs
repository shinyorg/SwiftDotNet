namespace SwiftDotNet;

/// <summary>
/// Base for a <b>custom native primitive</b> — a control that isn't a composition of existing views.
/// It emits a node under your <see cref="TypeName"/>; each backend renders it via a renderer registered
/// under that same name (e.g. <c>GtkRenderers.Register</c>), or shows a placeholder if none is registered.
///
/// For most custom controls you don't need this — just subclass <see cref="View"/> and compose existing
/// views in <c>Body</c> (works on every backend with no native code).
/// </summary>
public abstract class CustomView : View
{
    /// <summary>The renderer key; register a per-backend renderer under this exact name.</summary>
    protected abstract string TypeName { get; }

    /// <summary>Set the props your renderer reads, and optionally register the control's event callback.</summary>
    protected abstract void Configure(CustomNode node);

    internal sealed override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode(TypeName, path);
        Configure(new CustomNode(node, ctx));
        return node;
    }
}

/// <summary>Fluent builder handed to <see cref="CustomView.Configure"/> for setting props and the event handler.</summary>
public sealed class CustomNode
{
    readonly Node _node;
    readonly RenderContext _ctx;

    internal CustomNode(Node node, RenderContext ctx) { _node = node; _ctx = ctx; }

    public string Id => _node.Id;

    public CustomNode Prop(string key, string value) { _node.Props[key] = value; return this; }
    public CustomNode Prop(string key, double value) { _node.Props[key] = value; return this; }
    public CustomNode Prop(string key, bool value) { _node.Props[key] = value; return this; }

    /// <summary>Register the callback invoked when the rendered control emits an event (value optional).</summary>
    public CustomNode OnEvent(Action<string?> handler) { _ctx.RegisterAction(_node.Id, handler); return this; }
}
