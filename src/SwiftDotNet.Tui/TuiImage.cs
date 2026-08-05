using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Layout;
using XenoAtom.Terminal.UI.Rendering;

using TColor = XenoAtom.Terminal.UI.Color;
using TRect = XenoAtom.Terminal.UI.Geometry.Rectangle;

namespace SwiftDotNet;

/// <summary>
/// Global knobs for how the terminal backend draws <c>Image</c> nodes.
/// </summary>
public static class TuiImageOptions
{
    /// <summary>
    /// Which character-art mode to use. <see cref="TuiImageMode.Auto"/> picks from the terminal's colour
    /// support at render time.
    /// </summary>
    public static TuiImageMode Mode { get; set; } = TuiImageMode.Auto;

    /// <summary>Columns an image gets when neither the node nor a <c>.Frame</c> pins a size.</summary>
    public static int DefaultColumns { get; set; } = 32;

    /// <summary>
    /// Replaces the visual an <c>Image</c> node builds. <c>SwiftDotNet.Tui.Graphics</c> installs a factory
    /// here that emits a real Sixel/Kitty image with the character-art visual as its fallback content;
    /// leaving it null keeps images as character art everywhere.
    /// </summary>
    public static Func<TuiImageRequest, Visual>? VisualFactory { get; set; }

    /// <summary>
    /// The graphics presenter <see cref="SwiftDotNetHost"/> hands to the terminal app, set by
    /// <c>TuiGraphics.Enable()</c>. Kept here so enabling real images is one call in app code with no
    /// XenoAtom types named at the call site.
    /// </summary>
    public static ITerminalGraphicsPresenter? GraphicsPresenter { get; set; }
}

/// <summary>
/// What an <c>Image</c> node is asking for, handed to <see cref="TuiImageOptions.VisualFactory"/>.
/// <see cref="Fallback"/> is the character-art visual the core backend already built, so a factory can
/// use it as its own degradation path rather than rebuilding one.
/// </summary>
/// <param name="Bytes">Encoded image bytes, when the node carried or fetched them.</param>
/// <param name="File">A local file path, when the node named one.</param>
/// <param name="Url">A remote URL, when the node named one.</param>
/// <param name="Fill">True for <c>.ContentMode(.fill)</c> — crop to fill rather than fit inside.</param>
/// <param name="Fallback">The character-art visual, already wired to the same source.</param>
public readonly record struct TuiImageRequest(
    byte[]? Bytes, string? File, string? Url, bool Fill, Visual Fallback);

/// <summary>
/// Builds the visual for an <c>Image</c> node. Mirrors the GTK backend's source precedence
/// (<c>url</c> → <c>file</c> → <c>bytes</c> → SF-Symbol glyph) and its async-fetch behaviour: a remote
/// image renders empty for a frame and fills in once the download lands.
/// </summary>
static class TuiImage
{
    /// <summary>Shared fetcher for <c>Image.FromUrl</c> — one connection pool for the whole app.</summary>
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Decoded images keyed by URL, so a re-render never re-downloads.</summary>
    static readonly Dictionary<string, TuiPixels> UrlCache = new();

    public static Visual Create(TuiNode node, TuiBridge bridge)
    {
        var art = new TuiArtVisual { Fill = node.Str("contentMode") == "fill" };
        art.SourceKey = SourceKey(node);
        Load(node, bridge, art);

        if (TuiImageOptions.VisualFactory is not { } factory) return art;

        var bytes = node.Str("bytes") is { Length: > 0 } b64 ? SafeBase64(b64) : null;
        return factory(new TuiImageRequest(
            bytes,
            node.Str("file") is { Length: > 0 } f ? f : null,
            node.Str("url") is { Length: > 0 } u ? u : null,
            art.Fill,
            art));
    }

    public static void Update(TuiNode node, Visual visual, TuiBridge bridge)
    {
        if (Find(visual) is not { } art) return;
        art.Fill = node.Str("contentMode") == "fill";

        // Only re-decode when the source actually changed. Without this guard every unrelated patch —
        // and every keyed row that merely moved — would re-run base64 + PNG decode for an image whose
        // bytes are identical, which is the most expensive thing this backend does per frame.
        var key = SourceKey(node);
        if (key == art.SourceKey) return;
        art.SourceKey = key;
        Load(node, bridge, art);
    }

    /// <summary>Identifies an image node's source, so an unchanged one can skip decoding.</summary>
    static string SourceKey(TuiNode node)
        => $"{node.Str("url")}|{node.Str("file")}|{node.Str("system")}|{node.Str("bytes").Length}:{node.Str("bytes").GetHashCode()}";

    /// <summary>The art visual inside whatever a <see cref="TuiImageOptions.VisualFactory"/> wrapped it in.</summary>
    static TuiArtVisual? Find(Visual visual) => visual as TuiArtVisual
        ?? visual.EnumerateVisualsDepthFirst().OfType<TuiArtVisual>().FirstOrDefault();

