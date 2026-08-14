# Plan: CarPlay & Android Auto (`SwiftDotNet.CarPlay` / `SwiftDotNet.AndroidAuto`)

**Status:** Draft for review — **nothing built** · **Date:** 2026-08-14
**Save to (repo convention):** `plans/car-backends-plan.md`

> **Scope note.** This plan covers the **template** surface of both car platforms (Tier A) and the
> **navigation drawing surface** both of them expose to navigation-category apps (Tier B). It deliberately
> excludes **media** integration on both platforms — CarPlay audio apps go through
> `MPPlayableContentManager`/`MPNowPlayingInfoCenter` and Android Auto media apps through
> `MediaBrowserServiceCompat`. Neither is template-driven, neither shares anything with this plan, and
> folding them in would corrupt the vocabulary. If media support is wanted it is a separate plan.

## Context

CarPlay and Android Auto look like two more backends. They aren't — they are the first targets where
**the host will not let us draw**. Both are *template systems*: a fixed catalogue of screen shapes, a
screen stack, and a hard cap on how much content each shape may carry, all enforced for driver-distraction
reasons.

| | CarPlay | Android Auto |
|---|---|---|
| Entry point | `CPTemplateApplicationSceneDelegate.DidConnect(scene, interfaceController)` | `CarAppService` → `Session` → `Screen` |
| Stack | `CPInterfaceController.PushTemplate` / `PopTemplate` | `ScreenManager.Push` / `Pop` |
| Re-render | mutate the template (`UpdateSections`) or re-push | `Screen.Invalidate()` → `OnGetTemplate()` re-runs |
| Vocabulary | `CPListTemplate`, `CPGridTemplate`, `CPInformationTemplate`, `CPTabBarTemplate`, `CPAlertTemplate`, … | `ListTemplate`, `GridTemplate`, `PaneTemplate`, `TabTemplate`, `MessageTemplate`, … |
| Depth cap | 5 templates | 5 screens |
| Gate | Apple entitlement, per declared category | Google Play category + car-app quality review |

Android's model is *already* our render loop: `Screen.OnGetTemplate()` + `Invalidate()` is
"re-run the body when state changes". CarPlay's is more imperative but lands in the same place through
`updateProps`. So the machinery below the vocabulary transfers essentially whole — which is the entire
argument for doing this inside SwiftDotNet rather than as two hand-written apps.

**One backend, two Android products.** `androidx.car.app` is the same library for phone-projected Android
Auto *and* Android Automotive OS (AAOS, running natively in the head unit). The AAOS delta is manifest and
distribution, not rendering — so Tier A buys both.

## Crux #1 — the existing `View` DSL cannot be the car DSL

There is no `VStack`, no `.Frame()`, no `.Padding()`, no free composition on either platform. A
`CPListItem` has a title, a detail line, an image and an accessory; that is the whole of it.

**Rejected: auto-lowering the existing tree.** The obvious idea — walk a `VStack { Text; Text }` and
infer `title` / `detailText` — was rejected. It is guesswork at every node, it silently drops anything
unmappable, and the failure mode is a *car* screen that looks subtly wrong on a head unit the developer
cannot see. Worse, it makes the content caps invisible: a `List` of 200 rows is legal on a phone and
illegal while driving. An explicit vocabulary makes the constraint a compile-time fact.

**Adopted: a parallel car vocabulary over the same pipeline.** New view types, same `Node`, same
`TreeDiffer`, same `State<T>`, same event round-trip. The shared code is everything *under* the
vocabulary — which in this repo is the large majority of the framework.

## What is shared, precisely

| Piece | Shared? | Note |
|---|---|---|
| `State<T>` ([`State.cs`](../src/SwiftDotNet/Core/State.cs)) | ✅ verbatim | assignment → invalidate → re-render, unchanged |
| `Node` / `NodeBuilder` / `RenderContext` | ✅ verbatim | car templates are just node types |
| `NodeJson` | ✅ verbatim | still reflection-free |
| `TreeDiffer` | ✅ verbatim | `replace` / `updateProps` / `setChildren` map cleanly (§Node → template) |
| `SwiftApp` render + event round-trip | ✅ verbatim | `RegisterAction(id, handler)` is exactly a row tap |
| `IBridge` | ✅ verbatim | both car bridges implement it as-is |
| `Core/Hosting` (DI, `SwiftDotNetApp.CreateBuilder`) | ✅ verbatim | a car scene resolves services like any head |
| Layout (`GridEngine`, `AbsoluteLayoutBounds`) | ❌ n/a | the head unit lays out; we never measure |
| Modifiers / styles / theme | ⚠️ mostly n/a | see §Modifiers below |
| Renderer seam (`SwiftDotNet.Graphics`) | ✅ **Tier B only** | the nav surface (§Tier B) |

