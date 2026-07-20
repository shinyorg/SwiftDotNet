# Skia (self-drawing)

The Skia backend is the **self-drawing** family (Flutter/Avalonia model) — it paints *every pixel* with
SkiaSharp, using **no native controls**. It's a from-scratch UI toolkit that owns layout, text shaping,
scrolling, overlays, input/focus, an animation clock, and an icon font, rendering the *whole* shared
[`ContentView`](../../sample/SharedUI/ContentView.cs) **identically on every OS**.

- **Shipped & verified** (headless PNG + interactive macOS window): renders all tabs identically to the
  native backends.

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
| **MAUI + Shiny** | [`src/SwiftDotNet.Skia.Maui`](../../src/SwiftDotNet.Skia.Maui) + [`sample/SampleApp.Skia.Maui`](../../sample/SampleApp.Skia.Maui) | `SwiftDotNetSkiaView : SKCanvasView`; composes with **Shiny** via `.UseSkiaSharp().UseShiny()` — Skia UI + Shiny plugins share one DI container. Real two-finger pinch via MAUI's `PinchGestureRecognizer`. Targets `net10.0-ios;net10.0-maccatalyst`. ⚠️ **State-driven repaint is broken on this host** — see below. |

> ⚠️ **Known defect — the MAUI host does not repaint on state change.** Run on an iOS simulator, the tree
> paints and engine-local input works (nav push/pop), but nothing driven by a C# `State<T>` change ever
> reaches the screen. Touches arrive with correct coordinates, the frame clock runs, and `Emit` reaches
> Core's handler with a live `UIKitSynchronizationContext` — so the break is after the event and before
> the canvas. The AppKit and Silk hosts are unaffected. Leading suspect is `SwiftApp`'s static singleton
> state being rebound by a second `SwiftDotNetSkiaView`. Triage steps and hypotheses:
> [`plans/skia-maui-host-plan.md`](../../plans/skia-maui-host-plan.md).
>
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
- **MAUI + Shiny blocker (not a code defect):** a MAUI workload version gap — Shiny 5.2.3 wants
  `Microsoft.Maui.Core 10.0.80` but the installed workload is 10.0.20, causing a runtime
  `TypeLoadException: Page vtable failed to initialize`. Fix: `dotnet workload update`. Meanwhile
  `-p:NoShiny=true` builds/runs without Shiny.
- **Mac Catalyst launch:** launch via `open` (LaunchServices), not by exec'ing the Mach-O (direct exec fails
  Catalyst env setup). Release builds trim MAUI types → run Debug for dev.

## Next

Accessibility bridge; `WebView`/`Map` native-overlay punch-through; real keyboard IME + slider-drag polish;
dirty-rect repaint; iOS/Android MAUI TFMs (after the repo-wide bridge/AndroidX reconciliation). See the
[Roadmap](../roadmap.md).

## Hot reload

✅ **Verified here.** `dotnet watch run --project sample/SampleApp.Skia.Silk`, then edit a `Body` and save:
the window redraws in place with `State<T>` preserved. String, structural, and added-field edits all
applied live (45–282 ms measured).

One host-side requirement: Silk/GLFW has no `SynchronizationContext`, so the sample installs a
`RenderLoopSyncContext` before `SwiftApp.Run` captures it. Without one, the runtime's update thread would
rebuild the scene tree concurrently with the paint loop. See [Hot Reload](../hot-reload.md).
