# Plan 1: Missing framework features to host the Shiny controls port

**Status:** Draft for review · **Date:** 2026-07-19
**Save to (repo convention):** `plans/controls-missing-features-plan.md`
**Companion:** [Plan 2 — the controls library itself](controls-library-plan.md). This plan lands the
framework primitives **first**; Plan 2 builds the controls on top.

## Implementation status (2026-07-19)

**Wave A is implemented** — F1–F5 landed across Core and every backend, with 17 new tests
(`tests/SwiftDotNet.Tests/WaveAFeatureTests.cs`, 49 total green).

| Feature | Core | GTK | Web | Skia | SwiftUI | Compose | WinUI |
|---|---|---|---|---|---|---|---|
| F5 gradients | ✅ | ✅ | ✅ | ✅ | ✅¹ | ✅² | ✅² |
| F4 transforms + loop-anim | ✅ | ➖³ | ✅ | ✅ | ✅¹ | ✅² | ✅² |
| F3 raster images | ✅ | ✅ | ✅ | ✅ | ✅¹ | ✅² | ✅² |
| F1 drag/pan + pinch | ✅ | ✅ | ✅⁴ | ✅⁵ | ✅¹ | ✅² | ✅² |
| F2 overlay host + service | ✅⁶ | ✅⁶ | ✅⁶ | ✅⁶ | ✅⁶ | ✅⁶ | ✅⁶ |

¹ Swift shim `typecheck`-verified against the macOS + iOS SDKs (xcframework not rebuilt here).
² Compose/WinUI written to match; not compilable on this Mac (no Android SDK / Windows) — the repo's
standing constraint. ³ GTK4: **`.Offset` now works** (translated via CSS margins — `margin-left:x;margin-right:-x` etc., which
shifts a widget's allocation while keeping the layout footprint neutral; verified the exact CSS + headless
widget build). `.Rotation`/`.ScaleEffect` remain no-ops (GTK4 has no widget rotate/scale). Gradients work. ⁴ Web drag lands; multi-pointer pinch is a follow-up. ⁵ Skia exposes
`SkiaHost.Drag/Magnify`; each Skia host still wires its raw pointer stream to them. ⁶ **F2 is pure
composition** (`OverlayHost` lowers to `ZStack`+`Rectangle`+gesture) so it needs **zero backend code** —
`Overlay.Present/Dismiss/DismissAll` + `new OverlayHost(new ContentView())`.

**Wave B (partial) also landed:** **F6** material/blur (real backdrop blur on Web/SwiftUI, translucent
tint fallback on GTK/Skia/Compose/WinUI) and **F9** text-input config (`KeyboardType`/`ReturnKey`/
`MaxLength` on `TextField`, enforced in-binding + native keyboard on Web/SwiftUI/Compose). Tests in
`WaveAFeatureTests.cs`/`ControlsTests.cs`; 66 total green.

Still deferred: **F7** collection upgrades, **F8** drawing canvas, **F10** services, **F11** geometry
(GeometryReader — needs per-backend layout-event plumbing; the fixed-frame + drag-location trick covers
Slider/RangeSlider without it), **F12** camera preview (camera family).

## Context

The goal is a **separate control library** that ports the controls from `~/Desktop/dev/controls`
(`Shiny.Maui.Controls`) into SwiftDotNet — badges, pills, toasts, floating panels, trees, tables,
sliders, a color picker, an image viewer, a scheduler, chat, and so on. The precedent already exists in
this repo: **`SwiftDotNet.Maps`** is exactly this shape — a dependency-free `net10.0` core
([`src/SwiftDotNet.Maps/Map.cs`](../src/SwiftDotNet.Maps/Map.cs)) built on `CustomView`
([`Core/CustomView.cs`](../src/SwiftDotNet/Core/CustomView.cs)), with per-backend renderer companions
(`SwiftDotNet.Maps.Apple`, `SwiftDotNet.Maps.Web`). Plan 2 follows that split.

**The encouraging finding.** The Shiny controls are ~90% **pure C# composition** — a `ContentView`
subclass whose constructor builds a tree of `Label`/`Border`/`Image`/`Grid`/`StackLayout`, with state
carried on `BindableProperty`. That maps almost one-to-one onto a SwiftDotNet **composite** — a `View`
subclass with a `Body` (the [`Rating`](../sample/SharedUI/Rating.cs) pattern), which renders on **every**
backend with zero native code. Only **~7 files of true native handler code** exist across the whole
library (the three virtualized grids), plus a handful of thin platform `#if` blocks.

