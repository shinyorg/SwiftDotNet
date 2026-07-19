# Plan 2: The controls library (`SwiftDotNet.Controls`)

**Status:** Draft for review · **Date:** 2026-07-19
**Save to (repo convention):** `plans/controls-library-plan.md`
**Depends on:** [Plan 1 — Missing framework features](controls-missing-features-plan.md). Each wave below
names the Plan-1 features (F1–F11) it needs; a wave cannot start before its features land.

## Context

Port the controls from `~/Desktop/dev/controls` into SwiftDotNet as a **separate control library**,
mirroring the existing `SwiftDotNet.Maps` split exactly.

**Scope of this effort — two package families only:**

1. **`Shiny.Maui.Controls`** (the main package) — pills, badges, toasts, floating panels, trees, tables,
   sliders, color picker, image viewer, scheduler, chat, … (Waves 0–5 below).
2. **`Shiny.Maui.Camera.*`** — the camera family: `Camera` (core `CameraView` + native handler),
   `Camera.Barcode`, `Camera.Face`, `Camera.Ocr`, `Camera.Motion`, `Camera.Documents`, `Camera.Ai`,
   plus `Shiny.Controls.Camera.Shared` (Wave 6).

**Explicitly out of this effort** (not "first wave" — not in scope at all here; each needs its own later
analysis): `Shiny.Maui.Controls.SpeechAddins`, `.Markdown`, `.MermaidDiagrams`, `.Desktop`, the standalone
`.Barcodes` (ZXing generation — distinct from camera barcode *scanning*, which is in via `Camera.Barcode`),
`.Themes.Material/Ocean`, `.Kiosk`, and the entire parallel `Shiny.Blazor.Controls` family.

**Why a separate library.** Same reasons as Maps: the core stays dependency-free `net10.0` and
referenceable from every head; the few controls that need native/heavy renderers live in companion
packages so a neutral consumer isn't forced to pull them. The camera family in particular is native +
platform-ML heavy, so it lives in its own companion packages (§Wave 6).

**The porting model — two shapes, matching Core's two extension points:**

1. **Composite** (`View` + `Body`) — the [`Rating`](../sample/SharedUI/Rating.cs) pattern. The Shiny
   control's constructor tree of `Label`/`Border`/`Image`/`Grid` becomes a SwiftDotNet view tree; its
   `BindableProperty`s become constructor args + `State<T>`; its `propertyChanged` mutations become
   ordinary re-render. **Renders on every backend with zero native code.** This is ~80% of the port.
2. **Custom native primitive** (`CustomView` + per-backend renderer) — the [`Map`](../src/SwiftDotNet.Maps/Map.cs)
   pattern. Only for the handful needing native rendering (blur, native-recycling grids, freeform
   drawing). Costs a renderer in each backend.

Most controls are shape #1 and are gated only by the **framework features in Plan 1**, not by native
code — which is why Plan 1 comes first.

## Project structure (mirror the Maps split)

```
src/SwiftDotNet.Controls/               net10.0, dependency-free, RootNamespace=SwiftDotNet
    → composites (View subclasses) + CustomViews + the imperative service surfaces
    (references only ..\SwiftDotNet)
src/SwiftDotNet.Controls.Apple/         native renderers for the CustomViews that need them
src/SwiftDotNet.Controls.Web/           Blazor renderers (mirror SwiftDotNet.Maps.Web)
    (+ Gtk/Windows/Skia renderers where a CustomView needs them)
native/controls/*.swift / *.kt          native renderers for SwiftUI/Compose CustomViews (blur, grids)

# Camera family (Wave 6) — its own packages so a controls consumer isn't forced to pull camera/ML deps
src/SwiftDotNet.Controls.Camera/        net10.0 — CameraView (a CustomView) + CameraPhoto/CameraInfo +
    the IFrameAnalyzer pipeline abstractions (from Shiny.Controls.Camera.Shared). No native code.
src/SwiftDotNet.Controls.Camera.Apple/  native AVFoundation preview + Vision analyzers (renderer + services)
native/camera/*.swift / *.kt            native camera-preview renderers for the SwiftUI/Compose backends
    (AVCaptureVideoPreviewLayer / CameraX PreviewView) + MLKit/Vision frame analyzers
src/SwiftDotNet.Controls.Camera.{Ai,Barcode,Documents,Face,Motion,Ocr}/
    net10.0 analyzer add-ons that plug into the frame pipeline (map to Vision / ML Kit per platform)

sample/SharedUI/                        add a "Controls" tour tab exercising each wave
SwiftDotNet.slnx                        add the new projects
```

