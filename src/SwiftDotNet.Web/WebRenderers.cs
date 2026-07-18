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
/// Registry of custom web renderers — plug an HTML rendering for a <see cref="CustomView.TypeName"/> in
/// without forking the built-in interpreter.
/// </summary>
public static class WebRenderers
{
    static readonly Dictionary<string, WebRenderer> Map = new();

    public static void Register(string type, WebRenderer renderer) => Map[type] = renderer;

    internal static WebRenderer? Get(string type) => Map.GetValueOrDefault(type);
}