**The honest catch.** "Pure composition" in MAUI leans on capabilities MAUI gives for free that
SwiftDotNet **does not yet have**. A pill is composition today. But a `FloatingPanel` needs a
*continuous pan gesture*; a `Toast` needs a *top-level overlay host* invoked *imperatively from code*; an
`ImageViewer` needs *raster images* + *pinch/pan*; a `ColorPicker` needs a *drawing surface*. None of
these exist in Core (verified below). So the port is gated not by the controls' own complexity but by a
**small set of cross-cutting framework primitives**. This plan enumerates them, ordered by how many
controls each unblocks, so Plan 2 can proceed wave-by-wave as they land.

## What Core has today (the baseline we build on)

- **Views:** ~40 built-ins (stacks/grid/scroll/form/section, text/label/image-as-SF-Symbol/shapes,
  the two-way inputs, tab/nav/sheet/alert, `List`) — [`Core/Views/*`](../src/SwiftDotNet/Core/Views).
- **Composites** via `View.Body`; **custom native primitives** via `CustomView` + a per-backend renderer
  registry (`GtkRenderers`/`WinRenderers`/`WebRenderers`/`SkiaRenderers` + native `registerRenderer`),
  unknown type → `⚠️` placeholder ([`Core/CustomView.cs`](../src/SwiftDotNet/Core/CustomView.cs)).
- **Modifiers:** padding, font, fore/background (**solid color only**), frame, cornerRadius, shadow,
  border, align, opacity, disabled, **scaleEffect** (the only transform), animation, navigationTitle
  ([`Core/ViewModifiers.cs`](../src/SwiftDotNet/Core/ViewModifiers.cs)).
- **Gestures:** `OnTapGesture` (incl. double-tap), `OnLongPress`, `OnSwipe` (fixed direction, one-shot)
  — **all one-shot, no deltas** ([`Core/Modifier.cs`](../src/SwiftDotNet/Core/Modifier.cs):208-247).
- **Animation:** implicit, value-triggered interpolation only (`.Animation(spec, on:)`); no loop/repeat,
  no imperative animate-to ([`Core/Animation.cs`](../src/SwiftDotNet/Core/Animation.cs)).
- **State/events:** `State<T>` (reassign to invalidate; no derived/observable-collection);
  **one string-or-null event value per node**, multiplexed with a self-defined grammar (Map/List do this).