Composite controls need **no** companion package — they live entirely in `SwiftDotNet.Controls`
(`net10.0`) and render everywhere. Companions exist only for the native CustomViews (Waves 4–6). The
camera family is **all native/ML** and therefore ships entirely as its own packages.

## Control → approach → dependency map

Tiers and machinery are from the source-repo inventory. "Approach" = SwiftDotNet shape;
"Needs" = Plan-1 features that gate it.

| Control | Tier | Approach | Needs (Plan 1) |
|---|---|---|---|
| PillView, BadgeView | 1 | Composite | F4 (pulse) |
| Skeleton, ProgressBar | 1 | Composite | F4 loop-anim, F5 gradient |
| BorderlessEntry, TextEntry, AutoCompleteEntry | 1 | Composite | F9 focus/keyboard |
| SecurityPin | 1 | Composite | F9 |
| Cells (14) + TableView | 1 | Composite | F7(a–c) sections/swipe/reorder, F9 |
| TreeView | 1 | Composite | F7(a–c), per-child state (F-note) |
| DataGrid | 1 | Composite | F7(d) virtualization (large-N), F11 |
| CountryPicker, FontPicker, DurationPicker | 1 | Composite | F2 overlay (picker page/panel), F9 |
| Slider, RangeSlider | 1 | Composite | F1 pan, F11 geometry |
| Fab, FabMenu | 1 | Composite | F4 transforms/anim |
| Scheduler (3 views) | 1 | Composite | F7(a) sections, F11 geometry, F3 |
| ParallaxCollectionView | 1 | Composite | F7, F11 |
| Toast, Dialogs, Overlay/LoadingOverlay | 1/2 | Composite + service | **F2** overlay host, F4 |
| OverlayHost / ShinyContentPage | infra | maps onto **F2** | F2 |
| FloatingPanel | 2 | Composite | **F1** pan, **F2** overlay, F11 |
| ImageViewer | 2 | Composite | **F1** pinch/pan, F3 images, F2 |
| FrostedGlassView | 2 | CustomView (native) | **F6** blur |
| ColorPicker | 2 | Composite (gradients) | **F5** gradient, F1 |
| VirtualizedGrid, StaggeredGrid, CarouselGallery | 2 | CustomView (native recycling) | **F7(d–e)** |
| SignaturePad | 2/3 | CustomView (drawing) | **F8** canvas, F1 |
| ImageEditor | 3 | CustomView (drawing) | **F8**, F1, F3, F10 export |
| ChatView | 3 | Composite (large) | F7, F3, F9, F10 attach |
| MediaPickerButton | 3 | Composite + service | **F10** media picker |
| AddressEntry | 3 | Composite + service | F10 (HTTP geocode) |
| **CameraView** (Camera core) | 3 | **CustomView (native preview)** | **F10** camera+permissions, F1 tap-focus, F3 |
| Camera.Barcode / .Face / .Ocr / .Motion | 3 | Frame-analyzer add-on → platform ML | **F10** (Vision / ML Kit) |
| Camera.Documents / .Ai | 3 | Analyzer + pure-C# parsers over the ML text/detections | **F10** |

**Out of scope for this plan** (each needs its own later analysis): `Shiny.Maui.Controls.SpeechAddins`,
`.Markdown`, `.MermaidDiagrams`, `.Desktop`, the standalone `.Barcodes` (ZXing generation — camera barcode
*scanning* is in, via `Camera.Barcode`), `.Themes.Material/Ocean`, `.Kiosk`, and the whole parallel
`Shiny.Blazor.Controls` family.

## Delivery waves (each ends with a renderable, verified sample tab)

### Wave 0 — Scaffold  ·  (no Plan-1 deps)
- Create `src/SwiftDotNet.Controls` (`net10.0`, `RootNamespace=SwiftDotNet`, ProjectReference to
  `..\SwiftDotNet`, `IsTrimmable`) — copy `SwiftDotNet.Maps.csproj` verbatim.
- Add to `SwiftDotNet.slnx`. Add a **Controls** tab to `sample/SharedUI/ContentView.cs`.
- Port a **theme-token** shim: the Shiny controls read `ShinyThemeKeys` via `SetDynamicResource`; map
  those to SwiftDotNet's environment cascade / `Theme` ([`Core/Theme.cs`](../src/SwiftDotNet/Core/Theme.cs)).
- **First control (proof):** `PillView` — pure composite (`Border`+`Text`, 6 semantic types). Ships now,
  validates the whole pipeline end-to-end on all backends.

