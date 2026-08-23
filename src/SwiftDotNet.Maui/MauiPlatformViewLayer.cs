using SwiftDotNet.Graphics;
using Microsoft.Maui.Controls.Shapes;
using MauiControl = Microsoft.Maui.Controls.View;
using MauiRect = Microsoft.Maui.Graphics.Rect;
using MauiAbsoluteLayout = Microsoft.Maui.Controls.AbsoluteLayout;
using MauiWebView = Microsoft.Maui.Controls.WebView;

namespace SwiftDotNet;

/// <summary>
/// Places real .NET MAUI controls over a self-drawn canvas, fulfilling the engine's
/// <see cref="IPlatformViewHost"/> contract inside a MAUI app.
///
/// <para>The pleasant surprise of this direction is how little there is to it: a SwiftDotNet MAUI host is
/// itself a <c>Microsoft.Maui.Controls.ContentView</c>, so an embedded control is just another MAUI child
/// in the same layout. There is no <c>IMauiContext</c> to construct, no <c>UseMauiEmbedding</c>, no
/// <c>ToPlatform</c>, and not one line of per-OS handler code — MAUI is already running the app, and this
/// only decides where its views go.</para>
///
/// <para>It serves two node types: <see cref="MauiView.NodeType"/>, whose control comes from
/// <see cref="MauiViewRegistry"/>, and <c>WebView</c>, which the canvas has never been able to draw and
/// which now becomes a real <see cref="MauiWebView"/> instead of a painted apology.</para>
/// </summary>
public sealed class MauiPlatformViewLayer : IPlatformViewHost
{
    readonly MauiAbsoluteLayout _panel;
    readonly VisualBridge _bridge;
    readonly Dictionary<string, Live> _live = new(StringComparer.Ordinal);

    sealed record Live(MauiControl Control, string Type, string? Key);

    /// <param name="panel">
    /// The absolute layer the controls are added to. It must sit above the canvas in the host's layout and
    /// be input-transparent without cascading — see <see cref="CreatePanel"/>.
    /// </param>
    /// <param name="bridge">The engine, for routing an embedded control's events and reconciling focus.</param>
    public MauiPlatformViewLayer(MauiAbsoluteLayout panel, VisualBridge bridge)
    {
        _panel = panel;
        _bridge = bridge;

        // Declaring the types is what punches the hole: until a type is registered the engine keeps
        // painting it, which is exactly right on a host that cannot place native views.
        PlatformViews.Register(MauiView.NodeType);
        PlatformViews.Register("WebView");
    }

    /// <summary>
    /// The layer a host should add above its canvas.
    ///
    /// <para><c>InputTransparent</c> with <c>CascadeInputTransparent = false</c> is load-bearing, not
    /// tidiness: the panel covers the whole host, so if it were hit-testable it would swallow every touch
    /// the canvas needs, and if the transparency cascaded, the embedded controls would stop receiving their
    /// own. This pair means "the panel is a hole, its children are not".</para>
    /// </summary>
    public static MauiAbsoluteLayout CreatePanel() => new()
    {
        InputTransparent = true,
        CascadeInputTransparent = false,
    };

