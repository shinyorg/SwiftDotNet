using System.Windows;

namespace SwiftDotNet;

/// <summary>Context passed to a custom WPF renderer: the node's props, id, and an emit hook.</summary>
public sealed class WpfRenderContext
{
    readonly Action<string, string?> _emit;

    internal WpfRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
    {
        Id = id;
        Props = props;
        _emit = emit;
    }

    public string Id { get; }
    public IReadOnlyDictionary<string, object?> Props { get; }

    public void Emit(string? value = null) => _emit(Id, value);

    public string String(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    public double? Number(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    public bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
}

public interface IWpfRenderer
{
    FrameworkElement Create(WpfRenderContext ctx);
    void Update(FrameworkElement element, WpfRenderContext ctx) { }
}

/// <summary>
/// Registry of custom WPF renderers — plug a native WPF control in for a
/// <see cref="CustomView.TypeName"/> without forking the built-in interpreter:
/// <code>
/// WpfRenderers.Register("NativeRating", ctx => {
///     var slider = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 5, Value = ctx.Number("value") ?? 0 };
///     slider.ValueChanged += (_, e) => ctx.Emit(((int)e.NewValue).ToString());
///     return slider;
/// });
/// </code>
/// </summary>
public static class WpfRenderers
{
    static readonly Dictionary<string, IWpfRenderer> Map = new();

    public static void Register(string type, IWpfRenderer renderer) => Map[type] = renderer;

    public static void Register(string type, Func<WpfRenderContext, FrameworkElement> create)
        => Map[type] = new DelegateRenderer(create);

    internal static IWpfRenderer? Get(string type) => Map.GetValueOrDefault(type);

    sealed class DelegateRenderer(Func<WpfRenderContext, FrameworkElement> create) : IWpfRenderer
    {
        public FrameworkElement Create(WpfRenderContext ctx) => create(ctx);
    }
}