### Wave 1 — Static composites  ·  needs F3, F4, F5
Badges, chips, skeletons, bars, FABs — the visual-only controls.
- **PillView, BadgeView** (corner badge wrapping content, dot/count/overflow, **pulse** via F4).
- **Skeleton/SkeletonView** (shimmer via F4 loop + F5 gradient), **ProgressBar** (F4).
- **Fab, FabMenu** (radial expand via F4 transforms), **FabMenuItem**.
- **BorderlessEntry** (trivial styled field).
- Ends: a "Badges & FABs" sample section.

### Wave 2 — Overlays & imperative services  ·  needs F2 (+ F1 for panels, F4)
The overlay family — the biggest single unlock.
- Implement **OverlayHost / ShinyContentPage** as the app's Plan-1 **F2 overlay layer** (this *is* the
  infra the rest sit in).
- **Toast** (`IToastService.Show(...)`, queue/stack, auto-dismiss, spinner/progress, pill/fill) — a
  code-invoked service over F2.
- **Dialogs** (`IDialogService` alert/confirm/prompt), **Overlay/LoadingOverlay** (uses F6 blur when
  available, tint fallback until then).
- **FloatingPanel** (detent bottom/top sheet, drag handle, **pan-to-resize via F1**, keyboard avoidance).
- Ends: a "Overlays" sample section (toast button, confirm dialog, floating panel).

### Wave 3 — Inputs, pickers, lists  ·  needs F9, F7(a–c), F11, F1
The form/collection tier.
- **Slider, RangeSlider** (pan via F1, thumb position via F11), **SecurityPin**, **TextEntry**,
  **AutoCompleteEntry** (F9 focus/keyboard + F2 dropdown).
- **Cells (14)** + **TableView** (grouped table via F7 sections, swipe actions, drag-reorder),
  **TreeView** (expand/collapse, lazy load, reorder).
- **CountryPicker, FontPicker/FontSizePicker, DurationPicker** (picker pages/panels over F2).
- Ends: a "Forms & Lists" sample section.

### Wave 4 — Media & rich composites  ·  needs F3, F1, F6, F5
- **ImageViewer** (thumbnail → fullscreen pinch/pan/double-tap zoom via F1 + F3, presented via F2).
- **ColorPicker** (HSB spectrum + hue bar built from **F5 gradients** — no drawing canvas needed —
  + opacity + hex).
- **FrostedGlassView** — first **CustomView** with native renderers (F6): companion renderers in
  `SwiftDotNet.Controls.Apple` (UIVisualEffectView), `native/controls/*.kt` (RenderEffect), WinUI Acrylic,
  Web `backdrop-filter`, Skia blur pass; GTK tint fallback.
- **Scheduler** (calendar grid / agenda / list — composites over F7 + F11 + F3).
- **DataGrid** (sortable/filterable/groupable — composite; F7(d) virtualization for large N).
- Ends: an "Images & Scheduler" sample section.

### Wave 5 — Heavy / native  ·  needs F7(d–e), F8, F10
The Tier-2/3 controls that require native recycling, freeform drawing, or platform services.
- **VirtualizedGrid, StaggeredGrid, CarouselGallery** — `CustomView`s backed by native recycling views
  (RecyclerView / UICollectionView / ScrollViewer), or a windowed engine list where a native handler
  isn't warranted. Companion renderers per backend.
- **SignaturePad** — `DrawingCanvas` CustomView (F8), **Skia-backend-first** (the engine already owns a
  canvas), then native renderers; PNG export via F10.
- **ImageEditor** — largest: F8 canvas + crop/rotate/annotate + undo/redo + F10 export. Skia-first.
- **ChatView** — large composite (bubbles/typing/receipts/templates) over F7 + F3 + F9, attach via F10.
- **MediaPickerButton, AddressEntry** — thin composites over F10 services.
- Ends: a "Drawing & Chat" sample section, with clear per-backend capability notes (some are
  Skia/native-only).

### Wave 6 — Camera family  ·  needs F10 + a new **camera-preview CustomView** capability
The `Shiny.Maui.Camera.*` family. This is **all native + platform ML** — it ships as its own packages
(§Project structure) and does not affect the pure-composition controls above.

- **New capability first (a Plan-1 addition):** a live **camera-preview `CustomView`** — the `Map` model
  applied to a camera. `CameraView` emits a `"CameraView"` node; each backend registers a renderer that
  hosts the native preview (SwiftUI `AVCaptureVideoPreviewLayer`, Compose CameraX `PreviewView`, WinUI
  `MediaPlayerElement`/`CaptureElement`, Web `getUserMedia`+`<video>`; GTK/Skia → placeholder). Capture,
  torch, lens-switch, and tap-to-focus ride the event channel (F1) with a `kind:body` grammar like Map's.
  Track this as an addendum to Plan 1 (call it **F12 — camera preview**); it is camera-specific, so it
  lives with this wave rather than the general feature set.
