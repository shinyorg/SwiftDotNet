using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SwiftDotNet;

/// <summary>Context passed to a custom web renderer: the node's props, id, and an emit hook.</summary>
public sealed class WebRenderContext
{
    readonly Action<string, string?> _emit;

    internal WebRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
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

/// <summary>Emits a custom node's HTML into the render tree. Advance <paramref name="seq"/> for each element/attribute.</summary>
public delegate void WebRenderer(RenderTreeBuilder builder, WebRenderContext ctx, ref int seq);

/// <summary>
/// Base for a <b>persistent, stateful</b> custom renderer — a Blazor component kept alive across renders
/// (keyed by node id) instead of the stateless <see cref="WebRenderer"/> delegate that re-emits every time.
/// Use this for controls that own external/imperative state (a map, a chart, a video player): hold the JS
/// handle in the component and reconcile it on <see cref="ComponentBase.OnParametersSetAsync"/> /
/// <c>OnAfterRenderAsync</c> instead of rebuilding. <see cref="Props"/> carries the node's props; call
/// <see cref="Emit"/> to raise the control's event back to C#.
/// </summary>
public abstract class WebCustomComponent : ComponentBase
{
    [Parameter] public string NodeId { get; set; } = "";
    [Parameter] public IReadOnlyDictionary<string, object?> Props { get; set; } = default!;
    [Parameter] public Action<string, string?> Emit { get; set; } = default!;

    protected string String(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    protected double? Number(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    protected bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
}

/// <summary>
/// Registry of custom web renderers — plug an HTML rendering for a <see cref="CustomView.TypeName"/> in
/// without forking the built-in interpreter. Register either a stateless <see cref="WebRenderer"/> delegate
/// or a persistent <see cref="WebCustomComponent"/> type.
/// </summary>
public static class WebRenderers
{
    static readonly Dictionary<string, WebRenderer> Map = new();
    static readonly Dictionary<string, Type> Components = new();

    public static void Register(string type, WebRenderer renderer) => Map[type] = renderer;

    /// <summary>Register a persistent component renderer (a <see cref="WebCustomComponent"/> subclass).</summary>
    public static void Register<TComponent>(string type) where TComponent : WebCustomComponent
        => Components[type] = typeof(TComponent);

    internal static WebRenderer? Get(string type) => Map.GetValueOrDefault(type);
    internal static Type? GetComponent(string type) => Components.GetValueOrDefault(type);
}