- **Wire contract:** props/modifiers are **`string`/`double`/`bool` only**; rich payloads ride as a
  JSON-encoded string prop ([`Core/NodeJson.cs`](../src/SwiftDotNet/Core/NodeJson.cs), Maps' trick).

## Confirmed gaps (verified absent in `Core/`)

Continuous/pan gesture · pinch/rotate · offset & rotation transforms · looping/imperative animation ·
gradient/brush fills · backdrop blur/material · raster images (url/file/bytes — only SF-Symbols today) ·
imperative overlay host · collection virtualization/sections/swipe-actions/reorder/staggered/paged ·
DSL drawing surface · focus control & keyboard-type config · size/geometry reader · media-picker /
filesystem services · per-child-instance state.

## The gating features, ordered by controls unblocked

Each is scored **S / M / L** for Core + per-backend effort, and lists the controls it unlocks. The
first five (F1–F5) unblock the **majority of Tier-1 controls**; land those and Plan 2's first two waves
can ship.

### F1 — Continuous drag/pan gesture (+ pinch)  ·  effort L  ·  **highest leverage**
Unlocks: FloatingPanel (detent drag), Slider, RangeSlider, ImageViewer (pan), SignaturePad (stroke),
TableView/TreeView drag-reorder, ImageEditor, Scheduler carousel — **~9 controls**.

- **Core:** new `DragGesture` modifier `.OnDrag(Action<DragInfo>)` emitting phase + translation +
  location. Add pinch `.OnMagnify(Action<double>)` for zoom.
- **Wire:** the event channel is string-only, so encode phases like `Map` does its grammar —
  `"began:x,y"` / `"changed:dx,dy"` / `"ended:vx,vy"` (velocity), pinch `"scale:1.7"`. No protocol change.
- **Per backend:** SwiftUI `DragGesture`/`MagnificationGesture`; Compose `pointerInput` drag/transform;
  GTK `GestureDrag`/`GestureZoom`; WinUI `ManipulationDelta`; Web pointer-events; **Skia** gesture
  recognizer from the raw pointer stream (the Skia engine already owns hit-testing).
- **Why L:** six backends, and continuous events at pointer-move rate stress the "one render per change"
  loop — the pan should update *local* transform state and only re-render on commit where possible.

### F2 — Overlay host + imperative presentation service  ·  effort M
Unlocks: Toast, Dialogs (alert/confirm/prompt), Overlay/LoadingOverlay, FloatingPanel, ImageViewer,
DurationPicker — **~6 controls**, and it is the *substrate* several others sit in.

- **Problem:** these are invoked **from code** (`Toaster.Show(...)`), not declared in a `Body`. That
  clashes with the pure-declarative model. `Sheet`/`Alert` exist but are single, `State<bool>`-bound,
  fixed-chrome ([`Core/Views/Navigation.cs`](../src/SwiftDotNet/Core/Views/Navigation.cs)).
- **Core:** a root-level **overlay layer** — a `ZStack`-like top slot the runtime always renders above
  the root — backed by an observable ordered collection of overlay entries. An imperative
  `IOverlayService` (`Present(View, options)` / `Dismiss(id)`) writes to that collection and requests a
  render. Multiple simultaneous layers + detents (for FloatingPanel).
- **Per backend:** mostly free — it lowers to an existing `ZStack` over the root. Detent snapping rides
  F1. Blur backdrop rides F6.
- **Note:** this is the one feature that adds an *imperative* surface to a declarative framework — worth
  a design sub-review. Alternative: keep it declarative (a `State`-bound overlay list the app owns) and
  ship the imperative service as a thin convenience over that.

### F3 — Raster images (url / file / bytes)  ·  effort M
Unlocks: ImageViewer, MediaPicker, ImageEditor, ChatView, Scheduler avatars, image cells, FontPicker
preview — **~7 controls**. Today `Image` is **SF-Symbols only** ([`Core/Views/Display.cs`](../src/SwiftDotNet/Core/Views/Display.cs):4).

- **Core:** `Image.FromUrl(string)`, `Image.FromFile(string)`, `Image.FromBytes(byte[])` +
  content-mode (`.AspectFit/.AspectFill`). URL/path cross as string props; bytes as base64 string (or a
  registered resource handle to avoid huge payloads).
- **Per backend:** every toolkit loads a bitmap (SwiftUI `AsyncImage`/`UIImage`; Compose `AsyncImage`;
  GTK `Gtk.Picture`; WinUI `BitmapImage`; Web `<img>`; Skia `SKImage`). Add async load + placeholder.

### F4 — Transform modifiers + looping/imperative animation  ·  effort M
Unlocks: BadgeView pulse, Skeleton shimmer, ProgressBar, Fab, FabMenu (radial expand), Toast slide-in,
ImageViewer zoom-spring — **~7 controls**.

- **Core:** `.Offset(x,y)` and `.Rotation(deg)` modifiers (only `.ScaleEffect` exists today). A
  **repeating/looping** animation spec (`.Animation(Anim.EaseInOut.Repeating(autoreverse:true), ...)`)
  for shimmer/pulse; optionally an imperative `Animate.To(...)`.
- **Wire:** two new modifier `type`s (`offset`, `rotation`) + a `repeat`/`autoreverse` field on the
  existing `animation` modifier. Serializer already handles scalar fields.
- **Per backend:** transform + repeat map to native (`rotationEffect`/`offset`, `repeatForever`;
  Compose `graphicsLayer`/`rememberInfiniteTransition`; CSS transform/keyframes; Skia animation clock,
  which the Skia plan already scopes).

### F5 — Gradient & brush fills  ·  effort S–M
Unlocks: ColorPicker (spectrum/hue), Skeleton shimmer, ChatView bubbles, Scheduler, buttons —
**~5 controls**. `Background` is **solid color only** today.

- **Core:** `.Background(Brush)` overload with `LinearGradient(stops, angle)` / `RadialGradient`. Brush
  serializes to a JSON string prop (Maps' trick).
- **Per backend:** native gradient layers everywhere; trivial on Skia/Web/CSS.

### F6 — Backdrop blur / material  ·  effort M (native)
Unlocks: FrostedGlassView, Overlay/LoadingOverlay — **2 controls**, but a signature look.

- **Core:** `.Material(MaterialStyle)` / `.Blur(radius)` modifier.
- **Per backend:** **native-only** — UIVisualEffectView (iOS/macOS), `RenderEffect` blur (Android 12+),
  Acrylic (WinUI), `backdrop-filter` (Web), a blur pass (Skia). GTK falls back to a tint. This is the
  one that genuinely needs per-backend native work; ship a **tint fallback** first (as MAUI does).

### F7 — Collection upgrades: sections, swipe-actions, reorder, virtualization, staggered/paged  ·  effort L
Unlocks: TableView, TreeView, DataGrid, VirtualizedGrid, StaggeredGrid, CarouselGallery,
ParallaxCollectionView, Scheduler agenda, ChatView — **~9 controls**.

- **Today:** `List` has keyed reconciliation, selection, header/footer/empty, pull-to-refresh,
  `OnReachEnd` — but **rows materialize eagerly** ([`Core/Views/List.cs`](../src/SwiftDotNet/Core/Views/List.cs):158)
  and there is no sectioned source, swipe action, reorder, or staggered/paged layout.
- **Core (incremental):**
  - **(a) Sectioned data source** + sticky section headers — unlocks TableView/Scheduler/Chat grouping.
  - **(b) Swipe actions** on a row (leading/trailing action buttons) — TableView/TreeView.
  - **(c) Drag-to-reorder** (rides F1) — TableView/TreeView.
  - **(d) True virtualization / windowing** — a windowed data source that materializes only visible rows
    (the file's own comment flags this as future). Needed for DataGrid/VirtualizedGrid at scale.
  - **(e) Staggered & paged layouts** — StaggeredGrid, CarouselGallery.
- **Sequencing:** (a)+(b)+(c) are additive to `List` and unblock most; (d)+(e) are larger and can be a
  later wave (many controls work acceptably without true virtualization at small N).

### F8 — DSL drawing surface (retained vector canvas)  ·  effort L
Unlocks: ColorPicker, SignaturePad, ImageEditor — **3 controls** (all Tier 2/3).

- **The constraint:** MAUI's `GraphicsView`/`IDrawable` is an **immediate-mode callback** that runs on
  the C# side; SwiftDotNet's renderer is *remote* (across the wire/native shim), so a draw closure can't
  cross. The portable answer is a **retained draw-command list** — a `DrawingCanvas` `CustomView` whose
  prop is a serialized list of primitives (paths, lines, gradient fills, text) plus a pointer-stream
  feedback channel (rides F1) for capture. Each backend replays the command list.
- **Alternative for two of the three:** ColorPicker's spectrum/hue can be built from **F5 gradients**
  (no canvas); only SignaturePad/ImageEditor genuinely need freeform paths. Recommend **defer F8**,
  build ColorPicker on gradients, and treat SignaturePad/ImageEditor as a **Skia-backend-first** Tier-3
  effort (the Skia engine already owns a canvas — a `DrawingCanvas` renderer there is cheap).

### F9 — Focus control + keyboard config + text-input enhancements  ·  effort M
Unlocks: SecurityPin, TextEntry, AutoCompleteEntry, ChatView input, entry cells, CountryPicker search —
**~6 controls**.

- **Core:** on the text inputs — `KeyboardType`, `ReturnKey`, `MaxLength`, `Mask`, programmatic
  **focus** (`.Focused(State<bool>)`), and focus-changed / submit events. None exist today.
- **Per backend:** native keyboard-type + focus APIs everywhere.

### F10 — Platform services: media picker, filesystem, haptics, geocode  ·  effort M–L  ·  defer
Unlocks: MediaPickerButton, ImageEditor export, ChatView attach, AddressEntry, and the haptic feedback
that many controls call — **Tier 3**. These are *outside* the UI framework (service abstractions, not
views). Defer to a later wave; several controls degrade gracefully without them.

### F11 — Size / geometry reader (proportional layout)  ·  effort M
Unlocks: precise Slider thumb positioning, ImageViewer fit, Scheduler timeline, parallax — needed
wherever a control positions children by measured container width. SwiftDotNet **delegates layout to
each native toolkit** and exposes no size callback (only Skia measures internally).

- **Core:** a `GeometryReader`-style container that reports its resolved size back via the event channel,
  **or** proportional layout primitives (weight/star sizing, fractional offset) so controls avoid needing
  a measured pixel width. Prefer the proportional route where possible — many MAUI controls only used
  `AbsoluteLayout` because MAUI lacked good proportional sizing.

### F12 — Live camera-preview CustomView  ·  effort L (native)  ·  camera-family only
Unlocks: the entire `Shiny.Maui.Camera.*` family ([Plan 2 Wave 6](controls-library-plan.md)) — CameraView
plus the barcode/face/OCR/motion/documents/AI analyzers.

- **The `Map` model applied to a camera:** a `CameraView` `CustomView` emits a `"CameraView"` node; each
  backend registers a renderer hosting the **native preview** (SwiftUI `AVCaptureVideoPreviewLayer`,
  Compose CameraX `PreviewView`, WinUI `CaptureElement`, Web `getUserMedia`+`<video>`; GTK/Skia →
  placeholder). Capture / torch / lens-switch / tap-to-focus ride the event channel with a `kind:body`
  grammar (copy `Map.Dispatch`). Frames feed an `IFrameAnalyzer` pipeline whose ML analyzers map to the
  platform vision stack (Apple **Vision** / Android **ML Kit**).
- **Scope:** camera-specific, so it is *not* part of the general feature set — it lands **with** Plan 2
  Wave 6 and its own companion packages. Depends on **F10** for camera permissions/capture. This is the
  single largest native effort in the whole port and is deliberately last.

## Recommended sequencing

```
Wave A (unblocks Plan 2 Waves 1–2 — most Tier-1 controls):
    F3 raster images · F4 transforms+loop anim · F5 gradients      (S–M each, mostly free per-backend)
    F1 drag/pan gesture · F2 overlay host                          (the two big enablers)

Wave B (richer controls):
    F6 blur · F7(a–c) sections/swipe/reorder · F9 focus/keyboard · F11 geometry

Wave C (heavy):
    F7(d–e) virtualization/staggered/paged · F8 drawing canvas · F10 services

Wave D (camera family — Plan 2 Wave 6, last):
    F10 services · F12 camera-preview CustomView + native Vision/ML Kit analyzers
```

**Minimal unlock:** F1 + F2 + F3 + F4 + F5 make the entire Tier-1 set of Plan 2 buildable as composites
with **no per-backend control code** — the highest-value slice.

## Per-backend cost reality

Adding a *modifier* or *gesture* (F1, F4, F5, F6, F9) touches the switch in **each** backend:
SwiftUI + Compose native shims (native code, no C# seam — `native/`), GTK
([`GtkNode.cs`](../src/SwiftDotNet.Gtk/GtkNode.cs):539 modifier switch), WinUI
([`Platforms/Windows/WinNode.cs`](../src/SwiftDotNet/Platforms/Windows/WinNode.cs):452), Web
(`WebStyle.cs`), Skia (`SkiaNodePaint.cs`). Budget every modifier/gesture feature as **6 small edits**.
Adding a *new node type* (F8 canvas, F2 if done as a node) additionally needs a renderer registered per
backend — the `Map` cost model. **Composites (Plan 2's Tier 1) need none of this** once F1–F5 exist.

## Verification

- **Per feature:** exercise it in the shared `ContentView` tour and confirm it renders/behaves on
  SwiftUI (macOS), GTK, Web, and Skia at minimum (the four buildable here); screenshot-diff.
- **F1:** a drag on a test box updates an offset live and commits on release; pinch scales.
- **F2:** `IOverlayService.Present` shows a view above the root on all backends; dismiss removes it;
  two stacked overlays z-order correctly.
- **F3:** a URL image and a bytes image both load with a placeholder.
- **Loop/diff sanity:** continuous drag (F1) must not flood the differ — confirm local-transform updates
  don't emit a full patch per pointer-move; only commit re-renders.
- **No regressions:** the existing `ContentView` 5-tab tour renders identically after each feature.
