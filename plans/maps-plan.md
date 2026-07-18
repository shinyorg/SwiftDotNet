# Plan: Maps for SwiftDotNet (pins + polylines)

**Status:** Draft for review
**Date:** 2026-07-18
**Scope:** A `Map` control that renders **real native maps** on each backend, supports **pins**
(annotations) and **polylines** (drawn lines) both *declaratively* (bound to `State`) and *interactively*
(tap the map to drop a pin / add a line vertex), across SwiftUI/MapKit, Compose/Google-Maps, GTK, WinUI,
and Web.

---

## 1. Goal

```csharp
public sealed class RouteView : View
{
    readonly State<MapCamera> _camera = State(new(new(51.5, -0.12), zoom: 12));
    readonly State<List<MapCoordinate>> _route = State(new());

    public override View Body =>
        new Map(_camera)
            .Pins(_route.Value.Select((c, i) => new MapPin(c, title: $"Stop {i + 1}")))
            .Polylines(new[] { new MapPolyline(_route.Value, Color.Blue, width: 4) })
            .OnTap(c => { _route.Value = [.. _route.Value, c]; })   // tap to add a pin + extend the line
            .Frame(height: 400);
}
```

Same C# → **MapKit** on Apple, **Google Maps / MapLibre** on Android, **a native map** on Linux/Windows,
and **MapLibre GL** on the Web.

## 2. Why maps are the interesting case

A map is the archetypal **custom native primitive**, and it stresses the framework in three ways the
existing controls don't:

1. **Heavy, platform-specific, often key-gated SDKs.** MapKit (free, Apple-only), Google Maps (API key +
   Play Services), MapLibre/Leaflet (key-free, web/GL). These must **not** leak into the dependency-free
   `Core`.
2. **Structured data, not scalars.** A map's payload is *arrays of coordinates* (pins, polyline vertices) —
   but `CustomNode.Prop` today only accepts `string`/`double`/`bool`, and the renderer contexts
   (`WebRenderContext`/`GtkRenderContext`) only expose `String/Number/Bool`. Maps force a **structured
   value type** in the wire model.
3. **Persistent, stateful, imperative instances.** A map keeps camera position, tile caches, and gesture
   state. It must be **updated in place** on each patch — never recreated. The current `WebRenderer` is a
   *stateless* delegate that re-emits HTML every render (fine for a `<span>`, fatal for a map — it'd reset
   the view every keystroke). Maps force a **persistent/updatable renderer** path.

These three are real framework changes (§4), and they generalize — see §7.

## 3. Where it plugs in

Map rides the existing **`CustomView` + per-backend renderer registry** seam (`GtkRenderers.Register`,
`WebRenderers.Register`, `WinRenderers`, Swift `swiftDotNetRegisterRenderer`, Kotlin `registerRenderer`),
so no built-in interpreter is forked. To keep the SDK weight opt-in, ship maps as **companion packages**,
mirroring the combined-lib / separate-TFM split already in the repo:

| Package | TFMs | Contains |
|---------|------|----------|
| `SwiftDotNet.Maps` | multi-target (ios/macos/tvos/android/windows) | The Core `Map` view + data types (registered as a `CustomView`), and per-TFM renderers: MapKit, Google Maps Compose, WinUI |
| `SwiftDotNet.Maps.Gtk` | net10.0 | GTK renderer (libshumate or WebView fallback) |
| `SwiftDotNet.Maps.Web` | net10.0 | Blazor MapLibre component + JS interop |

The `Map` **view type + data model live in Core-adjacent shared code** (no SDK deps — just serialization);
only the *renderers* pull the SDKs. A platform that doesn't register a Map renderer shows the existing
`⚠️` placeholder — graceful, not a crash.

## 4. Public API

