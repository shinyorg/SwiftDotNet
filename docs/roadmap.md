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
Implicit `.Animation(spec, on:)` **shipped** (see
[Modifiers, Gestures & Animation](modifiers-gestures-animation.md)). Remaining: explicit `Animate.Run(...)`
transactions (needs render **batching** in `SwiftApp` — `State.Value` currently renders immediately per set)
and enter/leave `.Transition(...)` (gated on reconciliation). Phases: 1 = implicit ✅, 2 = batching + explicit,
3 = transitions.

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

## Backend-specific next steps

- **Windows** — compile + verify the WinUI 3 backend on a Windows host (expect minor API fixes). See
  [Windows backend](backends/windows.md).
- **Skia** — accessibility bridge; `WebView`/`Map` native-overlay punch-through; real keyboard IME; dirty-rect
  repaint; iOS/Android MAUI TFMs after the repo-wide bridge/AndroidX reconciliation. See
  [Skia backend](backends/skia.md).
- **Collection View** — true windowed streaming (WinUI/GTK/Web); Web pull-refresh/load-more/windowing (needs
  JS-interop `scrollTop`); Swift load-more wiring. See [Collection View → Deferred](collection-view.md#deferred).

## Framework-wide

- **Binary bridge protocol** — replace JSON on the hot path.
- **Physical-device runs** on iOS/Android (currently simulator/emulator verified).
- **Publish** the combined `SwiftDotNet` + `SwiftDotNet.Gtk` + `SwiftDotNet.Web` (+ Skia) as NuGet packages.
