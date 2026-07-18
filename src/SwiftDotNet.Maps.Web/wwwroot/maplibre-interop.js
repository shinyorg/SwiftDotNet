// MapLibre GL JS interop for SwiftDotNet.Maps.Web.
// The host page must load MapLibre GL JS + CSS first (e.g. the MapLibre CDN), which defines `maplibregl`.
// State (markers, the polyline source) is kept per-map so `update` can reconcile in place without
// recreating the map — the whole point of the persistent-component renderer.

const DEFAULT_STYLE = "https://demotiles.maplibre.org/style.json"; // key-free OSM demo tiles

function parse(json, fallback) {
    try { return json ? JSON.parse(json) : fallback; } catch { return fallback; }
}

export function init(elementId, cameraJson, pinsJson, polylinesJson, dotNetRef) {
    const el = document.getElementById(elementId);
    if (!el || typeof maplibregl === "undefined") {
        if (el) el.textContent = "MapLibre GL JS not loaded — add the CDN script/CSS to the host page.";
        return null;
    }

    const cam = parse(cameraJson, { lat: 0, lng: 0, zoom: 1 });
    const map = new maplibregl.Map({
        container: elementId,
        style: DEFAULT_STYLE,
        center: [cam.lng, cam.lat],
        zoom: cam.zoom,
    });

    const state = { map, markers: [], dotNetRef, lineId: "sdn-line" };

    map.on("click", (e) => dotNetRef.invokeMethodAsync("OnMapTap", e.lngLat.lat, e.lngLat.lng));
    map.on("moveend", () => {
        const c = map.getCenter();
        dotNetRef.invokeMethodAsync("OnCameraChanged", c.lat, c.lng, map.getZoom());
    });
    map.on("load", () => applyOverlays(state, pinsJson, polylinesJson));

    return state;
}

export function update(state, cameraJson, pinsJson, polylinesJson) {
    if (!state || !state.map) return;
    applyOverlays(state, pinsJson, polylinesJson);
}

export function destroy(state) {
    if (!state || !state.map) return;
    state.markers.forEach((m) => m.remove());
    state.markers = [];
    state.map.remove();
}

function applyOverlays(state, pinsJson, polylinesJson) {
    const map = state.map;
    if (!map.isStyleLoaded()) { map.once("idle", () => applyOverlays(state, pinsJson, polylinesJson)); return; }

    // Pins: clear and re-add (small counts in Phase 1; keyed diffing is a later milestone).
    state.markers.forEach((m) => m.remove());
    state.markers = [];
    for (const p of parse(pinsJson, [])) {
        const marker = new maplibregl.Marker({ color: cssColor(p.tint) })
            .setLngLat([p.lng, p.lat]);
        if (p.title) marker.setPopup(new maplibregl.Popup().setText(p.title));
        marker.addTo(map);
        marker.getElement().addEventListener("click", (ev) => {
            ev.stopPropagation();
            state.dotNetRef.invokeMethodAsync("OnPinTap", String(p.id));
        });
        state.markers.push(marker);
    }

    // Polylines: one GeoJSON source/layer, updated in place.
    const features = parse(polylinesJson, []).map((line) => ({
        type: "Feature",
        properties: { color: cssColor(line.color) || "#3b82f6", width: line.width || 3 },
        geometry: { type: "LineString", coordinates: line.points.map((pt) => [pt.lng, pt.lat]) },
    }));
    const data = { type: "FeatureCollection", features };

    const src = map.getSource(state.lineId);
    if (src) {
        src.setData(data);
    } else {
        map.addSource(state.lineId, { type: "geojson", data });
        map.addLayer({
            id: state.lineId,
            type: "line",
            source: state.lineId,
            layout: { "line-cap": "round", "line-join": "round" },
            paint: { "line-color": ["get", "color"], "line-width": ["get", "width"] },
        });
    }
}

// Map the framework's color tokens (or a hex passed through) to a CSS color.
function cssColor(token) {
    if (!token) return undefined;
    const named = {
        red: "#FF3B30", green: "#34C759", blue: "#007AFF",
        accentColor: "#007AFF", primary: "#000000", secondary: "#8E8E93",
    };
    return named[token] || token; // a "#RRGGBB" hex passes straight through
}
