# Skia (self-drawing)

The Skia backend is the **self-drawing** family (Flutter/Avalonia model) — it paints *every pixel* with
SkiaSharp, using **no native controls**. It's a from-scratch UI toolkit that owns layout, text shaping,
scrolling, overlays, input/focus, an animation clock, and an icon font, rendering the *whole* shared
[`ContentView`](../../sample/SharedUI/ContentView.cs) **identically on every OS**.

- **Shipped & verified** (headless PNG, interactive macOS window, iOS simulator and Android emulator via the
  MAUI host): renders all tabs identically to the native backends.

## Why it exists

- A **uniform look** on every platform (pixel-identical).
- Targets the native backends can't reach: **dependency-free desktop** (no GTK/WinUI/AppKit) and
  **embedded/framebuffer Linux**.

**Trade-offs:** no native accessibility, and `WebView`/`Map` can't be painted onto a canvas — they need a
native-view overlay (a planned punch-through).

## Engine ([`src/SwiftDotNet.Skia`](../../src/SwiftDotNet.Skia))

net10.0, `RootNamespace SwiftDotNet`, references `SwiftDotNet` + `SkiaSharp` + `SkiaSharp.HarfBuzz`.

| File | Role |
|------|------|
| `SkiaBridge.cs` | `IBridge`; retained `SkiaNode? _root`; applies patches via `Find(id)`; `Paint(canvas,size,dark)` = measure+arrange+paint+overlays; dispatches pointer/scroll/text/long-press/swipe/tick; focus; `TryGetFrame(id)` for tests. |
| `SkiaNode.cs` / `SkiaNodePaint.cs` / `SkiaNodeOverlay.cs` | Retained scene tree: two-pass `Measure`→`Arrange`, `Paint`, `HitTest`, `DispatchGesture`, `ScrollableAt`, animation clock. Covers all node types + modifiers; per-node local state (tab index, scroll offset, pushed content, menu open); overlays. |
| `SkiaText.cs` | Greedy word-wrap + per-run font fallback via `SKFontManager.MatchCharacter` (color emoji/CJK render instead of tofu). |
| `SkiaTheme.cs` | Color/font tokens (light + dark), SF-Symbol → emoji `Icon()`. |
| `SkiaRenderers.cs` | `ISkiaRenderer { Measure, Paint }` custom-renderer registry (demoed with a real Map renderer). |
| `SkiaHost.cs` | `ISkiaHost` abstraction + headless `SkiaImageHost`. |

## Coverage