```csharp
public readonly record struct MapCoordinate(double Latitude, double Longitude);

public sealed record MapCamera(MapCoordinate Center, double Zoom);

public sealed record MapPin(
    MapCoordinate Coordinate, string? Title = null, SwiftColor? Tint = null,
    bool Draggable = false, string? Id = null);

public sealed record MapPolyline(
    IReadOnlyList<MapCoordinate> Points, SwiftColor? Color = null, double Width = 3);

public sealed class Map : CustomView
{
    protected override string TypeName => "Map";
    public Map(State<MapCamera> camera);

    // Declarative overlays (bound to State — append to the collection to add a pin/line)
    public Map Pins(IEnumerable<MapPin> pins);
    public Map Polylines(IEnumerable<MapPolyline> lines);

    // Interaction (drawing + feedback)
    public Map OnTap(Action<MapCoordinate> handler);          // drop a pin / add a vertex
    public Map OnPinTap(Action<MapPin> handler);
    public Map OnPinDragged(Action<MapPin> handler);          // when Draggable
    public Map OnCameraChanged(Action<MapCamera> handler);    // two-way bind the camera
}
```

**Drawing = tap → State → re-render.** "Draw a line" isn't a special mode in the framework; it's the same
loop as every other control: `OnTap` yields a coordinate, the app appends it to a `State`-bound
`MapPolyline`, and the map re-renders with the extended line. An app can add its own "draw mode" toggle on
top. This keeps maps consistent with the rest of SwiftDotNet rather than bolting on an imperative canvas.

## 5. Framework prerequisites (the real work)

### 5.1 Structured prop values in the wire model

Extend the boxed prop union beyond `string/double/bool` to allow **list** and **nested object** values:

- `CustomNode.Prop(string key, object structured)` accepting `IReadOnlyList<...>` / small records.
- `Node.Props` value type stays `object`; `NodeJson` gains `AppendValue` handling for `IEnumerable` and a
  coordinate/record shape (hand-rolled, AOT-safe, no reflection — a small `IJsonSerializable` seam the
  map types implement).
- Renderer contexts gain typed accessors: `ctx.Pins()`, `ctx.Polylines()`, `ctx.Camera()` (or a generic
  `ctx.Get<T>(key)` deserializer).

*Pragmatic Phase-1 shortcut:* JSON-encode pins/lines into a single **string** prop and parse in the
renderer — zero wire-model change, ships fast, refactor to structured later. Recommended to start here.

### 5.2 Persistent / updatable custom renderers

Maps must update in place, so the renderer seam needs an **instance that survives across renders**, keyed
by node id, with a `Create` + `Update(patch)` lifecycle:

- **GTK** already has this: `IGtkRenderer.Create` + `Update(widget, ctx)`. Maps just implement `Update`.
- **Web** needs a new path: today `WebRenderer` is a stateless `delegate(builder, ctx, ref seq)`. Add a
  **stateful component renderer** — a Blazor component keyed by node id that holds the JS map handle and,
  on parameter change, diffs pins/lines via JS interop (`IJSObjectReference`), instead of re-emitting.
- **Native (Swift/Compose)** renderers must likewise **update the existing map view** (camera, annotations,
  overlays) rather than rebuild — the registered-renderer contract needs an update hook if it doesn't
  already expose one.

### 5.3 Richer event payloads

Events carry a single `string?` today. Encode coordinates as `"lat,lng"` (or small JSON) in the value; the
`Map` decodes in its `OnEvent` shim and dispatches to `OnTap`/`OnPinTap`/`OnCameraChanged`. No protocol
change needed — just a convention the Map owns.

## 6. Per-backend implementation

| Backend | Map engine | Pins | Polylines | Key / tiles | Notes |
|---------|-----------|------|-----------|-------------|-------|
| **iOS/macOS** (SwiftUI) | **MapKit** `Map` (iOS 17 `MapContentBuilder`) | `Marker` / `Annotation` | `MapPolyline` | None (Apple) | Best fit; native, free. |
| **tvOS** | MapKit (limited) | display markers | polyline overlay | None | Focus-driven, reduced interaction — degrade taps. |
| **Android** (Compose) | **Google Maps Compose** (`maps-compose`) *or* **MapLibre** | `Marker` | `Polyline` | Google = API key + Play Services; MapLibre = key-free | Offer MapLibre as the key-free default; Google as opt-in. |
| **Windows** (WinUI) | **WebView2 + MapLibre GL** (WinUI 3 has **no** native MapControl) | GL marker | GL line | MapLibre style URL | Reuses the Web renderer's JS. |
| **Linux** (GTK) | **libshumate** (GNOME GTK4 map) if Gir.Core-bindable; else **WebKitGTK + MapLibre** | Shumate marker layer | Shumate path layer | OSM tiles | Binding availability is the risk — WebView fallback is the safety net. |
| **Web** (Blazor) | **MapLibre GL JS** (or Leaflet) via JS interop | GL marker | GL line | MapLibre demo/OSM style | The reference implementation; WinUI/GTK-WebView reuse it. |

