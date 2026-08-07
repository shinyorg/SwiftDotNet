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
  Vello.
