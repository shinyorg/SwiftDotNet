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

## Open workstreams

### Dependency injection
[`plans/dependency-injection-proposal.md`](../plans/dependency-injection-proposal.md) — MS.DI container at the
composition root (`AppRoot`); root via ctor injection + a `View.Service<T>()` ambient locator for inline
children; `ISwiftDispatcher` for thread-safe `RequestRender`. Phased (Phase 1 = root + locator; child ctor
injection waits on per-view reconciliation). Ships as a `SwiftDotNet.Extensions.DependencyInjection` add-on so
Core stays dependency-free.

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

### Page / view lifecycle
[`plans/page-lifecycle-plan.md`](../plans/page-lifecycle-plan.md) — `OnLoad`/`OnAppear`/`OnAppearAsync(ct)`/
`OnDisappear`/`OnUnload` + `OnResume`/`OnPause`; visibility is native-authoritative, reported over the
existing event channel. Phase 1 = root; Phase 2 = nav/tab/sheet entries; Phase 3 = inline `Body` children
(gated on reconciliation). Also `IAppLifecycle` and `.OnChange(state, handler)`.

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
