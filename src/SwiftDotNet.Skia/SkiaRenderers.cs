using SkiaSharp;

namespace SwiftDotNet;

/// <summary>Context handed to a custom Skia renderer: the node's id/props plus an emit hook.</summary>
public sealed class SkiaRenderContext
{
    readonly Action<string, string?> _emit;

    internal SkiaRenderContext(string id, IReadOnlyDictionary<string, object?> props, Action<string, string?> emit)
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

/// <summary>
/// A custom renderer for a node type on the Skia backend. Because the backend owns the pixels, a
/// renderer must both <see cref="Measure"/> (intrinsic size) and <see cref="Paint"/> itself — the
/// self-drawing analog of GTK's <c>Create</c>/<c>Update</c> widget pair.
/// </summary>
public interface ISkiaRenderer
{
    SKSize Measure(SkiaRenderContext ctx, SKSize available);
    void Paint(SkiaRenderContext ctx, SKCanvas canvas, SKRect rect);
}

/// <summary>
/// Registry of custom Skia renderers, keyed by <see cref="CustomView.TypeName"/>. Mirrors
/// <c>GtkRenderers</c>; unregistered types fall back to a ⚠️ placeholder in the interpreter.
/// </summary>
public static class SkiaRenderers
{
    static readonly Dictionary<string, ISkiaRenderer> Map = new();

    public static void Register(string type, ISkiaRenderer renderer) => Map[type] = renderer;

    internal static ISkiaRenderer? Get(string type) => Map.GetValueOrDefault(type);
}
