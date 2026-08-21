using Android.Graphics;
using SwiftDotNet.Graphics;
using GSize = SwiftDotNet.Graphics.Size;

namespace SwiftDotNet;

/// <summary>
/// Renders a live tree to a <see cref="Bitmap"/> using the shared engine - the implementation behind
/// <see cref="LiveRenderMode.Bitmap"/>.
///
/// <para>Almost nothing here is new code, which is the point. The live vocabulary was defined as a strict
/// subset of the main DSL, so <see cref="LiveLowering"/> rewrites the tree into ordinary core nodes; the
/// engine in <c>SwiftDotNet.Graphics</c> already lays out and paints those headlessly behind
/// <see cref="ICanvas"/>; and <see cref="AndroidCanvas"/> is the only platform-specific part. The result
/// is that anything the Skia backend can draw becomes legal on a notification or a widget, on any API
/// level, without a second rasterizer.</para>
///
/// <para><b>What it costs</b>, and why it is opt-in rather than the default: the output is one
/// <c>ImageView</c>. TalkBack sees a single opaque image, so a content description is the only thing a
/// screen-reader user gets (the validator makes it mandatory); there are no per-view tap targets, only a
/// whole-surface tap plus action buttons; nothing responds to a theme or font-scale change until we
/// re-render; and a <see cref="LiveTimer"/> stops ticking, because a bitmap is a still frame.</para>
/// </summary>
public static class LiveBitmapRenderer
{
    /// <summary>
    /// Renders a live tree at <paramref name="widthDp"/> x <paramref name="heightDp"/> logical points.
    /// </summary>
    /// <param name="liveRoot">The live node tree.</param>
    /// <param name="widthDp">Logical width. The vocabulary's unit is a point, matching SwiftUI.</param>
    /// <param name="heightDp">Logical height.</param>
    /// <param name="density">Device pixel ratio; the canvas is scaled by it so text stays crisp.</param>
    /// <param name="dark">Whether to resolve semantic colours against the dark palette.</param>
    public static Bitmap Render(Node liveRoot, float widthDp, float heightDp, float density, bool dark)
    {
        var pixelWidth = Math.Max(1, (int)Math.Round(widthDp * density));
        var pixelHeight = Math.Max(1, (int)Math.Round(heightDp * density));

        var core = LiveLowering.ToCoreNode(liveRoot, LiveClock.Now);

        var bridge = new VisualBridge(new AndroidFontProvider(), new AndroidImageDecoder());
        bridge.Render(NodeJson.Serialize(core));

        var bitmap = Bitmap.CreateBitmap(pixelWidth, pixelHeight, Bitmap.Config.Argb8888!)
            ?? throw new InvalidOperationException($"Could not allocate a {pixelWidth}x{pixelHeight} bitmap.");

        var canvas = new Canvas(bitmap);
        // Scale once here rather than converting every coordinate: the engine works in logical points,
        // exactly like the native backends, so the tree needs no density awareness at all.
        canvas.Scale(density, density);

        bridge.Draw(new AndroidCanvas(canvas, widthDp, heightDp), new GSize(widthDp, heightDp), dark);
        return bitmap;
    }

    /// <summary>
    /// Renders a live tree straight into a one-<c>ImageView</c> <c>RemoteViews</c>, ready to post.
    /// </summary>
    public static Android.Widget.RemoteViews RenderToRemoteViews(
        Android.Content.Context context, Node liveRoot, float widthDp, float heightDp)
    {
        var metrics = context.Resources?.DisplayMetrics;
        var density = metrics?.Density ?? 1f;
        var dark = (context.Resources?.Configuration?.UiMode & Android.Content.Res.UiMode.NightMask)
                   == Android.Content.Res.UiMode.NightYes;

        var bitmap = Render(liveRoot, widthDp, heightDp, density, dark);

        var rv = new Android.Widget.RemoteViews(context.PackageName, Resource.Layout.sdn_bitmap);
        rv.SetImageViewBitmap(Resource.Id.sdn_root, bitmap);

        // The bitmap is otherwise invisible to TalkBack. LiveValidator requires an accessibility label on
        // the tree for exactly this reason; surface it here so the requirement actually pays off.
        var label = FindLabel(liveRoot);
        if (label is not null)
            rv.SetContentDescription(Resource.Id.sdn_root, new Java.Lang.String(label));

        return rv;
    }

    static string? FindLabel(Node node)
    {
        foreach (var mod in node.Modifiers)
        {
            if (mod.TryGetValue("type", out var t) && t is "a11yLabel"
                && mod.TryGetValue("value", out var v) && v is string s)
                return s;
        }

        foreach (var child in node.Children)
        {
            if (FindLabel(child) is { } found) return found;
        }

        return null;
    }
}
