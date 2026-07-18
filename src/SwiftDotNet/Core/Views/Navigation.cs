namespace SwiftDotNet;

/// <summary>A navigation container. Wrap a root view; push with <see cref="NavigationLink"/>.</summary>
public sealed class NavigationStack : View
{
    readonly View _root;
    public NavigationStack(View root) => _root = root;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("NavigationStack", path);
        node.Children.Add(_root.ToNode(ctx, path + ".0"));
        return node;
    }
}

/// <summary>A tappable row that pushes a destination view. Child 0 is the label, child 1 the destination.</summary>
public sealed class NavigationLink : View
{
    readonly View _label;
    readonly View _destination;

    public NavigationLink(View label, View destination)
    {
        _label = label;
        _destination = destination;
    }

    public NavigationLink(string title, View destination) : this(new Text(title), destination) { }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("NavigationLink", path);
        node.Children.Add(_label.ToNode(ctx, path + ".0"));
        node.Children.Add(_destination.ToNode(ctx, path + ".1"));
        return node;
    }
}

/// <summary>Presents modal content when the bound flag is true. Child 0 is the body, child 1 the sheet content.</summary>
public sealed class Sheet : View
{
    readonly State<bool> _isPresented;
    readonly View _body;
    readonly View _content;

    public Sheet(State<bool> isPresented, View body, View content)
    {
        _isPresented = isPresented;
        _body = body;
        _content = content;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Sheet", path);
        node.Props["presented"] = _isPresented.Value;
        ctx.RegisterAction(node.Id, v => _isPresented.Value = v == "true");
        node.Children.Add(_body.ToNode(ctx, path + ".0"));
        node.Children.Add(_content.ToNode(ctx, path + ".1"));
        return node;
    }
}

/// <summary>Presents a standard alert (title, message, OK) when the bound flag is true. Child 0 is the body.</summary>
public sealed class Alert : View
{
    readonly State<bool> _isPresented;
    readonly string _title;
    readonly string _message;
    readonly View _body;

    public Alert(State<bool> isPresented, string title, string message, View body)
    {
        _isPresented = isPresented;
        _title = title;
        _message = message;
        _body = body;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Alert", path);
        node.Props["presented"] = _isPresented.Value;
        node.Props["title"] = _title;
        node.Props["message"] = _message;
        ctx.RegisterAction(node.Id, v => _isPresented.Value = v == "true");
        node.Children.Add(_body.ToNode(ctx, path + ".0"));
        return node;
    }
}
