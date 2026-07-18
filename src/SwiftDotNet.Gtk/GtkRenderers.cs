namespace SwiftDotNet;

/// <summary>Context passed to a custom GTK renderer: the node's props, id, and an emit hook.</summary>
public sealed class GtkRenderContext
{
    readonly Action<string, string?> _emit;

    internal GtkRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
    {
        Id = id;
        Props = props;
        _emit = emit;
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, object?> Props { get; }

    /// <summary>Raise this control's event back to its C# handler.</summary>
    public void Emit(string? value = null) => _emit(Id, value);

    public string String(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    public double? Number(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    public bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
}

/// <summary>A custom renderer for a node type. <see cref="Update"/> re-syncs on patch (default: no-op).</summary>
public interface IGtkRenderer
{
    Gtk.Widget Create(GtkRenderContext ctx);
    void Update(Gtk.Widget widget, GtkRenderContext ctx) { }
}

/// <summary>
/// Registry of custom GTK renderers. Register a renderer for a <see cref="CustomView.TypeName"/> to plug in
/// a native GTK widget without forking the built-in interpreter:
/// <code>
/// GtkRenderers.Register("NativeRating", ctx => {
///     var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 5, 1);
///     scale.SetValue(ctx.Number("value") ?? 0);
///     scale.OnValueChanged += (_, _) => ctx.Emit(((int)scale.GetValue()).ToString());
///     return scale;
/// });
/// </code>
/// </summary>
public static class GtkRenderers
{
    static readonly Dictionary<string, IGtkRenderer> Map = new();

    public static void Register(string type, IGtkRenderer renderer) => Map[type] = renderer;

    public static void Register(string type, Func<GtkRenderContext, Gtk.Widget> create)
        => Map[type] = new DelegateRenderer(create);

    internal static IGtkRenderer? Get(string type) => Map.GetValueOrDefault(type);

    sealed class DelegateRenderer(Func<GtkRenderContext, Gtk.Widget> create) : IGtkRenderer
    {
        public Gtk.Widget Create(GtkRenderContext ctx) => create(ctx);
    }
}
