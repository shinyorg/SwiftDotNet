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

    /// <summary>Deferred reusable-style bundles (<c>.Style</c>/<c>.CardStyle</c>). Resolved during the render
    /// pass — not when attached — so a bundle can read the ambient <see cref="Theme"/> in effect at render.</summary>
    List<Action<ViewStyleBuilder>>? _styles;

    internal void AddStyle(Action<ViewStyleBuilder> configure) => (_styles ??= new()).Add(configure);

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
        // Reusable style bundles resolve here, at render time, so a bundle can read the ambient Theme
        // (EnvironmentValues.Current). Applied before the environment cascade so an inherited font/color
        // treats a value the bundle already set as "explicit" and doesn't duplicate it.
        if (_styles is { } styles)
        {
            var b = new ViewStyleBuilder();
            for (var i = 0; i < styles.Count; i++) styles[i](b);
            for (var i = 0; i < b.Modifiers.Count; i++)
                node.Modifiers.Add(b.Modifiers[i].Serialize(ctx, path + "$s" + i));
        }
        // Resolve the ambient style cascade in C#: any font/foregroundColor/control-style not set
        // explicitly above is inherited here, so the node ships to every backend fully resolved.
        // No-op unless the view is under an EnvironmentScope.
        EnvironmentValues.Current.InjectDefaults(ctx, node);
        return node;
    }

    /// <summary>Declares SwiftUI-style local state. Mirrors <c>@State private var x = ...</c>.</summary>
    protected static State<T> State<T>(T initialValue) => new(initialValue);
}