    static void Load(TuiNode node, TuiBridge bridge, TuiArtVisual art)
    {
        var url = node.Str("url");
        if (url.Length > 0)
        {
            if (UrlCache.TryGetValue(url, out var cached)) art.Pixels = cached;
            else LoadUrlAsync(url, art, bridge);
            return;
        }

        var file = node.Str("file");
        if (file.Length > 0)
        {
            try { art.Pixels = TuiImageDecoders.Decode(System.IO.File.ReadAllBytes(file)); }
            catch { art.Pixels = null; }
            return;
        }

        var b64 = node.Str("bytes");
        if (b64.Length > 0)
        {
            var bytes = SafeBase64(b64);
            art.Pixels = bytes is null ? null : TuiImageDecoders.Decode(bytes);
            return;
        }

        // Only fall back to a glyph when an SF Symbol was actually requested; a raster image that failed
        // to load stays empty so the caller's own placeholder shows through — same rule as GTK.
        art.Pixels = null;
        art.AltText = node.Str("system") is { Length: > 0 } symbol ? TuiStyle.Glyph(symbol) : "";
    }

    static byte[]? SafeBase64(string b64)
    {
        try { return Convert.FromBase64String(b64); }
        catch { return null; }
    }

    /// <summary>
    /// Fetches and decodes off the UI thread, then hands the pixels back on it — Terminal.UI visuals are
    /// dispatcher-affine. Every failure (DNS, status, timeout, undecodable payload) is swallowed and the
    /// image simply stays empty; it never throws and never faults an unobserved task.
    /// </summary>
    static void LoadUrlAsync(string url, TuiArtVisual art, TuiBridge bridge)
    {
        _ = Task.Run(async () =>
        {
            TuiPixels? pixels;
            try { pixels = TuiImageDecoders.Decode(await Http.GetByteArrayAsync(url).ConfigureAwait(false)); }
            catch { return; }
            if (pixels is null) return;

            void Apply()
            {
                UrlCache[url] = pixels;
                art.Pixels = pixels;
                bridge.App?.RequestFullRender();
            }

            if (bridge.App is { } app) app.Post(Apply);
            else Apply();
        });
    }
}

/// <summary>
/// The visual that paints an image as characters. A first-class <c>Visual</c> rather than a
/// <c>Canvas</c> painter because it needs to <em>measure</em> — an image's cell size comes from its own
/// aspect ratio corrected for the terminal's ~2:1 cell, which a canvas has no way to report.
/// </summary>
sealed class TuiArtVisual : Visual
{
    public TuiPixels? Pixels { get; set; }

    /// <summary>Text drawn when there are no pixels — an SF-Symbol glyph, or nothing.</summary>
    public string AltText { get; set; } = "";

    /// <summary>What the current <see cref="Pixels"/> were decoded from; see <c>TuiImage.SourceKey</c>.</summary>
    public string SourceKey { get; set; } = "";

    /// <summary>True for <c>.ContentMode(.fill)</c>: crop to fill the slot instead of fitting inside it.</summary>
    public bool Fill { get; set; }

    protected override SizeHints MeasureCore(in LayoutConstraints constraints)
    {
        if (Pixels is not { } pixels)
            return SizeHints.Fixed(new Size(AltText.Length, AltText.Length > 0 ? 1 : 0));

        var (cols, rows) = TuiAsciiArt.Fit(pixels, null, null, TuiImageOptions.DefaultColumns);
        if (constraints.IsWidthBounded && cols > constraints.MaxWidth)
        {
            // Too wide for the slot: re-fit to the width we can have, so the aspect survives the squeeze.
            (cols, rows) = TuiAsciiArt.Fit(pixels, Math.Max(1, constraints.MaxWidth), null);
        }
        var natural = new Size(cols, rows);
        return SizeHints.Flex(new Size(1, 1), natural, natural, 0, 0, 1, 1);
    }

    protected override void ArrangeCore(in TRect finalRect) { }

    protected override void RenderOverride(CellBuffer buffer)
    {
        var b = Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        if (Pixels is not { } pixels)
        {
            if (AltText.Length > 0) buffer.WriteText(b.Left, b.Top, AltText, GetTheme().ForegroundTextStyle());
            return;
        }

        // `fit` letterboxes inside the slot; `fill` crops by over-scaling to cover it. Cropping is done by
        // scaling the source up and painting only the slot, which is what the box resampler does anyway
        // once it is handed a larger target than the slot.
        var (cols, rows) = TuiAsciiArt.Fit(pixels, b.Width, null);
        if (Fill ? rows < b.Height : rows > b.Height)
            (cols, rows) = TuiAsciiArt.Fit(pixels, null, b.Height);

        cols = Math.Min(cols, b.Width);
        rows = Math.Min(rows, b.Height);
        var left = b.Left + (b.Width - cols) / 2;
        var top = b.Top + (b.Height - rows) / 2;

        var background = GetTheme().Background ?? Colors.Black;
        TuiAsciiArt.Paint(buffer, pixels, TuiImageOptions.Mode, left, top, cols, rows, background);
    }
}
