// IVisualRenderer.Paint(...) shadows the Paint value type inside this class, so paints are built through
// an alias rather than renaming an interface member every other renderer implements.
using Ink = SwiftDotNet.Graphics.Paint;

namespace SwiftDotNet.Graphics;

/// <summary>How a video frame fills the space the node was given.</summary>
public enum SurfaceFill
{
    /// <summary>Letterbox: the whole frame is visible, aspect preserved (SwiftUI's <c>.fit</c>).</summary>
    Contain,

    /// <summary>Crop: the space is filled edge to edge, aspect preserved (SwiftUI's <c>.fill</c>).</summary>
    Cover,

    /// <summary>Stretch to the box, ignoring aspect.</summary>
    Stretch,
}

/// <summary>
/// The built-in renderer for video-bearing nodes (<c>VideoView</c>, <c>CameraView</c>) on every
/// self-drawing backend: it pulls the newest frame from the node's <see cref="IFrameSource"/>,
/// uploads it through the rasterizer's <see cref="IPixelUploader"/>, and draws it through
/// <see cref="ICanvas"/> — so the feed composites with the rest of the UI like any other pixel.
/// </summary>
/// <remarks>
/// <para>Because it draws through <see cref="ICanvas"/> rather than floating an OS control above the
/// canvas (the <see cref="IPlatformViewHost"/> route), the frame is clipped by its scroll view, respects
/// z-order under a sheet, and lands in the headless harness's PNG. The cost is a per-frame copy and
/// upload, and no DRM path — a protected stream needs the platform-view route instead.</para>
///
/// <para>With no provider registered it paints an honest placeholder rather than an empty box or a fake
/// feed, on the same reasoning as the sample's camera stand-in: a blank rectangle reads as a bug.</para>
/// </remarks>
public sealed class SurfaceRenderer : IVisualRenderer
{
    /// <summary>The node types that draw a video surface.</summary>
    public const string VideoNode = "VideoView";

    /// <summary>The camera preview node type, shared with the native backends' camera renderers.</summary>
    public const string CameraNode = "CameraView";

    /// <summary>
    /// The core <c>WebView</c> node type. Unlike the other two this is a <em>built-in</em> node with its
    /// own placeholder, so the engine only routes it here when a provider actually serves it.
    /// </summary>
    public const string WebNode = "WebView";

    /// <summary>The shared instance the engine draws built-in surface-bearing nodes with.</summary>
    public static SurfaceRenderer Shared { get; } = new();

    /// <summary>Greedy, like <c>Image</c> and the native previews: fill what you are offered.</summary>
    public Size Measure(VisualRenderContext ctx, Size available)
    {
        // An unconstrained parent (a VStack measuring at infinity) would otherwise ask for an infinite
        // preview. 16:9 at the offered width is the conventional fallback.
        var width = float.IsFinite(available.Width) && available.Width > 0 ? available.Width : 320;
        var height = float.IsFinite(available.Height) && available.Height > 0 ? available.Height : width * 9 / 16;
        return new Size(width, height);
    }

    public void Paint(VisualRenderContext ctx, ICanvas canvas, Rect rect)
    {
        if (!TryPaint(ctx, canvas, rect)) PaintPlaceholder(ctx, canvas, rect, error: null);
    }