- **Camera core** (`SwiftDotNet.Controls.Camera`): `CameraView` (CustomView) + `CameraPhoto`/`CameraInfo`
  + the `IFrameAnalyzer` pipeline abstractions (port `Shiny.Controls.Camera.Shared` — frame model,
  coordinate transforms, overlay boxes). A captured photo is shown via **F3** raster images.
- **ML analyzer add-ons** (each its own package, plugging into the frame pipeline, mapped to the platform
  vision stack — **Apple Vision / Android ML Kit**):
  - **Camera.Barcode** — barcode/QR scanning.
  - **Camera.Face** — face detection.
  - **Camera.Ocr** — text recognition.
  - **Camera.Motion** — frame-difference motion detection (mostly pure C# over frames).
  - **Camera.Documents** — business-card / credit-card / drivers-license parsing (pure-C# parsers over
    OCR text — the analyzers are native, the parsers port straight across).
  - **Camera.Ai** — document analysis over the detections.
- **Reality check:** the preview + analyzers are **native-only** and land first on the backends that have
  a camera (iOS/Android via the shims; macOS/Windows where a capture API exists). GTK/Skia/Web-without-
  camera show the `⚠️` placeholder. Permissions/capture are **F10** platform services.
- Ends: a "Camera" sample section (live preview + a barcode/OCR scan), gated to camera-capable backends.

## Patterns to reuse (don't reinvent)

- **Multiplexed events:** controls with several callbacks use one event channel + a `kind:body` value
  grammar — copy [`Map.Dispatch`](../src/SwiftDotNet.Maps/Map.cs):61.
- **Rich payloads as string props:** any structured config (theme tokens, gradient stops, detents,
  cell models) serializes to a JSON string prop — copy `MapJson`.
- **Composite = `View.Body`:** copy [`Rating`](../sample/SharedUI/Rating.cs) for every Tier-1 control.
- **CustomView + registry:** copy `Map` + the `*Renderers.Register` seams for Wave 4–5 natives.
- **Service registration:** an `AddSwiftDotNetControls()` extension registering `IToastService` /
  `IDialogService` / `IOverlayService`, mirroring `MauiAppBuilderExtensions.cs` in the source repo.

## Critical files

- **New (Waves 0–5):** `src/SwiftDotNet.Controls/**`, `src/SwiftDotNet.Controls.Apple/**`,
  `src/SwiftDotNet.Controls.Web/**`, `native/controls/**`, additions to
  `sample/SharedUI/ContentView.cs`, `SwiftDotNet.slnx`, a README backend/library row.
- **New (Wave 6 — camera):** `src/SwiftDotNet.Controls.Camera/**`,
  `src/SwiftDotNet.Controls.Camera.Apple/**`, `src/SwiftDotNet.Controls.Camera.{Ai,Barcode,Documents,Face,Motion,Ocr}/**`,
  `native/camera/**`.
- **Read/copy patterns from:** `src/SwiftDotNet.Maps/{Map,MapJson,MapTypes}.cs`,
  `src/SwiftDotNet.Maps.Web/MapLibreMap.cs`, `sample/SharedUI/Rating.cs`,
  `src/SwiftDotNet/Core/{CustomView,View,State,Theme}.cs`.
- **Source to port:** `~/Desktop/dev/controls/src/Shiny.Maui.Controls/**` (Waves 0–5) and
  `~/Desktop/dev/controls/src/Shiny.Maui.Controls.Camera*/**` + `Shiny.Controls.Camera.Shared/**` (Wave 6).

## Verification

- **Per wave:** build `src/SwiftDotNet.Controls` (+ any companion) and the sample on macOS; run the new
  sample tab and screenshot each control on **SwiftUI (macOS), GTK, Web, Skia** (the four buildable here).
- **Parity check:** where feasible, screenshot the same control in the source Shiny sample app and diff
  structure/behavior (not pixels — SwiftDotNet is native-look per backend, except Skia which is uniform).
- **Interaction:** pill/badge render (W1); toast shows+auto-dismisses, dialog confirms, floating panel
  drags to detents (W2); slider pans, table swipes+reorders, tree expands (W3); image viewer pinch-zooms,
  color picker updates hex (W4); signature captures a stroke on Skia, chat scrolls+loads-more (W5);
  live camera preview shows + a barcode/OCR scan fires its callback on a camera-capable backend (W6).
- **Composite = zero native:** confirm every Tier-1 control renders on all four backends **without** a
  registered renderer (proves it decomposed to primitives).
- **Graceful degradation:** a native-only control (FrostedGlass on GTK, SignaturePad off-Skia before its
  renderer lands) shows the tint fallback / `⚠️` placeholder, never a crash.