New surface area is therefore roughly **~10 view types + 2 bridges**, not a backend from scratch.

## Crux #2 — both backends are pure C#, route 2

This is the pleasant surprise. CarPlay templates are ordinary Objective-C objects, **already bound in .NET
for iOS** (the `CarPlay` namespace). There is no SwiftUI, no compiler plugin, and therefore **no Swift shim
and no xcframework** — `SwiftDotNet.CarPlay` does *not* import
[`SwiftDotNetBridge.targets`](../src/SwiftDotNet/SwiftDotNetBridge.targets). It is the first Apple target in
this project that is pure C#, and it sits in family 2 alongside GTK/WinUI/Web
(see [Architecture → the two backend routes](../docs/architecture.md#the-two-backend-routes)).

Android needs a binding for `androidx.car.app`. Preferred: the AndroidX binding package
(`Xamarin.AndroidX.Car.App.App`, plus `…Car.App.App.Automotive` for AAOS) — **version and existence to be
confirmed in Phase 0**, since the AndroidX binding set does lag upstream. Fallback: a Java binding project
over the published `.aar`, which is the same mechanism already used for
[`SwiftDotNetComposeBridge.aar`](../src/SwiftDotNet/SwiftDotNet.csproj), only with no Kotlin of our own to
write.

Both bridges are `GtkBridge`-shaped: keep a node tree keyed by structural id, `Find(id)` positionally,
apply the three patch ops. [`GtkBridge`](../src/SwiftDotNet.Gtk/GtkBridge.cs) is the reference to copy.

## Crux #3 — the vocabulary must live in Core, not in the car projects

`View.BuildNode` is `internal`. An out-of-assembly type can only (a) override `Body` and compose existing
views, or (b) derive from [`CustomView`](../src/SwiftDotNet/Core/CustomView.cs) — which emits **flat props
and no children**. Car templates are inherently parented (a list *has* rows; a tab bar *has* tabs), so
`CustomView` is not sufficient and `SwiftDotNet.Controls` gives no precedent to follow here.

**Decision: the car view types live in Core**, at `src/SwiftDotNet/Core/Views/Car/`. They are
dependency-free pure node-builders, so they violate nothing the Core promises, and Core already compiles
for every TFM. The cost is that every backend assembly carries ~10 view types it can never render — types
only, no code paths, and unused types trim out.

**Alternative, not chosen now:** widen the seam (`protected` `BuildNode`, or a `ContainerCustomView` with
children). That is a real want — it is the same seam
[native-view access](native-view-access-plan.md) and the [view construction seam](view-construction-seam.md)
keep circling — but it is a larger decision than this plan should force. Revisit if a third consumer appears.

## Tier A — the car vocabulary

One vocabulary, both platforms:

| Car DSL | CarPlay | `androidx.car.app` |
|---|---|---|
| `CarStack` (root) | `CPInterfaceController` template stack | `ScreenManager` back stack |
| `CarList` / `CarSection` / `CarRow` | `CPListTemplate` / `CPListSection` / `CPListItem` | `ListTemplate` / `SectionedItemList` / `Row` |
| `CarGrid` / `CarGridItem` | `CPGridTemplate` / `CPGridButton` (≤8) | `GridTemplate` / `GridItem` (≈6) |
| `CarPane` | `CPInformationTemplate` | `PaneTemplate` |
| `CarMessage` | `CPAlertTemplate` / `CPActionSheetTemplate` | `MessageTemplate` / `LongMessageTemplate` |
| `CarTabs` / `CarTab` | `CPTabBarTemplate` (≤5) | `TabTemplate` |
| `CarSearch` | `CPSearchTemplate` | `SearchTemplate` |
| `CarPlaceList` | `CPPointOfInterestTemplate` | `PlaceListMapTemplate` |
| `CarAction` | `CPTextButton` / `CPBarButton` | `Action` / `ActionStrip` |
| `CarNavigation` (Tier B) | `CPMapTemplate` | `NavigationTemplate` |

In the DSL's fluent style, mirroring [`ContentView`](../sample/SharedUI/ContentView.cs):

```csharp
public sealed class CarRoot : View
{
    readonly State<int> _selected = State(0);

    public override View? Body =>
        new CarStack(
            new CarList("Playlists")
                .Section("Recent",
                    new CarRow("Morning Drive").Detail("18 tracks").OnTap(() => _selected.Value = 0),
                    new CarRow("Podcasts").Detail("3 new").OnTap(() => _selected.Value = 1))
                .Toolbar(new CarAction("Shuffle", () => Shuffle())));
}
```

**The stack is declarative.** `CarStack`'s children *are* the pushed templates, so a push is a state
change that grows the child list — consistent with how `NavigationStack` already works
([`Navigation.cs`](../src/SwiftDotNet/Core/Views/Navigation.cs)) and it means back-stack depth is
diffable and testable in Core. An imperative `Push`/`Pop` escape hatch on the bridge is fine for
platform-initiated pops (the user pressing Back on the head unit), which must flow *up* as an event.

### Node → template mapping

| Patch op | CarPlay | Android |
|---|---|---|
| `replace` | `SetRootTemplate` | `ScreenManager.PopToRoot` + push |
| `setChildren` on `CarStack` | diff stack depth → `PushTemplate` / `PopTemplate` | `Push` / `Pop` |
| `setChildren` on a template | `UpdateSections` (list) | `Screen.Invalidate()` |
| `updateProps` on a row | `UpdateSections` with rebuilt items | `Screen.Invalidate()` |

Android's templates are immutable builder output, so **any** patch below a screen collapses to
"`Invalidate()` that screen and re-materialize its template from the node". The diff still earns its keep:
it tells us *which* screen, and Android throttles refreshes while driving, so invalidating the whole stack
on every tick would get dropped.

CarPlay is the messier half: `CPListTemplate.UpdateSections` is the supported in-place path, but not every
template is mutable. **Open question — verify per template** (`CPInformationTemplate`,
`CPPointOfInterestTemplate`): where in-place update is unavailable, the fallback is pop-and-re-push the
top template, which is visible to the user and must not be done on every keystroke-level change.

### Modifiers

Most of [`ViewModifiers.cs`](../src/SwiftDotNet/Core/ViewModifiers.cs) is meaningless here — there is no
frame, padding, opacity, transform, or animation to apply. The car vocabulary should **not** silently accept
and drop them. Preferred: the car view types don't inherit the modifier surface at all, or the bridges log
once per unsupported modifier in debug. Decide in Phase 1; the honest-status rule in
[CLAUDE.md](../CLAUDE.md) applies to no-ops too.

## Tier B — the navigation surface (nearly free)

For **navigation-category** apps only, both platforms hand back a real drawing surface:

- CarPlay: the nav app's `CPTemplateApplicationScene` owns a `UIWindow` beneath `CPMapTemplate`.
- Android: `NavigationTemplate` + `SurfaceCallback` → a `SurfaceContainer` with a raw `Surface`.

That is precisely the contract `SwiftDotNet.Graphics` + the Skia/WebGPU renderers already satisfy — the
same shape as the [Wayland host](wayland-host-plan.md): give the engine a buffer, an invalidate signal, and
input events. `SkiaBridge` already exposes `Paint(canvas, size, dark)`, `DispatchPointer`, `Scroll`,
`Tick(dt)` and `event Invalidate`. **No engine changes; only surface plumbing.**

Two caveats that keep this from being literally free:

- **Input is not touch on many head units.** Rotary controllers and touchpads are common, which means
  focus-based navigation the self-drawing engine does not have today. This is the same gap
  [accessibility](accessibility-plan.md) names — Skia is one unlabelled rectangle to a focus engine. Tier B
  is usable with touch-only head units before that lands; not beyond.
- **Entitlement.** Tier B is *only* reachable by an app that already has the navigation entitlement.

## The gate — state this before anyone builds on it

Neither platform is open. Apple grants CarPlay entitlements only per declared category (audio,
communication, navigation, parking, EV charging, fueling, driving task, quick food ordering); Google gates
Android Auto by app category plus a car-app quality review. **SwiftDotNet cannot paper over this.** A
consumer who does not fit a category cannot ship a car app, however good the DSL is. Say so in the docs on
day one rather than letting someone discover it after building.

## Phases

**Phase 0 — spike, no framework code.** A hand-written CarPlay list in a .NET iOS app (simulator: the
CarPlay simulator window in Xcode's *I/O → External Displays*) and a hand-written `CarAppService` list on
the Desktop Head Unit. Deliverables: confirm the `Xamarin.AndroidX.Car.App.App` binding exists and works
(or start the binding project), confirm the CarPlay bindings in .NET for iOS cover the templates in the
table, and write down the entitlement/manifest incantations. *Nothing else starts until this is green.*

**Phase 1 — Core vocabulary + tests.** `Core/Views/Car/*.cs`, the node types, the `CarStack` stack
semantics, and the constraint model. Verified by **Core tests**, which is the half of this project that
actually runs in CI on macOS. Deliverable: lowering + diff tests for push/pop/row-update, including the
content-cap truncation behaviour.

**Phase 2 — `SwiftDotNet.CarPlay` (net10.0-ios).** `CarPlayBridge : IBridge`, modelled on `GtkBridge`; a
`CPTemplateApplicationSceneDelegate` that calls `SwiftApp.Run(root, bridge, services)`. Resolve the
in-place-update question per template. Verified in the CarPlay simulator.

**Phase 3 — `SwiftDotNet.AndroidAuto` (net10.0-android).** `AndroidAutoBridge : IBridge`, a `CarAppService`
+ `Session`, and one `Screen` per stack entry backed by its node. Verified on the DHU, then on an AAOS
emulator image.

**Phase 4 — constraints & driving state.** A `CarConstraints` environment value in Core so apps truncate
*once*: fed by `ConstraintManager.GetContentLimit` on Android and the CarPlay caps on iOS, plus a
parked/driving signal. Without this, every app hard-codes a guess at the smallest head unit.

**Phase 5 — Tier B nav surface.** `ISkiaHost` implementations over the CarPlay window and the Android
`SurfaceContainer`. Gated on having a navigation entitlement to test against.

**Phase 6 — docs.** Per [CLAUDE.md](../CLAUDE.md): `docs/backends/carplay.md` and
`docs/backends/android-auto.md`, rows in the [platform matrix](../docs/backends/README.md), a link from the
[README](../README.md), and honest `🧩 Scaffolded` status until each is actually run on a head unit or
simulator — naming *where* it was verified.

## Projects

Mirroring the `SwiftDotNet.Maps.Apple` split (platform code in its own project, vocabulary shared):

```
src/SwiftDotNet/Core/Views/Car/**     net10.0;net10.0-ios;net10.0-android;…   vocabulary (Crux #3)
src/SwiftDotNet.CarPlay/              net10.0-ios                             CarPlayBridge + scene delegate
src/SwiftDotNet.AndroidAuto/          net10.0-android                         AndroidAutoBridge + CarAppService
sample/SampleApp.CarPlay/             companion iOS app + CarPlay scene
sample/SampleApp.AndroidAuto/         CarAppService head, DHU-runnable
```

## Decisions to make before coding

1. **Vocabulary in Core, or widen the `BuildNode` seam?** (Crux #3.) Recommendation: Core now, seam later.
2. **Declarative `CarStack` vs. an imperative push/pop API.** Recommendation: declarative, with a
   platform-initiated-pop event flowing up.
3. **Do car views carry the standard modifier surface at all?** Recommendation: no — a dropped `.Padding()`
   is worse than a compile error.
4. **One `SwiftDotNet.Car` shared project, or vocabulary-in-Core + two platform projects?**
   Recommendation: the latter, per Crux #3.

## Open questions

- Which CarPlay templates support in-place update, and which force a pop-and-re-push?
- Does `Xamarin.AndroidX.Car.App.App` exist at a usable version, and does it bind `TabTemplate` and
  `ConstraintManager`? (Phase 0 answers this.)
- Do the two platforms' content caps differ enough that a single `CarConstraints` truncation is wrong
  rather than merely conservative?
- Android Auto and the existing Compose backend in one app: does `androidx.car.app` drag AndroidX versions
  that conflict with the Compose/MAUI pins already documented in
  [`SwiftDotNet.csproj`](../src/SwiftDotNet/SwiftDotNet.csproj)? That NU1107 trap has bitten this repo once.
- Tier B: is there a focus/rotary input story short of the full accessibility tree?

## Risks

- **The gate** (§The gate) is the top risk and is entirely outside our control.
- **Head-unit fragmentation** on Android: caps and template support vary by car and by car API level.
- **No CI possible.** Neither platform can be exercised headlessly; this backend family will be
  hand-verified only, like GTK/Web/WinUI. Keep the testable weight in Phase 1 accordingly.
