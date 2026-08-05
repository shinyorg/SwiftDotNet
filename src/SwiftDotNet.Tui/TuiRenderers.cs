using XenoAtom.Terminal.UI;

namespace SwiftDotNet;

/// <summary>Context passed to a custom terminal renderer: the node's props, id, and an emit hook.</summary>
public sealed class TuiRenderContext
{
    readonly Action<string, string?> _emit;

    internal TuiRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
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
public interface ITuiRenderer
{
    Visual Create(TuiRenderContext ctx);
    void Update(Visual visual, TuiRenderContext ctx) { }
}

/// <summary>
/// Registry of custom terminal renderers. Register one for a <see cref="CustomView.TypeName"/> to plug a
/// real Terminal.UI control into the tree without forking the built-in interpreter. This is also the
/// intended door to Terminal.UI's terminal-only controls — <c>Table</c>, <c>DataGridControl</c>,
/// <c>BarChart</c>, <c>Sparkline</c>, <c>CodeEditor</c>, <c>TreeView</c> — which have no Core node type:
/// <code>
/// TuiRenderers.Register("NativeRating", ctx =>
/// {
///     var slider = new Slider&lt;double&gt;(0, 5, ctx.Number("value") ?? 0) { Step = 1, SnapToStep = true };
///     slider.ValueChangedEvent.AddHandler(slider, (_, _) =&gt; ctx.Emit(((int)slider.Value).ToString()));
///     return slider;
/// });
/// </code>
/// </summary>
public static class TuiRenderers
{
    static readonly Dictionary<string, ITuiRenderer> Map = new();

    public static void Register(string type, ITuiRenderer renderer) => Map[type] = renderer;

    public static void Register(string type, Func<TuiRenderContext, Visual> create)
        => Map[type] = new DelegateRenderer(create);

    internal static ITuiRenderer? Get(string type) => Map.GetValueOrDefault(type);

    sealed class DelegateRenderer(Func<TuiRenderContext, Visual> create) : ITuiRenderer
    {
        public Visual Create(TuiRenderContext ctx) => create(ctx);
    }
}
