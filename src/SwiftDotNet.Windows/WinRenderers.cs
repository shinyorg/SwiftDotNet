using Microsoft.UI.Xaml;

namespace SwiftDotNet;

/// <summary>Context passed to a custom WinUI renderer: the node's props, id, and an emit hook.</summary>
public sealed class WinRenderContext
{
    readonly Action<string, string?> _emit;

    internal WinRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
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

public interface IWinRenderer
{
    FrameworkElement Create(WinRenderContext ctx);
    void Update(FrameworkElement element, WinRenderContext ctx) { }
}

/// <summary>
/// Registry of custom WinUI renderers — plug a native WinUI control in for a
/// <see cref="CustomView.TypeName"/> without forking the built-in interpreter:
/// <code>
/// WinRenderers.Register("NativeRating", ctx => {
///     var slider = new Microsoft.UI.Xaml.Controls.Slider { Minimum = 0, Maximum = 5, Value = ctx.Number("value") ?? 0 };
///     slider.ValueChanged += (_, e) => ctx.Emit(((int)e.NewValue).ToString());
///     return slider;
/// });
/// </code>
/// </summary>
public static class WinRenderers
{
    static readonly Dictionary<string, IWinRenderer> Map = new();

    public static void Register(string type, IWinRenderer renderer) => Map[type] = renderer;

    public static void Register(string type, Func<WinRenderContext, FrameworkElement> create)
        => Map[type] = new DelegateRenderer(create);

    internal static IWinRenderer? Get(string type) => Map.GetValueOrDefault(type);

    sealed class DelegateRenderer(Func<WinRenderContext, FrameworkElement> create) : IWinRenderer
    {
        public FrameworkElement Create(WinRenderContext ctx) => create(ctx);
    }
}
