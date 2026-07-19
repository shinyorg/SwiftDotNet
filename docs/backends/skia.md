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
Sheet bottom-sheet, Alert modal, Menu popover); the custom-renderer registry; gestures (tap; long-press/swipe
dispatch); and an implicit animation clock (opacity + height interpolate). The [Collection View](../collection-view.md)
is fully test-verified on Skia.

## Hosts

The engine is host-agnostic via `ISkiaHost`. Available hosts:

| Host | Project | Notes |
|------|---------|-------|
| **Headless** | [`sample/SampleApp.Skia`](../../sample/SampleApp.Skia) | Console harness → PNGs; `-- <dir> anim` renders animation frames; registers a custom Map renderer. |
| **macOS / AppKit** | [`sample/SampleApp.Skia.Mac`](../../sample/SampleApp.Skia.Mac) | Interactive `NSView` blits the scene; mouse/scroll/keyboard → bridge; `NSTimer(1/60)` → animation clock. |
| **Silk.NET desktop** | [`sample/SampleApp.Skia.Silk`](../../sample/SampleApp.Skia.Silk) | Silk.NET (GLFW) window + GL context; SkiaSharp draws to a GL-backed surface. Dependency-free cross-platform desktop; base for embedded/framebuffer Linux. |
| **MAUI + Shiny** | [`src/SwiftDotNet.Skia.Maui`](../../src/SwiftDotNet.Skia.Maui) + [`sample/SampleApp.Skia.Maui`](../../sample/SampleApp.Skia.Maui) | `SwiftDotNetSkiaView : SKCanvasView`; composes with **Shiny** via `.UseSkiaSharp().UseShiny()` — Skia UI + Shiny plugins share one DI container. |

## Gotchas

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
