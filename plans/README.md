# Plans

Design docs for work that is proposed, in flight, or partly landed. **These are the historical record —
the *why*, the alternatives that were rejected, and the decisions still open.** The user-facing reference
lives in [`docs/`](../docs/README.md); when a plan and the docs disagree, the docs describe what the code
does today and the plan describes how it got there.

A plan is deleted once it is fully implemented and its content lives in `docs/` (e.g. the Skia backend
plan, removed 2026-07-19 — see [Skia backend](../docs/backends/skia.md)).

## Status at a glance — 2026-08-08

| Plan | Status | What's left |
|---|---|---|
| [Dependency injection](dependency-injection-proposal.md) | **Phase 1 shipped** | `ISwiftDispatcher` for the Skia hosts; verify the Windows head; `SDN1003` false positives. Docs: [Hosting & DI](../docs/hosting-and-di.md) |
| [Page & view lifecycle](page-lifecycle-plan.md) | **Partially shipped** | The big one: **native visibility emitters** — `OnAppearing` isn't wired to real platform visibility yet. Then `IAppLifecycle`, `.OnChange`, `OnAppearAsync(ct)` |
| [Controls: missing framework features](controls-missing-features-plan.md) | **Partially shipped** | Wave A done; F7 collections, F8 drawing canvas, F10 services, F11 geometry |
| [Controls library](controls-library-plan.md) | **Partially shipped** | VirtualizedGrid, ~8 cell types, Compose/WinUI camera renderers; camera not device-verified |
| [Safe area insets](safe-area-insets-plan.md) | **Implemented, unverified** | Device/simulator run (notched iOS sim + Android 15 emulator); RTL reconciliation; a sample that uses it; decide on the `SafeAreaRegions` name collision with MAUI. Docs: [Safe area](../docs/modifiers-gestures-animation.md#safe-area-ios--android-only) |
| [Skia MAUI host](skia-maui-host-plan.md) | **Resolved** — iOS + Android verified, incl. touch scroll / slider / soft keyboard | Caret placement & selection; keyboard avoidance; pan inertia; automated coverage of the MAUI adapter; the Windows head. The "repaint defect" was a *sample* bug (a rebuilt stateful child), not a host bug. Docs: [Skia](../docs/backends/skia.md) |
| [Accessibility & screen readers](accessibility-plan.md) | Draft — nothing built | Everything. Phase 1 (Core modifiers + the `$a11y` settings channel) is standalone; Phases 3–4 (the Skia accessibility tree + its iOS/Android host adapters) are where the real gap is — Skia is a single unlabelled rectangle to VoiceOver/TalkBack today |
| [Game surface & real-time rendering](game-engine-plan.md) | Draft — nothing built | Everything. Phases 1–3 (a `Canvas` node, a `DisplayList` recorder, `.OnFrame(dt)` vsync, raw input, paths/blend/atlases) **supersede F8** and stand on their own for charts/signature pads; Phase 4 (`SwiftDotNet.Game`) is a separate bet to decide at the Phase-2 checkpoint. Key finding: `IBridge.Render` takes JSON on *every* backend, so the surface must bypass it entirely |
| [Navigation service](navigation-service-plan.md) | ⏸ **Paused** | Everything. Would be the first consumer of `ViewScope` (built, no caller) |
| [View construction seam](view-construction-seam.md) | Draft | Decision 1 — adopt the function form (`Text()` vs `new Text()`)? The `[Inject]` generator it once owned already shipped |
| [Windows / Scenes (multi-window)](windows-plan.md) | Draft — nothing built | Step 0 is de-singletoning `SwiftApp`; then the Swift shim host-handle refactor |
| [MSBuild SDK / custom TFMs](msbuild-sdk-plan.md) | Draft — nothing built | Everything. Prototype-verified: a wrapper SDK is cheap; custom TFMs work but are viral (`NU1202` for stock-SDK consumers) |
| [Native-view access](native-view-access-plan.md) | Draft — nothing built | Everything (`.Tag` + per-backend `Customize` registries) |
| [Rider plugin](rider-plugin-plan.md) | **Phases 1–4 built; verified inside Rider headlessly** | Press Run once by hand — `swiftdotnet-doctor` verifies discovery/gate/devices/planning inside Rider and both mobile heads deploy from its planned commands, but the final `getStateAsync` → Rider-runner hop is unexercised; confirm `IRiderDebuggable` satisfies Rider's debug runner; move the iOS delta-applier reference into the SDK. The Phase-4 spike **solved iOS hot reload** — the cause was `dotnet watch`'s startup hook, not the 127.0.0.1:10000 socket. Docs: [Rider plugin](../docs/rider-plugin.md), [Hot reload](../docs/hot-reload.md) |
| [Wayland host](wayland-host-plan.md) | Draft — not committed to build | Everything; explicitly not scheduled |
| [CarPlay & Android Auto](car-backends-plan.md) | Draft — nothing built | Everything. Phase 0 is a two-platform spike that answers whether the `androidx.car.app` binding exists and which CarPlay templates update in place. Key findings: both platforms are **template** systems (the existing `View` DSL can't lower into them), both bridges are **pure C#** (no Swift shim — a first for an Apple target), the car vocabulary must live in Core because `View.BuildNode` is `internal`, and Tier B (the navigation drawing surface) is nearly free reuse of the Skia engine. Gated by Apple/Google category entitlements we don't control |

## Cross-cutting milestone: view-instance reconciliation

Four plans defer their last phase to the same unstarted milestone — keyed identity for child `View`
instances across renders, so an inline `Body` child is a stable object rather than a fresh one each pass:

- DI — container-created child views, scoped-per-view lifetimes
- Page lifecycle — lifecycle for inline children
- View construction seam — Tier 1 positional retention (this plan *is* that milestone, approached from
  the construction side)
- Animations — enter/leave transitions, keyed `ForEach`

Nothing has started on it. It is the single highest-leverage piece of unbuilt framework work.

## Second cross-cutting milestone: de-singletoning `SwiftApp`

`SwiftApp` keeps `_bridge`, `_lastTree` and `_uiContext` in statics, so exactly one view tree can be live
per process. [Windows / Scenes](windows-plan.md) names this as its Step 0. It is small and well-scoped, but
it has **no concrete bug driving it** — the [Skia MAUI host](skia-maui-host-plan.md) defect was once
attributed to a second host view rebinding those statics, and instrumentation on a simulator disproved that
(`Run` was called exactly once; the real cause was a rebuilt stateful child in the sample). The motivation
is multi-window, and multi-window alone.