**Default posture: key-free.** Lead with MapKit (Apple) + MapLibre/OSM (everywhere else) so the samples run
with no API keys. Provide a `MapConfig` seam (style URL, Google key, tile provider) for apps that want
Google/commercial tiles.

## 7. Interplay with other milestones

- **Structured prop values (§5.1)** is not map-only — it also cleans up `List`/`Picker` data and any future
  data-heavy control. Worth landing as a general capability.
- **Persistent renderers (§5.2)** benefits every stateful custom control (charts, video, web views), not
  just maps.
- **Draggable pins / camera two-way binding** compose with the **animations plan** (animate camera moves)
  and the **DI plan** (a `IGeolocationService` injected into a map screen).
- **Clustering / large pin sets** will want the keyed-reconciliation milestone to diff annotations by id
  efficiently.

## 8. Phased delivery

| Phase | Deliverable | Backends | Risk |
|-------|-------------|----------|------|
| **1** | `Map` + data types; **static** pins + polylines (declarative, no interaction); JSON-string prop shortcut (§5.1) | **Web (MapLibre)** + **Apple (MapKit)** first | Med |
| **2** | **Interaction**: `OnTap` (draw), `OnPinTap`, camera two-way `OnCameraChanged`; persistent/updatable renderers (§5.2); **Android** (MapLibre) | Web, Apple, Android | Med–high |
| **3** | Structured wire model (§5.1 proper); **WinUI** + **GTK**; draggable pins; polygons/circles; clustering | all | Higher (WinUI/GTK map stories are the weakest) |

Phase 1 puts a real map with pins and a drawn line on screen from C#. Phase 2 makes "tap to draw" work.
Phase 3 fills in the harder platforms and richer overlays.

## 9. Worked example — tap to draw a route

```csharp
public sealed class DrawRoute : View
{
    readonly State<MapCamera> _cam = State(new(new(51.5074, -0.1278), 12));
    readonly State<List<MapCoordinate>> _pts = State(new());

    public override View Body =>
        new VStack(
            new Map(_cam)
                .Pins(_pts.Value.Select((c, i) => new MapPin(c, $"{i + 1}", Color.Red)))
                .Polylines(new[] { new MapPolyline(_pts.Value, Color.Blue, 4) })
                .OnTap(c => _pts.Value = [.. _pts.Value, c])
                .OnCameraChanged(cam => _cam.Value = cam)
                .Frame(height: 460),
            new Button("Clear", () => _pts.Value = new())
        ).Spacing(8);
}
```

Each tap drops a pin and extends the polyline; the same tree renders as MapKit overlays on iOS and
MapLibre GL layers on the Web.

## 10. Decisions needed

1. **Android engine:** default to **MapLibre** (key-free, consistent with Web/WinUI/GTK) or **Google Maps**
   (richer, needs a key)? *Rec: MapLibre default, Google as opt-in via `MapConfig`.*
2. **WinUI & GTK:** accept **WebView + MapLibre** as the shipping approach (WinUI 3 has no native map; GTK's
   libshumate binding is uncertain), or invest in native libshumate for GTK? *Rec: WebView reuse first; a
   native GTK renderer is a later enhancement.*
3. **Wire model:** land the **structured value** change (§5.1) up front (cleaner, helps List/Picker), or
   ship Phase 1 on the **JSON-string prop** shortcut and refactor later? *Rec: string shortcut for Phase 1,
   structured in Phase 3.*
4. **Packaging:** confirm maps ship as **opt-in `SwiftDotNet.Maps*` companion packages** so Core and the
   base backends stay SDK-free. *Rec: yes.*
```
