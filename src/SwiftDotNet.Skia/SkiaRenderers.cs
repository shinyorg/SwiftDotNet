using SkiaSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>Context handed to a custom Skia renderer: the node's id/props plus an emit hook.</summary>
public sealed class SkiaRenderContext
{
    readonly VisualRenderContext _inner;

    internal SkiaRenderContext(VisualRenderContext inner) => _inner = inner;

    public string Id => _inner.Id;
    public IReadOnlyDictionary<string, object?> Props => _inner.Props;

    /// <summary>Raise this control's event back to its C# handler.</summary>
    public void Emit(string? value = null) => _inner.Emit(value);

    public string String(string key) => _inner.String(key);
    public double? Number(string key) => _inner.Number(key);
    public bool Bool(string key) => _inner.Bool(key);
}

/// <summary>
/// A custom renderer for a node type on the Skia backend, drawing directly onto an <see cref="SKCanvas"/>.
/// </summary>
/// <remarks>
/// Prefer <see cref="IVisualRenderer"/> for new code: it draws through the engine's rasterizer-neutral
/// <see cref="ICanvas"/>, so the same renderer works on the WebGPU and Unity backends too. This interface
/// remains supported and is bridged onto that seam by <see cref="SkiaRenderers.Register"/>, but it is
/// inherently Skia-only — a renderer registered through it draws nothing on a non-Skia canvas.
/// </remarks>
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
    /// <summary>Registers a Skia-typed renderer, adapting it onto the shared renderer registry.</summary>
    public static void Register(string type, ISkiaRenderer renderer) =>
        VisualRenderers.Register(type, new Adapter(renderer));

    /// <summary>Registers a rasterizer-neutral renderer (equivalent to <see cref="VisualRenderers.Register"/>).</summary>
    public static void Register(string type, IVisualRenderer renderer) =>
        VisualRenderers.Register(type, renderer);

    sealed class Adapter(ISkiaRenderer inner) : IVisualRenderer
    {
        public Graphics.Size Measure(VisualRenderContext ctx, Graphics.Size available)
        {
            var s = inner.Measure(new SkiaRenderContext(ctx), new SKSize(available.Width, available.Height));
            return new Graphics.Size(s.Width, s.Height);
        }

        public void Paint(VisualRenderContext ctx, ICanvas canvas, Graphics.Rect rect)
        {
            // An ISkiaRenderer needs a real SKCanvas. On any other rasterizer there is nothing sensible to
            // hand it, so the node draws nothing rather than the backend guessing.
            if (canvas is not SkiaCanvas skia) return;
            inner.Paint(new SkiaRenderContext(ctx), skia.Native, SkiaCanvas.ToSk(rect));
        }
    }
}

/// <summary>
/// Process-wide async cache for remote (<c>Image.FromUrl</c>) images.
/// </summary>
/// <remarks>Retained for continuity; the implementation now lives in <see cref="ImageLoader"/>, which is
/// shared by every self-drawing backend.</remarks>
public static class SkiaImageLoader
{
    /// <inheritdoc cref="ImageLoader.Http"/>
    public static HttpClient Http
    {
        get => ImageLoader.Http;
        set => ImageLoader.Http = value;
    }

    /// <inheritdoc cref="ImageLoader.Clear"/>
    public static void Clear() => ImageLoader.Clear();
}
