using SwiftDotNet;

namespace SwiftDotNet.Graphics;

/// <summary>
/// Context handed to a custom renderer: the node's identity and props, an emit hook, and the pieces of
/// the rasterizer a renderer needs to draw more than shapes — the font provider, the image decoder, the
/// current appearance, and a way to ask for another frame.
/// </summary>
public sealed class VisualRenderContext
{
    readonly VisualBridge _bridge;

    internal VisualRenderContext(string id, string type, IReadOnlyDictionary<string, object?> props,
        VisualBridge bridge, float cornerRadius = 0)
    {
        Id = id;
        NodeType = type;
        Props = props;
        _bridge = bridge;
        CornerRadius = cornerRadius;
    }

    public string Id { get; }

    /// <summary>The node's type name — the key this renderer was registered under.</summary>
    public string NodeType { get; }

    public IReadOnlyDictionary<string, object?> Props { get; }

    /// <summary>
    /// The node's <c>.CornerRadius</c>, so a renderer that draws its own content can round it. Only
    /// meaningful on a canvas that implements <see cref="IRoundedClipCanvas"/>; 0 when unset.
    /// </summary>
    public float CornerRadius { get; }

    /// <summary>The rasterizer's font provider, for measuring and drawing text.</summary>
    public IFontProvider Fonts => _bridge.Fonts;

    /// <summary>
    /// The rasterizer's image decoder. Cast it to <see cref="IPixelUploader"/> to turn raw pixels
    /// (a video frame, a generated bitmap) into something <see cref="ICanvas.DrawImage"/> can draw.
    /// </summary>
    public IImageDecoder Images => _bridge.Images;

    /// <summary>True when the current frame is painting in dark appearance.</summary>
    public bool Dark => _bridge.Dark;

    /// <summary>
    /// Ask the host to repaint. Safe from any thread — a renderer with an asynchronous source (a
    /// download, a decoder) calls this when new content lands.
    /// </summary>
    public void RequestRepaint() => _bridge.RequestRepaint();

    /// <summary>The bridge's live video surfaces — how a video-bearing node reaches its frame source.</summary>
    internal FrameSurfaces Surfaces => _bridge.Surfaces;

    /// <summary>Raise this control's event back to its C# handler.</summary>
    public void Emit(string? value = null) => _bridge.Emit(Id, value);

    public string String(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    public double? Number(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    public bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
}

/// <summary>
/// A custom renderer for a node type on a self-drawing backend. Because the backend owns the pixels, a
/// renderer must both <see cref="Measure"/> (intrinsic size) and <see cref="Paint"/> itself — the
/// self-drawing analog of GTK's <c>Create</c>/<c>Update</c> widget pair.
/// </summary>
public interface IVisualRenderer
{
    Size Measure(VisualRenderContext ctx, Size available);
    void Paint(VisualRenderContext ctx, ICanvas canvas, Rect rect);
}

/// <summary>
/// Registry of custom renderers, keyed by <see cref="CustomView.TypeName"/>. Mirrors
/// <c>GtkRenderers</c>; unregistered types fall back to a ⚠️ placeholder in the interpreter.
/// </summary>
public static class VisualRenderers
{
    static readonly Dictionary<string, IVisualRenderer> Map = new();

    public static void Register(string type, IVisualRenderer renderer) => Map[type] = renderer;

    /// <summary>
    /// The renderer for a node type: whatever was registered, else the engine's own renderer for the
    /// video-bearing types.
    /// </summary>
    /// <remarks>
    /// <c>VideoView</c> and <c>CameraView</c> resolve to <see cref="SurfaceRenderer"/> without any
    /// registration, so a video node draws a feed (or an honest placeholder) on every self-drawing
    /// backend out of the box. An explicit <see cref="Register"/> for those types still wins.
    /// </remarks>
    internal static IVisualRenderer? Get(string type) =>
        Map.TryGetValue(type, out var registered) ? registered : BuiltIn(type);

    static readonly SurfaceRenderer Video = new();

    static IVisualRenderer? BuiltIn(string type) =>
        type is SurfaceRenderer.VideoNode or SurfaceRenderer.CameraNode ? Video : null;
}