    /// <summary>
    /// Reconcile the live controls against the frame's complete placement set: create what is new, move and
    /// show/hide what persists, dispose what is gone.
    /// </summary>
    public void SyncPlatformViews(IReadOnlyList<PlatformViewPlacement> placements)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var p in placements)
        {
            // Identity, not position. A MauiView under an explicit .Key keeps one control while its
            // structural node id moves (a keyed row reorders); everything else is identified by node id.
            // Keying _live by p.Id alone would tear down and rebuild the control on every reorder, which
            // is precisely what .Key exists to prevent.
            var identity = Identity(p);
            seen.Add(identity);

            if (!_live.TryGetValue(identity, out var live))
            {
                if (Create(p) is not { } created) continue;   // a type we don't serve — leave it alone
                live = created;
                _live[identity] = live;
                _panel.Children.Add(live.Control);
            }

            var control = live.Control;
            MauiAbsoluteLayout.SetLayoutBounds(control,
                new MauiRect(p.Frame.Left, p.Frame.Top, p.Frame.Width, p.Frame.Height));
            control.IsVisible = p.Visible;
            ApplyClip(control, p);

            if (live.Key is { } key)
            {
                // Re-point the event channel every frame: under a stable .Key the structural node id can
                // move, and an event has to reach the node that exists *now*.
                if (MauiViewRegistry.NodeIdOf(key) is { } nodeId)
                    MauiViewRegistry.SetEmitter(key, v => _bridge.Emit(nodeId, v));
                MauiViewRegistry.Update(key, control);
            }
            else if (live.Type == "WebView")
            {
                ApplyWebViewSource((MauiWebView)control, p);
            }
        }

        // Anything absent from this frame's set has left the tree for good. A control that merely scrolled
        // off-screen is still *present* with Visible=false, so it is not caught here — that distinction is
        // the whole reason the engine hands over the full set rather than a delta.
        foreach (var identity in _live.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var live = _live[identity];
            _live.Remove(identity);
            _panel.Children.Remove(live.Control);
            live.Control.Handler?.DisconnectHandler();
            if (live.Key is { } key)
            {
                MauiViewRegistry.SetEmitter(key, null);
                MauiViewRegistry.Release(key);   // else the factory's captured closures outlive the node
            }
        }
    }

    static string Identity(PlatformViewPlacement p)
        => p.Type == MauiView.NodeType && p.Props.GetValueOrDefault("key") as string is { } key
            ? "k:" + key
            : "i:" + p.Id;

    Live? Create(PlatformViewPlacement p)
    {
        switch (p.Type)
        {
            case MauiView.NodeType:
            {
                if (p.Props.GetValueOrDefault("key") as string is not { } key) return null;
                if (MauiViewRegistry.Create(key) is not { } control) return null;
                WatchFocus(control);
                return new Live(control, p.Type, key);
            }
            case "WebView":
            {
                var web = new MauiWebView();
                ApplyWebViewSource(web, p);
                return new Live(web, p.Type, null);
            }
            default:
                return null;
        }
    }

    static void ApplyWebViewSource(MauiWebView web, PlatformViewPlacement p)
    {
        if (p.Props.GetValueOrDefault("url") as string is { Length: > 0 } url)
        {
            if (web.Source is not UrlWebViewSource existing || existing.Url != url)
                web.Source = new UrlWebViewSource { Url = url };
        }
        else if (p.Props.GetValueOrDefault("html") as string is { Length: > 0 } html)
        {
            if (web.Source is not HtmlWebViewSource current || current.Html != html)
                web.Source = new HtmlWebViewSource { Html = html };
        }
    }

    // A platform view floats above the canvas, so the canvas's own clip does nothing for it — a row that is
    // half-scrolled out of a ScrollView would otherwise spill over the viewport's edge. Clip geometry is in
    // the element's own space, so the viewport rect is rebased onto the control's origin.
    static void ApplyClip(MauiControl control, PlatformViewPlacement p)
    {
        if (p.Clip is not { } clip || Contains(clip, p.Frame))
        {
            control.Clip = null;
            return;
        }
        control.Clip = new RectangleGeometry(new MauiRect(
            clip.Left - p.Frame.Left, clip.Top - p.Frame.Top, clip.Width, clip.Height));
    }

    static bool Contains(Graphics.Rect outer, Graphics.Rect inner)
        => inner.Left >= outer.Left && inner.Top >= outer.Top
        && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    // Two claimants for the IME: the host's hidden 1×1 Entry (which the engine focuses for a canvas-drawn
    // TextField) and any real text control inside an embedded view. Whichever the user touches wins, and the
    // engine has to let go so its caret stops blinking in a field the user is no longer editing.
    void WatchFocus(MauiControl control)
    {
        control.Focused += (_, _) => _bridge.ClearFocus();
        foreach (var child in Descendants(control)) child.Focused += (_, _) => _bridge.ClearFocus();
    }

    static IEnumerable<Microsoft.Maui.Controls.VisualElement> Descendants(Microsoft.Maui.IVisualTreeElement root)
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is Microsoft.Maui.Controls.VisualElement v) yield return v;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>
    /// Drop platform focus when the engine takes it back for a canvas-drawn control — the other half of
    /// <see cref="WatchFocus"/>. A host wires this to its bridge's <c>FocusChanged</c>.
    /// </summary>
    public void OnEngineFocused(string? nodeId)
    {
        if (nodeId is null) return;
        foreach (var live in _live.Values)
            if (live.Control.IsFocused) live.Control.Unfocus();
    }
}