    /// <summary>
    /// Draw the node's surface, returning false when <b>no provider serves this node at all</b> — the
    /// caller then paints whatever placeholder that node type already has. A source that exists but has
    /// not produced a frame yet still returns true: "starting" is this renderer's story to tell.
    /// </summary>
    public bool TryPaint(VisualRenderContext ctx, ICanvas canvas, Rect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return false;

        var draw = ctx.Surfaces.Acquire(ctx);
        if (!draw.HasSource) return false;

        ctx.Surfaces.NotifySize(ctx.Id, rect.Width, rect.Height);

        var (image, frame, error) = (draw.Image, draw.Frame, draw.Error);
        if (image is null || frame is null)
        {
            PaintPlaceholder(ctx, canvas, rect, error);
            return true;
        }

        var dest = FitRect(rect, frame.DisplayWidth, frame.DisplayHeight, FillFor(ctx));

        var save = canvas.Save();
        Clip(ctx, canvas, rect);

        // Mirror is in display space so it must sit outside the sensor rotation, or a 90°-mounted front
        // camera flips top-to-bottom instead of left-to-right.
        if (frame.Mirrored)
        {
            canvas.Translate(rect.MidX, 0);
            canvas.Scale(-1, 1);
            canvas.Translate(-rect.MidX, 0);
        }

        if (frame.Rotation != 0) canvas.RotateDegrees(frame.Rotation, dest.MidX, dest.MidY);

        // A quarter-turn draws the *unrotated* buffer, so its rect is the destination with the axes
        // swapped back, centred on the same point.
        var target = frame.Rotation is 90 or 270
            ? Rect.Create(dest.MidX - dest.Height / 2, dest.MidY - dest.Width / 2, dest.Height, dest.Width)
            : dest;

        canvas.DrawImage(image, target);
        canvas.RestoreToCount(save);

        if (error is not null) PaintErrorBadge(ctx, canvas, rect, error);
        return true;
    }

    /// <summary>
    /// Clip to the node's box, rounding it when the node asked for a corner radius and the rasterizer
    /// can. On one that cannot, the frame squares off at the corners — the same limit every other piece
    /// of content in the engine has, because <see cref="ICanvas"/> is rect-clip-only by design.
    /// </summary>
    static void Clip(VisualRenderContext ctx, ICanvas canvas, Rect rect)
    {
        if (ctx.CornerRadius > 0 && canvas is IRoundedClipCanvas rounded)
            rounded.ClipRoundRect(rect, ctx.CornerRadius, ctx.CornerRadius);
        else
            canvas.ClipRect(rect);
    }

    /// <summary>The rect a <paramref name="sourceWidth"/>×<paramref name="sourceHeight"/> frame draws into.</summary>
    public static Rect FitRect(Rect bounds, int sourceWidth, int sourceHeight, SurfaceFill fill)
    {
        if (fill == SurfaceFill.Stretch || sourceWidth <= 0 || sourceHeight <= 0) return bounds;

        var scaleX = bounds.Width / sourceWidth;
        var scaleY = bounds.Height / sourceHeight;
        var scale = fill == SurfaceFill.Cover ? Math.Max(scaleX, scaleY) : Math.Min(scaleX, scaleY);

        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        return Rect.Create(bounds.MidX - width / 2, bounds.MidY - height / 2, width, height);
    }

    /// <summary>
    /// The fill mode for a node. The token vocabulary is <c>Image</c>'s (<c>"fit"</c>/<c>"fill"</c>) plus
    /// <c>"stretch"</c>; a camera with nothing specified fills, because a viewfinder that letterboxes
    /// itself inside its own frame looks broken.
    /// </summary>
    static SurfaceFill FillFor(VisualRenderContext ctx) => ctx.String("contentMode") switch
    {
        "fill" => SurfaceFill.Cover,
        "stretch" => SurfaceFill.Stretch,
        "fit" => SurfaceFill.Contain,
        _ => ctx.NodeType == CameraNode ? SurfaceFill.Cover : SurfaceFill.Contain,
    };

    // ---- placeholder --------------------------------------------------------

