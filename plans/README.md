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
| [Foldables & iPhone Duo](foldables-plan.md) | **Implemented — Android verified (Pixel Fold AVD); iPhone Duo verified closed only** | Open the Duo simulator (hinge is DeviceHub-GUI-only) to verify the fold split + `ArrangementView` on a fold; run the Web reporter in Chromium; Wear re-run after the Compose 1.12 bump; geometry-reader node so `TwoPaneView` needn't fill the root. Docs: [Window posture](../docs/modifiers-gestures-animation.md#window-posture-foldables--iphone-duo), [TwoPaneView](../docs/views-and-controls.md#twopaneview) |
| [Windows / Scenes (multi-window)](windows-plan.md) | Draft — nothing built | Step 0 is de-singletoning `SwiftApp`; then the Swift shim host-handle refactor |
| [MSBuild SDK / custom TFMs](msbuild-sdk-plan.md) | Draft — nothing built | Everything. Prototype-verified: a wrapper SDK is cheap; custom TFMs work but are viral (`NU1202` for stock-SDK consumers) |
| [Native-view access](native-view-access-plan.md) | Draft — nothing built | Everything (`.Tag` + per-backend `Customize` registries) |
| [Rider plugin](rider-plugin-plan.md) | **Phases 1–4 built; verified inside Rider headlessly** | Press Run once by hand — `swiftdotnet-doctor` verifies discovery/gate/devices/planning inside Rider and both mobile heads deploy from its planned commands, but the final `getStateAsync` → Rider-runner hop is unexercised; confirm `IRiderDebuggable` satisfies Rider's debug runner; move the iOS delta-applier reference into the SDK. The Phase-4 spike **solved iOS hot reload** — the cause was `dotnet watch`'s startup hook, not the 127.0.0.1:10000 socket. Docs: [Rider plugin](../docs/rider-plugin.md), [Hot reload](../docs/hot-reload.md) |
| [Live Activities & rich notifications](live-activities-plan.md) | **Implemented** — pure C# tested, drivers unrun | Device/emulator runs, and the five Phase-0 facts they would settle (widget memory budget; whether a custom `Layout` survives archiving; whether `LiveActivityIntent.perform()` really runs in-app; real `RemoteViews` parcel limits; API-36 promotion). Key findings once built: `View.BuildNode` being `internal` **did not** block a satellite vocabulary (`LiveView` emits `Node` directly), the Kotlin interpreter proved unnecessary (a `RemoteViews` is a recipe, and the provider is our process), the `SwiftDotNetWire` extraction was dropped (the two decoders share nothing), and `LiveTimer` — a self-ticking clock costing zero updates — turned out to be the most valuable node in the vocabulary. Docs: [Live surfaces](../docs/live-surfaces.md) |
| [Widgets & the surface channel](widgets-plan.md) | **Implemented** — pure C# tested, drivers unrun | Widget configuration (`WidgetConfigurationIntent` generated from C#), a `dotnet new` template for the Xcode extension target, and a device run. The Apple **inversion** held up in the build: `TimelineAsync` cannot run in the extension, so the app pre-renders every entry × placed family into an App Group and `SDNTimelineProvider` is a dumb reader — which means a widget only shows what the app already computed, and never refreshes itself. Docs: [Live surfaces](../docs/live-surfaces.md) |
| [Wayland host](wayland-host-plan.md) | Draft — not committed to build | Everything; explicitly not scheduled |
| [CarPlay & Android Auto](car-backends-plan.md) | **Tier A implemented — CarPlay verified on the iOS 26.5 simulator; Android Auto builder-validated (9/9), never run on a head unit** | An Android Auto run on the Desktop Head Unit (needs the full Android Auto app, not the emulator's stub) and an AAOS image; the app-drawn map on either platform (CarPlay's `CPMapTemplate` host crashes in the 26.5 simulator); a physical head unit; driving-state signal. Key findings (revising the draft): the vocabulary is a **parallel assembly, not Core views** — the `SwiftDotNet.Live` precedent — because `SwiftApp` is a per-process singleton owned by the phone UI; screens are MVU (`Build()` + `Invalidate()`) rather than `State<T>`; `SwiftDotNetApp.GetOrCreate` makes the phone head and the car entry points share one container; the CarPlay head needs `UseCarPlay()` or the build drops the assembly *and* `CarPlay.framework`. Adapted from the Shiny.Auto prototype. Docs: [CarPlay & Android Auto](../docs/car.md) |
| [Apple Watch](watchos-plan.md) | **Implemented — verified on the watchOS 27 simulator** | A physical arm64 watch, TestFlight and App Store review (acceptance of restamped iOS objects is the open risk); complications (via `SwiftDotNet.Widgets`); companion embedding in the iOS head; the Mac preview window and lldb formatters from the prototype. Key findings: the shared `Bridge.swift` compiles for watchOS with tvOS-style `#if` fallbacks, so **no watch renderer was forked**; the host is *inverted* (Swift owns the process, .NET is a NativeAOT static library restamped from iOS), but `SwiftApp` + `TreeDiffer` run unchanged behind a main-thread `SynchronizationContext`; `ModifierData.value` had to become lenient because `digitalCrown` is the first modifier to ship `value` as a number. Adapted from the Shiny.Wearables prototype. Docs: [Apple Watch](../docs/backends/watchos.md) |
| [Wear OS](wear-os-plan.md) | **Phases 1–3 built; verified on the Wear OS emulator** (API 36 round AVD) | Tiles/complications (a glanceables decision shared with watchOS), a vertical page indicator, Wear `Picker`/`Dialog`, drag-follow swipe-to-dismiss, a physical-device run. Key findings: Wear OS is just the Android backend plus a **skin** — a Kotlin-only form-factor seam (`overrideRenderer` / `setRootContainer`) lets a second `.aar` re-map eight node types without forking the interpreter; the Wear bindings (`Xamarin.AndroidX.Wear.Compose.*` 1.6.2.1) sit on the **same Compose line** the phone graph already pins, so no NU1107 re-pin; there is no .NET binding for Wear Material 3 yet. Docs: [Wear OS](../docs/backends/wear-os.md) |
| [MAUI interop](maui-interop-plan.md) | **Implemented** — engine seam CI-tested; MAUI half builds, never run | Drive the sample's MAUI tab by hand on the iOS simulator and Android emulator (scroll tracking, clipping, the Sheet suppression rule, IME hand-off); a `Map` platform-view renderer; an `IPlatformViewHost` for the non-MAUI Skia hosts so `WebView` punches through there too; the WinUI half has **never compiled**. Key findings: inside a MAUI host **no `IMauiContext`/embedding is needed** (the host *is* a MAUI `ContentView`), MAUI's `UseMauiEmbedding` is **internal** in 10.0.80 (the public path is `UseMauiEmbeddedApp` + `ToPlatformEmbedded`, and naming the type is CS0433), the factory can't ride the JSON wire so identity is the **node id**, transforms are paint-time and therefore a **no-op on platform views**, and every Core reference must be TFM-pinned or a `-ios` build resolves the SwiftUI slice. Docs: [MAUI Interop](../docs/maui-interop.md) |

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
