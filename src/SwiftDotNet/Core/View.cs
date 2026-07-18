namespace SwiftDotNet;

/// <summary>
/// Base type for all views, mirroring SwiftUI's <c>View</c> protocol.
///
/// Composite views (your screens) override <see cref="Body"/>; leaf/primitive views override
/// <see cref="BuildNode"/> to emit their node. Modifiers accumulated via the fluent extensions in
/// <see cref="ViewModifiers"/> are applied (in order) on top of whichever node a view produces.
///
/// <paramref name="path"/> is the structural id of this view's position in the tree; container
/// views pass <c>path + "." + index</c> to each child.
/// </summary>
public abstract class View
{
    internal readonly List<Modifier> Modifiers = new();

    /// <summary>The view's content, re-evaluated on every render — the C# analog of <c>var body: some View</c>.</summary>
    public virtual View? Body => null;

    /// <summary>Leaf views override this to emit their node. Composites default to rendering their <see cref="Body"/>.</summary>
    internal virtual Node BuildNode(RenderContext ctx, string path)
    {
        var body = Body ?? throw new InvalidOperationException(
            $"{GetType().Name} is a composite view but returned a null Body.");
        return body.ToNode(ctx, path);
    }

    internal Node ToNode(RenderContext ctx, string path)
    {
        var node = BuildNode(ctx, path);
        for (var i = 0; i < Modifiers.Count; i++)
            node.Modifiers.Add(Modifiers[i].Serialize(ctx, path + "$" + i));
        return node;
    }

    /// <summary>Declares SwiftUI-style local state. Mirrors <c>@State private var x = ...</c>.</summary>
    protected static State<T> State<T>(T initialValue) => new(initialValue);
}