    /// <summary>
    /// What a node paints before its first frame — or forever, on a host with no provider registered.
    /// A viewfinder for a camera, a play glyph for a movie, and the reason underneath when there is one.
    /// </summary>
    static void PaintPlaceholder(VisualRenderContext ctx, ICanvas canvas, Rect rect, string? error)
    {
        var camera = ctx.NodeType == CameraNode;
        var web = ctx.NodeType == WebNode;
        canvas.DrawRoundRect(rect, 12, 12, Ink.Fill(new Color(0x1C, 0x1C, 0x1E)));

        var save = canvas.Save();
        Clip(ctx, canvas, rect);

        if (camera) PaintViewfinder(canvas, rect);
        else if (!web) PaintPlayGlyph(canvas, rect);

        var message = error
            ?? (FrameSources.Any
                ? camera ? "Starting camera…" : web ? ctx.String("url") is { Length: > 0 } url ? url : "Loading…" : "Loading…"
                : camera ? "No camera provider registered"
                : web ? "No web provider registered"
                : "No video provider registered");

        var font = ctx.Fonts.Get(12, bold: false);
        var width = ctx.Fonts.Measure(message, font);
        if (width <= rect.Width - 16)
        {
            var color = error is null ? new Color(0xFF, 0xFF, 0xFF, 0xB0) : new Color(0xFF, 0x9F, 0x0A);
            canvas.DrawText(message, rect.MidX - width / 2, rect.Bottom - 14, font, color);
        }

        canvas.RestoreToCount(save);
    }

    static void PaintViewfinder(ICanvas canvas, Rect rect)
    {
        var bracket = Ink.Stroke(new Color(0xFF, 0xFF, 0xFF, 0x8C), 3) with { StrokeCap = StrokeCap.Round };
        var inset = 18f;
        var len = Math.Min(34f, Math.Min(rect.Width, rect.Height) / 4);
        float l = rect.Left + inset, t = rect.Top + inset, r = rect.Right - inset, b = rect.Bottom - inset;
        if (r - l < len * 2 || b - t < len * 2) return;

        canvas.DrawLine(l, t, l + len, t, bracket); canvas.DrawLine(l, t, l, t + len, bracket);
        canvas.DrawLine(r, t, r - len, t, bracket); canvas.DrawLine(r, t, r, t + len, bracket);
        canvas.DrawLine(l, b, l + len, b, bracket); canvas.DrawLine(l, b, l, b - len, bracket);
        canvas.DrawLine(r, b, r - len, b, bracket); canvas.DrawLine(r, b, r, b - len, bracket);
    }

    static void PaintPlayGlyph(ICanvas canvas, Rect rect)
    {
        var radius = Math.Min(28f, Math.Min(rect.Width, rect.Height) / 4);
        if (radius < 8) return;
        canvas.DrawCircle(rect.MidX, rect.MidY, radius, Ink.Stroke(new Color(0xFF, 0xFF, 0xFF, 0x8C), 2));

        // A triangle needs paths, which the canvas seam deliberately does not have; three stacked lines
        // read as a play glyph at this size and keep the seam closed.
        var h = radius * 0.62f;
        var x = rect.MidX - radius * 0.18f;
        var tip = rect.MidX + radius * 0.42f;
        var stroke = Ink.Stroke(new Color(0xFF, 0xFF, 0xFF, 0xC0), 2);
        canvas.DrawLine(x, rect.MidY - h / 2, tip, rect.MidY, stroke);
        canvas.DrawLine(x, rect.MidY + h / 2, tip, rect.MidY, stroke);
        canvas.DrawLine(x, rect.MidY - h / 2, x, rect.MidY + h / 2, stroke);
    }

    /// <summary>A frame is on screen but the source has since failed: keep the picture, flag the fault.</summary>
    static void PaintErrorBadge(VisualRenderContext ctx, ICanvas canvas, Rect rect, string error)
    {
        var font = ctx.Fonts.Get(11, bold: false);
        var width = ctx.Fonts.Measure(error, font);
        if (width > rect.Width - 12) return;
        var badge = Rect.Create(rect.Left + 6, rect.Bottom - 24, width + 12, 18);
        canvas.DrawRoundRect(badge, 6, 6, Ink.Fill(new Color(0x00, 0x00, 0x00, 0xA0)));
        canvas.DrawText(error, badge.Left + 6, badge.Bottom - 5, font, new Color(0xFF, 0x9F, 0x0A));
    }
}
