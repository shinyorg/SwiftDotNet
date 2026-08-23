using SwiftDotNet;

namespace SwiftDotNet.Graphics;

/// <summary>
/// Where a real OS control has to be floated above the canvas this frame.
/// </summary>
/// <param name="Id">The node id — the identity a host reconciles against. Stable across renders.</param>
/// <param name="Type">The node type (<c>"MauiView"</c>, <c>"WebView"</c>, …), so one host can serve several.</param>
/// <param name="Frame">Content rect in <b>window-relative DIPs</b>, post-scroll. Transforms are not applied — see remarks.</param>
/// <param name="Clip">The nearest scrolling/clipping ancestor's rect, in the same space, or null when unclipped.</param>
/// <param name="Visible">False when scrolled out of its clip, or when an overlay is covering it.</param>
/// <param name="Props">The node's props, so a host can configure the control it creates (a WebView's url, …).</param>
/// <remarks>
/// <b>Transforms are a no-op on platform views.</b> <c>.Offset</c> / <c>.ScaleEffect</c> / <c>.Rotation</c>
/// are applied to the canvas matrix at paint time (<c>VisualNodePaint.ApplyScale</c> and friends) and are
/// never folded into the node's frame, so a placement is the *untransformed* layout rect. A transformed
/// platform view therefore stays where it was laid out while its canvas-drawn siblings move — the same
/// documented no-op as <c>.ScaleEffect</c> on GTK.
/// </remarks>
public readonly record struct PlatformViewPlacement(
    string Id,
    string Type,
    Rect Frame,
    Rect? Clip,
    bool Visible,
    IReadOnlyDictionary<string, object?> Props);

/// <summary>
/// Implemented by a host that can float real OS controls above the canvas — the seam that lets a
/// self-drawing backend show a control it cannot possibly draw (a <c>WebView</c>, a map, an embedded
/// .NET MAUI view).
///
/// <para>The engine calls <see cref="SyncPlatformViews"/> once per frame with the <b>complete</b> set,
/// not a create/update/destroy delta. That is deliberate: the scene is recomputed every frame anyway, and
/// a set-reconcile is the only shape that cannot leak a control when a subtree vanishes through a
/// <c>setChildren</c> patch. A node that is merely scrolled off-screen is still *present* with
/// <see cref="PlatformViewPlacement.Visible"/> false, so hosts must hide those rather than dispose
/// them.</para>
///
/// <para>A host that cannot place native views (headless, Silk, MonoGame, Godot) simply never sets
/// <see cref="VisualBridge.PlatformViewHost"/>, and every platform-view node keeps painting the
/// placeholder it paints today.</para>
/// </summary>
public interface IPlatformViewHost
{
    /// <summary>
    /// The full set of platform views for this frame. Create what is new, move and show/hide what
    /// persists, and dispose anything whose id is absent.
    /// </summary>
    void SyncPlatformViews(IReadOnlyList<PlatformViewPlacement> placements);
}

/// <summary>
/// Which node types need a real OS control rather than paint. Mirrors <see cref="VisualRenderers"/>:
/// a static registry consulted by the interpreter, so registering a type needs no engine fork.
///
/// <para>Registration affects <b>layout</b> (a registered type measures as a platform view) whether or not
/// a host is attached, so a tree lays out identically everywhere. It only affects <b>painting</b> — the
/// hole punched in the canvas — when the bridge actually has an <see cref="IPlatformViewHost"/>.</para>
/// </summary>
public static class PlatformViews
{
    static readonly HashSet<string> Types = new(StringComparer.Ordinal);

    /// <summary>Declare that <paramref name="type"/> is rendered by a real OS control, not by the canvas.</summary>
    public static void Register(string type) => Types.Add(type);

    /// <summary>Stop treating <paramref name="type"/> as a platform view (it goes back to painting).</summary>
    public static void Unregister(string type) => Types.Remove(type);

    /// <summary>True when <paramref name="type"/> has been registered as a platform view.</summary>
    public static bool IsRegistered(string type) => Types.Contains(type);
}