Layout (VStack/HStack/ZStack/Grid/Group/Form/Section/List/ScrollView/Tab/Nav + padding/frame/align/spacing);
paint (Text/Label/Image/Button/Link/Divider/shapes/ProgressView/Gauge + background/border/shadow/cornerRadius/
opacity/scaleEffect); text (HarfBuzz wrap + fallback + icon font + dark mode); scrolling (offset/clip/
scrollbar); **all inputs** tap-interactive (+ keyboard/drag from the window); nav + overlays (nav bar, push,
Sheet bottom-sheet, Alert modal, Menu popover); the custom-renderer registry; the full gesture set (tap,
long-press, swipe, continuous drag, pinch — see [the router](#gestures-hosts-must-wire-the-pointer-router));
and an implicit animation clock (one-shot opacity + height interpolation, plus self-playing
`.Repeating()` loops). The [Collection View](../collection-view.md) is fully test-verified on Skia.

Two paint-side notes: raster images (`Image.FromFile/FromBytes`, and `Image.FromUrl` via the async
[`SkiaImageLoader`](../../src/SwiftDotNet.Skia/SkiaImageLoader.cs)) are **greedy** — they fill the space
offered, like a shape, so a `.Frame` is what constrains them; and `.Material` blur is a translucent **tint**,
not a real backdrop blur.

### `SwiftDotNet.Controls`

**Every control in [`SwiftDotNet.Controls`](../../src/SwiftDotNet.Controls) renders on Skia**, verified by
pushing all seven "Shiny Controls" sample pages through the headless harness and inspecting the output.
Nothing was needed to make them *paint*: the controls are **pure composites** that lower to the core views
Skia already draws — pills, badges, skeletons, progress, sliders, PIN entry, autocomplete, colour picker,
duration picker, table/tree/data-grid, staggered grid, carousel, scheduler (calendar + agenda), chat,
toasts, dialogs, FABs, floating panels and frosted glass.

The one exception to "pure composite" is `CameraView`, which is a **custom native primitive**. Skia has no
capture stack, so the sample registers an honest viewfinder placeholder rather than faking a feed — see
[`SkiaSampleRenderers`](../../sample/SampleApp.Skia.Renderers/SkiaSampleRenderers.cs). Without a
registered renderer it would paint the generic "⚠️ unknown view" box, which reads as a bug rather than an
unsupported capability.

#### What a finger needs that a mouse got for free

A self-drawing backend has no toolkit recognizers *and*, on a touch host, no wheel. Three interactions
therefore have to be resolved from the raw pointer stream, and until they were, they read as dead controls:

| Interaction | How it resolves | Why it was inert |
|---|---|---|
| **Scroll** | `SkiaBridge.BeginPan` / `PanScroll` — the innermost scrollable under the press, panned 1:1 once the finger passes `TapSlop` | Scrolling only ever arrived via `Scroll(point, dy)`, which a host raises from a **wheel**. A touch host has none, so a long `Form` could not be scrolled at all. |
| **Slider drag** | `SkiaBridge.BeginScrub` / `Scrub` — captures a continuous control and tracks the finger, keeping it even once the finger leaves its bounds | The drag path keys off an **`.OnDrag` modifier**, which the Controls library's sliders carry but the built-in `Slider` does not — it was tap-to-set only. Worse than "drag does nothing": passing `TapSlop` also cancels the tap, so dragging a slider left it exactly where it started. |
| **Text entry** | The host raises a real IME off `SkiaBridge.FocusChanged` (below) | The engine tracked focus and had `InsertText`, but nothing raised a keyboard, so tapping a field only blinked a caret. |

Only *continuous* controls scrub. Stepper / Picker / DatePicker / ColorPicker are discrete and stay
tap-driven, or a press-and-drag would fire them once per pointer-move event. Covered by
[`SkiaTouchScrubTests`](../../tests/SwiftDotNet.Tests/SkiaTouchScrubTests.cs).

The **`ColorPicker`** opens a swatch popover (engine-local, like a `Menu`) with the current colour ringed.
It used to advance blindly to the next palette entry per tap, which is indistinguishable from a broken
control — you could step past the colour you wanted but never choose it.

Presentations *inside* an overlay's content — a `Menu` or that colour popover on a **pushed nav page**, a
sheet from within a sheet — are composited too. They were previously dropped: the overlay walk only
descended through `Children`, and an overlay's content subtree hangs off the node instead.

#### Soft keyboard (MAUI hosts)

A canvas cannot raise a keyboard; only a focused native text input can. So `SwiftDotNetSkiaView` is a
`ContentView` wrapping the canvas **plus a 1×1 transparent `Entry`**, focused whenever the engine focuses a
text control:

```csharp
bridge.FocusChanged += id => { … id is null ? entry.Unfocus() : entry.Focus(); };  // engine → IME
entry.TextChanged  += (_, e) => bridge.SetFocusedText(e.NewTextValue ?? "");        // IME → engine
```

Text crosses as the **whole string**, not keystrokes — that is the only form that survives autocorrect,
dictation, paste and selection edits. `SecureField` sets `IsPassword`; `TextEditor` gets a return key
instead of Done.

> **Gotcha:** the shadow entry must **not** be `InputTransparent`. On iOS that maps to
> `UserInteractionEnabled = false`, and a view that cannot be interacted with cannot become first
> responder — `Focus()` silently returns `false` and no keyboard ever appears. (Android allows the
> programmatic focus either way, so this fails on exactly one platform.)

#### Gestures: hosts must wire the pointer router

Making the controls **interactive** did need something, and it's easy to miss. The controls that respond to a
continuous gesture — `Slider`, `RangeSlider`, `ColorPicker`, `FloatingPanel`, `SwipeContainer`,
`ReorderableList`, `ImageViewer` — depend on `.OnDrag` / `.OnMagnify`. Every other backend inherits those
recognizers from its toolkit; a self-drawing backend has none, so the engine's `SkiaBridge.Drag/Magnify`
sat unused while hosts forwarded only taps, and those seven controls rendered perfectly and did nothing.

[`SkiaPointerRouter`](../../src/SwiftDotNet.Skia/SkiaPointerRouter.cs) closes that: hosts feed it raw
pointer events and it resolves tap / long-press / swipe / drag / pinch. **A host that does not use it gets
tap-only interaction.** Wiring is four calls:

```csharp
var router = new SkiaPointerRouter(bridge);
// pointer events
router.Down(point, timeSeconds);
router.Move(point, timeSeconds);
router.Up(point, timeSeconds);
// once per frame, off the same clock that drives bridge.Tick — the long-press timer needs it
router.Poll(timeSeconds);
```

The router resolves a press in order of specificity: an `.OnDrag` node → a continuous control to scrub →
a scrollable to pan → tap / long-press / swipe. A host that feeds it gets all of them; a host that
forwards only taps gets only taps.

Hosts with a real pinch recognizer forward it to `router.Pinch(...)`; hosts without one get ctrl+wheel /
trackpad zoom from `router.PinchDelta(...)`. All three in-repo hosts are wired. Behaviour is covered by
[`SkiaPointerRouterTests`](../../tests/SwiftDotNet.Tests/SkiaPointerRouterTests.cs) (the router takes an
explicit clock, so the tests drive time directly rather than sleeping).

## Hosts

The engine is host-agnostic via `ISkiaHost`. Available hosts:

| Host | Project | Notes |
|------|---------|-------|
| **Headless** | [`sample/SampleApp.Skia`](../../sample/SampleApp.Skia) | Console harness → PNGs; `-- <dir> anim` renders animation frames. Walks the whole flyout, including every controls page. |
| **macOS / AppKit** | [`sample/SampleApp.Skia.Mac`](../../sample/SampleApp.Skia.Mac) | Interactive `NSView` blits the scene; mouse/scroll/keyboard → router → bridge; `NSTimer(1/60)` drives both the animation clock and `router.Poll`. Real trackpad pinch via `MagnifyWithEvent`; ⌃-scroll zooms on a mouse. |
| **Silk.NET desktop** | [`sample/SampleApp.Skia.Silk`](../../sample/SampleApp.Skia.Silk) | Silk.NET (GLFW) window + GL context; SkiaSharp draws to a GL-backed surface. Dependency-free cross-platform desktop; base for embedded/framebuffer Linux. GLFW has no pinch event, so zoom is ctrl+wheel. |
| **MAUI + Shiny** | [`src/SwiftDotNet.Skia.Maui`](../../src/SwiftDotNet.Skia.Maui) + [`sample/SampleApp.Skia.Maui`](../../sample/SampleApp.Skia.Maui) | `SwiftDotNetSkiaView` (a `ContentView` over an `SKCanvasView` + a shadow `Entry` for the soft keyboard); composes with **Shiny** via `.UseSkiaSharp().UseShiny()` — Skia UI + Shiny plugins share one DI container. Real two-finger pinch via MAUI's `PinchGestureRecognizer`. Targets `net10.0-ios;net10.0-maccatalyst;net10.0-android` (+ `net10.0-windows…` when built on Windows). ✅ Verified on the **iOS simulator** and the **Android emulator**: full sample renders; nav push/pop; finger scrolling; slider drag; colour-picker popover; soft keyboard typing into a bound `TextField`; state round-trips repaint. |

> An iOS app hosting this view must also `<Import>` [`SwiftDotNetBridge.targets`](../../src/SwiftDotNet/SwiftDotNetBridge.targets)
> — the app's ProjectReference graph resolves `SwiftDotNet` to its iOS slice, whose Swift-bridge P/Invokes
> the native linker must resolve even though the Skia route never calls them.

## Gotchas

- **A `ScrollView` centres its content** (a `Form`/`List`/`Section` is leading-aligned instead). A composite
  whose header sits outside the scroll view and whose rows sit inside it will therefore *not* line up unless
  the inner stack fills the width — add `.Align(Alignment.Leading)`. `DataGrid` hit exactly this.
- **A greedy child in an `HStack` shrinks only when the row would overflow.** `TextField`, `Slider`,
  `Toggle`, `Picker` and anything with a `maxWidth` frame each claim the full width offered; when the row
  doesn't fit, they share what is left after the fixed-size siblings. Rows that already fit keep their
  natural sizing. Regression-tested in
  [`SkiaRowOverflowTests`](../../tests/SwiftDotNet.Tests/SkiaRowOverflowTests.cs).
- **`SKTypeface.Default` has no emoji coverage.** Text drawn by the engine goes through the fallback chain
  and renders emoji fine, but a *custom renderer* calling `SKFont(SKTypeface.Default, …)` directly will paint
  tofu — resolve a fallback face or avoid emoji in custom paint code.
- **No AppKit SkiaSharp views package exists** (`SkiaSharp.Views.Mac`/`.Apple`/`.iOS` are not on NuGet). The
  macOS host blits `SKSurface → PNG → NSImage` into an `NSView` itself.
- The Skia **macOS** app must import [`SwiftDotNetBridge.targets`](../../src/SwiftDotNet/SwiftDotNetBridge.targets)
  and pin `<RuntimeIdentifier>osx-arm64</RuntimeIdentifier>` — referencing `SwiftDotNet` on the macos TFM
  compiles the SwiftUI `MacBridge` P/Invoke, so the linker needs the Swift xcframework symbols even though
  Skia never calls them.
- **Shapes are greedy** (fill offered space; `.Frame` overrides) — SwiftUI parity.
- `Picker`/`Menu` etc. must **not** paint their non-visual children (options/actions) — they'd land at (0,0).
- The overlay walk must **respect TabView selection**, or a presentation in a hidden tab bleeds onto the
  visible one.
- **MAUI's two halves must be pinned to one version.** Since .NET 8, `<UseMaui>true</UseMaui>` no longer
  implies the package references (warning `MA002`), so the workload supplies `Microsoft.Maui.Controls.Core`
  at the manifest version while a transitive dependency (Shiny 5.2.3 → `Microsoft.Maui.Core 10.0.80`) floats
  the *other* half higher. Nothing fails at build time; the app dies at **launch** with
  `TypeLoadException: VTable setup of type Microsoft.Maui.Controls.Page failed`. Both the host library and
  the sample head therefore reference `Microsoft.Maui.Controls` explicitly. This was previously mis-recorded
  here as a workload gap needing `dotnet workload update` — it is a version-pinning problem in the project.
- **Switching `-p:NoShiny=true` on or off needs a clean.** The `.app` directory is patched incrementally, so
  assemblies from the previous configuration stay behind and mix with the new ones — which produces exactly
  the same `Page` vtable `TypeLoadException` and looks like the bug above. `rm -rf bin obj` first.
- **Android is a three-way AndroidX negotiation.** An app on the Skia MAUI host pulls MAUI's AndroidX graph
  *and*, through `SwiftDotNet`'s android slice, the Compose backend's — so the Compose pins in
  [`SwiftDotNet.csproj`](../../src/SwiftDotNet/SwiftDotNet.csproj) are not free to drift (they carry a
  comment saying so). Two constraints, both discovered the hard way: `Activity.Compose` must be ≥ 1.10.1
  (older ones pin `Activity.Ktx` below what MAUI's `Fragment.Ktx` needs → `NU1107`), and the Compose set must
  be ≥ 1.10 (before that, `androidx.compose.runtime.Immutable` lives in the runtime AAR, and MAUI's
  `NavigationEvent` → `Compose.Runtime.Annotation` ships it too → a **D8 duplicate-class** failure, not a
  NuGet one). Separately, MAUI 10.0.x's own graph is internally unsatisfiable on Android — `Glide` →
  `Fragment 1.8.9.3` wants `Lifecycle.LiveData.Core >= 2.11.0.1` while `Microsoft.Maui.Core` pins
  `LiveData 2.9.2.1` — so the host library takes a direct `LiveData`/`LiveData.Core` reference to break it.
- **Mac Catalyst launch:** launch via `open` (LaunchServices), not by exec'ing the Mach-O (direct exec fails
  Catalyst env setup). Release builds trim MAUI types → run Debug for dev.

## Next

Accessibility bridge; `WebView`/`Map` native-overlay punch-through; caret placement and selection (the IME
replaces the whole string, so you always edit at the end); keyboard-avoidance (the engine does not know how
much of the canvas the keyboard covers); fling/inertia and rubber-banding on a pan; dirty-rect repaint; the
Windows MAUI head (needs a Windows machine to build). See the [Roadmap](../roadmap.md).

## Hot reload

✅ **Verified here.** `dotnet watch run --project sample/SampleApp.Skia.Silk`, then edit a `Body` and save:
the window redraws in place with `State<T>` preserved. String, structural, and added-field edits all
applied live (45–282 ms measured).

One host-side requirement: Silk/GLFW has no `SynchronizationContext`, so the sample installs a
`RenderLoopSyncContext` before `SwiftApp.Run` captures it. Without one, the runtime's update thread would
rebuild the scene tree concurrently with the paint loop. See [Hot Reload](../hot-reload.md).
