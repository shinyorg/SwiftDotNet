# Maps

`Map` renders a **real native map** from one C# tree — MapLibre GL on Web, MapKit on Apple, MapLibre on
Android. It ships as **opt-in companion packages** so the SDK weight stays optional and Core stays
dependency-free.

```csharp
new Map(cameraState)
    .Pins(pins)
    .Polylines(routes)
    .OnTap(coord => _route.Value = _route.Value.Add(coord));   // tap-to-draw
```

`Map` is built on the [custom-control](custom-controls.md) seam (`CustomView` + the per-backend renderer
registry), which is exactly why it can live outside Core. A platform with **no map renderer shows the standard
`⚠️` placeholder**, not a crash.

## Packages

| Package | Role | Status |
|---------|------|--------|
| [`src/SwiftDotNet.Maps`](../src/SwiftDotNet.Maps) | The `Map` view + data types (`MapTypes`, `MapJson`). | ✅ |
| [`src/SwiftDotNet.Maps.Web`](../src/SwiftDotNet.Maps.Web) | MapLibre GL renderer (`MapLibreMap`). | ✅ Built & verified |
| [`src/SwiftDotNet.Maps.Apple`](../src/SwiftDotNet.Maps.Apple) | MapKit; P/Invoke `AppleMaps.Register()` + `SwiftDotNetMaps.targets`. | ✅ (iOS builds/runs) |
| [`native/maps`](../native/maps) | Swift (`MapRenderer.swift`) + Kotlin (`MapRenderer.kt`) renderers for Apple/Android. | ✅ / native |

MapKit stays **out of the core bridge** — it ships as a separate `SwiftDotNetMaps.xcframework`
(imports the bridge; iOS + macOS), registered explicitly in the app (`AppleMaps.Register()` in the iOS
AppDelegate). Key-free by default (MapKit + MapLibre/OSM).

## Scope (Phase 1)

Static pins + polylines, declarative, plus tap-to-draw. "Drawing" is just `OnTap` → append a coordinate to a
`State`-bound polyline — the same loop as any other control.

Phase 1 forced three real framework capabilities that now exist:

1. **Structured (array) prop values** beyond scalar string/double/bool (via a JSON-string prop shortcut).
2. **Persistent/updatable custom renderers** (the Web `WebRenderer` was stateless recreate-per-render → now a
   stateful JS-interop component; GTK already had `IGtkRenderer.Update`).
3. **Richer event payloads** (encode `"lat,lng"`).

See the [Roadmap](roadmap.md) for what's next.
