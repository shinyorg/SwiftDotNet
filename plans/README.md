# Plans

Design docs for work that is proposed, in flight, or partly landed. **These are the historical record —
the *why*, the alternatives that were rejected, and the decisions still open.** The user-facing reference
lives in [`docs/`](../docs/README.md); when a plan and the docs disagree, the docs describe what the code
does today and the plan describes how it got there.

A plan is deleted once it is fully implemented and its content lives in `docs/` (e.g. the Skia backend
plan, removed 2026-07-19 — see [Skia backend](../docs/backends/skia.md)).

## Status at a glance — 2026-07-19

| Plan | Status | What's left |
|---|---|---|
| [Dependency injection](dependency-injection-proposal.md) | **Phase 1 shipped** | `ISwiftDispatcher` for the Skia hosts; verify the Windows head; `SDN1003` false positives. Docs: [Hosting & DI](../docs/hosting-and-di.md) |
| [Page & view lifecycle](page-lifecycle-plan.md) | **Partially shipped** | The big one: **native visibility emitters** — `OnAppearing` isn't wired to real platform visibility yet. Then `IAppLifecycle`, `.OnChange`, `OnAppearAsync(ct)` |
| [Controls: missing framework features](controls-missing-features-plan.md) | **Partially shipped** | Wave A done; F7 collections, F8 drawing canvas, F10 services, F11 geometry |
| [Controls library](controls-library-plan.md) | **Partially shipped** | VirtualizedGrid, ~8 cell types, Compose/WinUI camera renderers; camera not device-verified |
| [Navigation service](navigation-service-plan.md) | ⏸ **Paused** | Everything. Would be the first consumer of `ViewScope` (built, no caller) |
| [View construction seam](view-construction-seam.md) | Draft | Decision 1 — adopt the function form (`Text()` vs `new Text()`)? The `[Inject]` generator it once owned already shipped |
| [Windows / Scenes (multi-window)](windows-plan.md) | Draft — nothing built | Step 0 is de-singletoning `SwiftApp`; then the Swift shim host-handle refactor |
| [MSBuild SDK / custom TFMs](msbuild-sdk-plan.md) | Draft — nothing built | Everything. Prototype-verified: a wrapper SDK is cheap; custom TFMs work but are viral (`NU1202` for stock-SDK consumers) |
| [Native-view access](native-view-access-plan.md) | Draft — nothing built | Everything (`.Tag` + per-backend `Customize` registries) |
| [Wayland host](wayland-host-plan.md) | Draft — not committed to build | Everything; explicitly not scheduled |

## Cross-cutting milestone: view-instance reconciliation

Four plans defer their last phase to the same unstarted milestone — keyed identity for child `View`
instances across renders, so an inline `Body` child is a stable object rather than a fresh one each pass:

- DI — container-created child views, scoped-per-view lifetimes
- Page lifecycle — lifecycle for inline children
- View construction seam — Tier 1 positional retention (this plan *is* that milestone, approached from
  the construction side)
- Animations — enter/leave transitions, keyed `ForEach`

Nothing has started on it. It is the single highest-leverage piece of unbuilt framework work.
