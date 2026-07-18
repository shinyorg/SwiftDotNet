using System.Globalization;

namespace SwiftDotNet;

/// <summary>
/// A <b>real native map</b> — MapKit on Apple, MapLibre on Web/Android — bound to a <see cref="MapCamera"/>
/// with declarative pins and polylines. It's a <see cref="CustomView"/>: each backend renders it via a
/// renderer registered under <c>"Map"</c> (see <c>SwiftDotNet.Maps.Web</c> and the native Swift/Kotlin
/// renderers). A platform with no map renderer registered shows the standard <c>⚠️</c> placeholder.
///
/// Drawing is not a special mode — <see cref="OnTap"/> yields a coordinate, the app appends it to a
/// <c>State</c>-bound list, and the map re-renders with the extended overlay, exactly like every other control.
/// </summary>
public sealed class Map : CustomView
{
    readonly State<MapCamera> _camera;
    readonly List<MapPin> _pins = new();
    readonly List<MapPolyline> _polylines = new();

    Action<MapCoordinate>? _onTap;
    Action<MapPin>? _onPinTap;
    Action<MapPin>? _onPinDragged;
    Action<MapCamera>? _onCameraChanged;

    public Map(State<MapCamera> camera) => _camera = camera;

    protected override string TypeName => "Map";

    /// <summary>Declarative markers. Bind to <c>State</c> and append to add pins.</summary>
    public Map Pins(IEnumerable<MapPin> pins) { _pins.Clear(); _pins.AddRange(pins); return this; }

    /// <summary>Declarative lines. Bind to <c>State</c> and extend a polyline's points to draw.</summary>
    public Map Polylines(IEnumerable<MapPolyline> lines) { _polylines.Clear(); _polylines.AddRange(lines); return this; }

    /// <summary>Fires with the tapped coordinate — drop a pin or add a polyline vertex.</summary>
    public Map OnTap(Action<MapCoordinate> handler) { _onTap = handler; return this; }

    /// <summary>Fires when a pin is tapped (matched by its <see cref="MapPin.Id"/>).</summary>
    public Map OnPinTap(Action<MapPin> handler) { _onPinTap = handler; return this; }

    /// <summary>Fires when a <see cref="MapPin.Draggable"/> pin is dragged to a new coordinate.</summary>
    public Map OnPinDragged(Action<MapPin> handler) { _onPinDragged = handler; return this; }

    /// <summary>Fires when the user pans/zooms — two-way bind the camera by writing it back to your <c>State</c>.</summary>
    public Map OnCameraChanged(Action<MapCamera> handler) { _onCameraChanged = handler; return this; }

    protected override void Configure(CustomNode node)
    {
        node.Prop("camera", MapJson.Camera(_camera.Value));
        node.Prop("pins", MapJson.Pins(_pins));
        node.Prop("polylines", MapJson.Polylines(_polylines));
        // A single event channel; the value string is prefixed so one handler can route to the right callback.
        node.OnEvent(Dispatch);
    }

    // Event value grammar (owned by Map, no protocol change):
    //   "tap:<lat>,<lng>"                  → OnTap
    //   "pinTap:<id>"                      → OnPinTap
    //   "pinDrag:<id>,<lat>,<lng>"         → OnPinDragged
    //   "camera:<lat>,<lng>,<zoom>"        → OnCameraChanged (also updates the bound camera State)
    void Dispatch(string? value)
    {
        if (value is null) return;
        var colon = value.IndexOf(':');
        if (colon < 0) return;
        var kind = value[..colon];
        var body = value[(colon + 1)..];

        switch (kind)
        {
            case "tap" when _onTap is not null && Coord(body) is { } c:
                _onTap(c);
                break;
            case "pinTap" when _onPinTap is not null:
                if (FindPin(body) is { } tapped) _onPinTap(tapped);
                break;
            case "pinDrag" when _onPinDragged is not null:
                var comma = body.IndexOf(',');
                if (comma > 0 && Coord(body[(comma + 1)..]) is { } dc && FindPin(body[..comma]) is { } dragged)
                    _onPinDragged(dragged with { Coordinate = dc });
                break;
            case "camera":
                if (Camera(body) is { } cam)
                {
                    _camera.Value = cam;       // keep the bound camera in sync with the native viewport
                    _onCameraChanged?.Invoke(cam);
                }
                break;
        }
    }

    MapPin? FindPin(string id) => _pins.FirstOrDefault(p => (p.Id ?? "") == id);

    static MapCoordinate? Coord(string s)
    {
        var comma = s.IndexOf(',');
        if (comma < 0) return null;
        if (D(s[..comma]) is { } lat && D(s[(comma + 1)..]) is { } lng) return new MapCoordinate(lat, lng);
        return null;
    }

    static MapCamera? Camera(string s)
    {
        var parts = s.Split(',');
        if (parts.Length != 3) return null;
        if (D(parts[0]) is { } lat && D(parts[1]) is { } lng && D(parts[2]) is { } zoom)
            return new MapCamera(new MapCoordinate(lat, lng), zoom);
        return null;
    }

    static double? D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
