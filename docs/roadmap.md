# Roadmap

SwiftDotNet is early-stage and an active design space. Detailed design docs live in
[`plans/`](../plans); this page indexes them and the open questions.

## Cross-cutting milestone: per-view reconciliation

Several features are gated on the same milestone — **per-view local state ownership**, i.e. child composite
views keeping local state across renders via view-instance reconciliation. It unblocks:

- child ctor injection for [DI](#dependency-injection),
- enter/leave [transitions](#animation),
- inline-`Body`-child [lifecycle](#page--view-lifecycle) hooks,
- keyed `ForEach` for animated list insert/remove/move.

> **Plan index:** [`plans/README.md`](../plans/README.md) lists every design doc with its real status
> and what's left in each.

## Open workstreams

### Linux / Wayland backend — **scaffolded, unproven**
See [Linux / Wayland](backends/wayland.md). The Skia backend now has a host that talks `xdg-shell` directly:
client-side decorations, an shm swapchain, xkb input with compose and key repeat, fractional scaling,
clipboard and IME. The protocol layer is shared with the .NET MAUI Wayland backend in the sibling
`maui-wayland` repo.

Everything compiles and the protocol tables are unit-tested (23 tests covering signature arity, cross-interface
references and the native `wl_interface` struct layout), but **nothing has been run against a live
compositor** — it was written on macOS. Remaining, in order:

1. First run on GNOME/Mutter, KDE/KWin and a wlroots compositor. These three diverge on decorations,
   fractional scale and layer-shell availability, so all three matter.
2. Route `zwp_text_input_v3` into the Skia text controls — the platform layer surfaces preedit/commit already,
   but `SkiaBridge` still only receives committed text from xkb.
3. `wl_subsurface` overlays for `WebView` / `Map`, which no Skia host can paint.
4. A GPU path (EGL or Vulkan + `zwp_linux_dmabuf_v1`); today it is CPU raster into shared memory.
5. AT-SPI2 accessibility — the genuine cost of self-drawing on Linux, and the reason to keep the
   [GTK4 backend](backends/linux-gtk.md) alongside it rather than replacing it.


### Dependency injection — **Phase 1 shipped**
See [Hosting & Dependency Injection](hosting-and-di.md);
design in [`plans/dependency-injection-proposal.md`](../plans/dependency-injection-proposal.md).
MAUI-style `SwiftProgram.CreateSwiftApp()` + `SwiftDotNetApp.CreateBuilder()`, `[Inject]` partial properties
filled by a **reflection-free source generator**, `View.Service<T>()` for inline children, `IViewLifecycle`
observers + view hooks, and `ISwiftInitializer`. MS.DI is referenced by the **root** library (the add-on idea
was rejected — hosting is the front door).

Remaining: per-page `IServiceScope` (needs the paused
[navigation service](../plans/navigation-service-plan.md)); child-view injection (waits on per-view
reconciliation); `ISwiftDispatcher` — mostly moot, since `SwiftApp` already marshals via the captured
`SynchronizationContext`, so the work is auditing backends that lack one.

### Animation
Implicit `.Animation(spec, on:)` **shipped**, and multi-track
[`.Keyframes(…)` timelines](modifiers-gestures-animation.md#keyframe-animations) **shipped** on every backend
except the TUI, which has no animation clock at all (WinUI's path is written but 🧩 scaffolded — never
compiled). Remaining: explicit `Animate.Run(...)` transactions (needs render **batching** in `SwiftApp` —
`State.Value` currently renders immediately per set) and enter/leave `.Transition(...)` (gated on
reconciliation). Phases: 1 = implicit ✅, 1b = keyframes ✅, 2 = batching + explicit, 3 = transitions.

### Gestures & transforms
`.ScaleEffect` **shipped**. Tap/long-press/swipe (one-shot) **shipped**. Remaining: continuous **pan/pinch**
(need a new
throttled/committed event channel + a `Transformable` container — native-owned live transform, C# syncs on
end); `.Rotation`/`.Offset` siblings; and the `.Tag(name)` native-view-access seam.

### Page / view lifecycle — **Phase 1 shipped**
[`plans/page-lifecycle-plan.md`](../plans/page-lifecycle-plan.md) (reconciled 2026-07-19).
Shipped with DI Phase 1: `OnCreated`/`OnAppearing`/`OnDisappearing`/`OnDestroyed` on `View`, plus
`IViewLifecycle` observers registered in the container and a dispatcher with a defined ordering
(observers → view on setup, view → observers on teardown). See
[Hosting & Dependency Injection](hosting-and-di.md).

**Caveat worth knowing:** visibility is not real yet — `OnAppearing` is currently raised by the host code
path (app start / `ViewScope`), not by actual platform visibility. The next slice is the
**native appear/disappear emitters** per backend (SwiftUI `.onAppear`, Compose `DisposableEffect`, WinUI
`Loaded`, GTK `map`, Blazor `OnAfterRenderAsync`) plus node-id→view routing in `SwiftApp`. Then
`IAppLifecycle`, `.OnChange(state, handler)`, and `OnAppearAsync(ct)`. Per-**page** lifecycle is blocked on
the paused [navigation service](../plans/navigation-service-plan.md); inline-child lifecycle waits on
reconciliation.

### Native view access
[`plans/native-view-access-plan.md`](../plans/native-view-access-plan.md) — tag-based access to a control's
underlying native view (`.Tag` + per-backend `Customize` registries).

### Accessibility & screen readers
[`plans/accessibility-plan.md`](../plans/accessibility-plan.md) — nothing is built. Ten SwiftUI-style
modifiers (`.AccessibilityLabel`/`Hint`/`Value`/`Hidden`/traits/grouping/actions), an `Accessibility`
settings channel over a reserved `$a11y` event id (screen-reader-running, reduce motion, text scale, high
contrast) in the shape of [`SafeArea`](../src/SwiftDotNet/Core/SafeArea.cs), and — the real gap — a Skia
accessibility tree with `UIAccessibilityContainer` / `ExploreByTouchHelper` host adapters, since the Skia
canvas is a single unlabelled rectangle to VoiceOver and TalkBack today.

### Game surface & real-time rendering
[`plans/game-engine-plan.md`](../plans/game-engine-plan.md) — draft, nothing built. Everything the framework
does today is **declarative and native-owned**: `.Animation` and `.Keyframes` hand interpolation to
SwiftUI/CSS/Compose and never report back, so there is no frame clock, no `update(dt)`, no immediate-mode
drawing surface, and no keyboard/multi-touch input anywhere.

The plan adds a `Canvas` node that is diffed **once** and thereafter bypasses
[`IBridge`](../src/SwiftDotNet/Core/IBridge.cs) entirely — necessary because `IBridge.Render` takes a JSON
string on *every* backend, including the pure-C# in-process ones, so no per-frame content can travel that
path. Frames are recorded into a binary `DisplayList` in C# and replayed per backend (the same shape as
Flutter's Dart→engine display list), which works across both the shim and interpreter routes. Phases: 1 =
surface, 2 = `.OnFrame(dt)` + raw input, 3 = paths/blend modes/sprite atlases, 4 = a separate
`SwiftDotNet.Game` library, 5 = the remaining backends. Phases 1–3 are justified by charts, signature pads
and custom gauges on their own; they also **supersede F8** in
[`plans/controls-missing-features-plan.md`](../plans/controls-missing-features-plan.md). Audio and a
portable shader language are explicitly out of scope.

## Backend-specific next steps

- **Windows** — compile + verify the WinUI 3 backend on a Windows host (expect minor API fixes). See
  [Windows backend](backends/windows.md).
- **Skia** — [accessibility bridge](../plans/accessibility-plan.md); `WebView`/`Map` native-overlay punch-through; caret placement and
  selection (the IME replaces the whole string, so edits always land at the end); keyboard avoidance; pan
  inertia/rubber-banding; dirty-rect repaint; the Windows MAUI head. The iOS/Android MAUI TFMs, the AndroidX
  reconciliation, finger scrolling, slider scrubbing and the soft keyboard have landed. See
  [Skia backend](backends/skia.md).
- **Terminal / TUI** — drive it by hand in a live terminal (focus order, mouse reporting, the alternate-screen
  lifecycle are all framework-supplied but unverified by us); wire the terminal-only controls Terminal.UI
  ships and the DSL has no node for — `Table`, `DataGridControl`, `TreeView`, `CodeEditor`,
  `MarkdownControl`, the chart family — through the [`TuiRenderers`](custom-controls.md) seam; consider a
  windowed `List` on Terminal.UI's own `ListBox<T>` virtualization. See [Terminal/TUI](backends/tui.md).
- **WebGPU** — a windowed host (only the headless one exists; mirror
  [`SampleApp.Skia.Silk`](../sample/SampleApp.Skia.Silk)); run it on Vulkan and D3D12, which are written but
  unexercised; real compositing layers so `.Opacity` on an overlapping subtree stops approximating; text
  shaping for scripts needing ligatures/reordering; image formats beyond PNG. See
  [WebGPU backend](backends/webgpu.md).
- **Unity** — open the package in a Unity 6 project and make it compile and run (it never has); verify
  IL2CPP/AOT; wire safe area (needs a host-facing entry point — `SafeArea.Update` is internal) and the soft
  keyboard off `FocusChanged`. Route B (node tree → UI Toolkit `VisualElement`s) remains open if
  Unity-native inspectability ever matters more than reusing the verified Skia backend. See
  [Unity backend](backends/unity.md).
- **Collection View** — true windowed streaming (WinUI/GTK/Web); Web pull-refresh/load-more/windowing (needs
  JS-interop `scrollTop`); Swift load-more wiring. See [Collection View → Deferred](collection-view.md#deferred).

## Framework-wide

- **Binary bridge protocol** — replace JSON on the hot path.
- **Physical-device runs** on iOS/Android (currently simulator/emulator verified).
- **Publish** the combined `SwiftDotNet` + `SwiftDotNet.Gtk` + `SwiftDotNet.Web` (+ Graphics, Skia, WebGPU,
  Tui) as NuGet packages.
- **An arbitrary path primitive** in [`ICanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs), behind a
  capability check rather than in the interface — adding it unconditionally would make every future
  self-drawing backend owe a full vector rasterizer. Skia gets it free; WebGPU would need something like
  Vello. Now scheduled as Phase 3 of the
  [game surface plan](../plans/game-engine-plan.md), alongside blend modes and sprite atlases.
