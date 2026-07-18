using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace SwiftDotNet;

/// <summary>
/// The Web renderer for <c>Map</c> — a persistent <see cref="WebCustomComponent"/> hosting a
/// <b>MapLibre GL JS</b> map. Kept alive across renders (keyed by node id), it initializes the map once
/// and reconciles pins/polylines/camera via a JS-interop module on each parameter change, rather than
/// re-emitting HTML. Register it with <c>WebRenderers.Register&lt;MapLibreMap&gt;("Map")</c> (see
/// <see cref="MapsWeb.UseMapLibre"/>).
///
/// The host page must load MapLibre GL JS + CSS (e.g. from the MapLibre CDN) — see the package README.
/// </summary>
public sealed class MapLibreMap : WebCustomComponent, IAsyncDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    IJSObjectReference? _module;
    IJSObjectReference? _map;
    DotNetObjectReference<MapLibreMap>? _self;
    string ElementId => "sdn-map-" + NodeId.Replace('.', '-').Replace('$', '-');

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "id", ElementId);
        // MapLibre needs an explicitly sized container; fill the slot the layout gives us.
        builder.AddAttribute(2, "style", "width:100%;height:100%;min-height:240px;");
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _self = DotNetObjectReference.Create(this);
            _module = await JS.InvokeAsync<IJSObjectReference>(
                "import", "./_content/SwiftDotNet.Maps.Web/maplibre-interop.js");
            _map = await _module.InvokeAsync<IJSObjectReference>(
                "init", ElementId, String("camera"), String("pins"), String("polylines"), _self);
        }
        else if (_module is not null && _map is not null)
        {
            // Reconcile overlays + camera in place — the map instance (and its tiles) survives.
            await _module.InvokeVoidAsync("update", _map, String("camera"), String("pins"), String("polylines"));
        }
    }

    /// <summary>Invoked from JS when the map is tapped — routes to the C# <c>Map.OnTap</c> via the event channel.</summary>
    [JSInvokable]
    public void OnMapTap(double lat, double lng) => Emit(NodeId, $"tap:{Inv(lat)},{Inv(lng)}");

    /// <summary>Invoked from JS when a marker is tapped.</summary>
    [JSInvokable]
    public void OnPinTap(string id) => Emit(NodeId, $"pinTap:{id}");

    /// <summary>Invoked from JS after a pan/zoom settles.</summary>
    [JSInvokable]
    public void OnCameraChanged(double lat, double lng, double zoom) => Emit(NodeId, $"camera:{Inv(lat)},{Inv(lng)},{Inv(zoom)}");

    static string Inv(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null && _map is not null) await _module.InvokeVoidAsync("destroy", _map);
            if (_map is not null) await _map.DisposeAsync();
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException) { /* circuit already gone — nothing to clean up */ }
        _self?.Dispose();
    }
}

/// <summary>Registration helper — call once at app startup so <c>Map</c> renders as a MapLibre map on Web.</summary>
public static class MapsWeb
{
    public static void UseMapLibre() => WebRenderers.Register<MapLibreMap>("Map");
}
