# Plan: Wear OS (`SwiftDotNet.Wear`)

**Status:** Phases 1–3 built and **verified on the Wear OS emulator** · Phases 4–5 open · **Date:** 2026-09-28
**Docs:** [Wear OS backend](../docs/backends/wear-os.md)

## Context

Wear OS *is* Android. A .NET Android app runs on a watch unchanged, and SwiftDotNet already renders to
Jetpack Compose through the Kotlin interpreter in
[`native/SwiftDotNetComposeBridge`](../native/SwiftDotNetComposeBridge). So "Wear OS support" was never a new
backend — the question was only which parts of the phone rendering are the wrong *shape* on a 1.3" round
screen, and how to swap those without forking the interpreter.

Background: the MauiNext prototype plan (`Prototypes/MauiNext/WATCH-CROSSPLATFORM-PLAN.md`, Phase 2–3) scoped
the same job for Shiny.Wearables and estimated it as "write a Kotlin/Compose-for-Wear renderer that
interprets the JSON tree". In SwiftDotNet that renderer already exists, which is why this is small.

## What is the wrong shape on a watch

| Node | Phone (Compose/M3) | Why it's wrong on a watch | Wear mapping |
|---|---|---|---|
| `List` / `Form` / vertical `ScrollView` | `Card` of rows / scrolling `Column` | square-cornered, no edge scaling, no rotary scroll, clipped by a round bezel | `ScalingLazyColumn` + `PositionIndicator` + rotary scrolling |
| `Section` | inset-grouped card | nested cards waste the width | `ListHeader` + its rows, **flattened** into the column |
| `Button` | M3 `Button` | pill buttons are not the Wear idiom for list rows | `Chip` (row) / round `Button` (short glyph like `+`) |
| `Toggle` | label + `Switch` in a row | same | `ToggleChip` + Wear `Switch` |
| `NavigationStack` / `NavigationLink` | `TopAppBar` + back arrow | a watch has no app bar; back is a swipe | own stack; system back pops; title becomes the list header |
| `TabView` | bottom `NavigationBar` / horizontal pager | watchOS 10 and Wear page **vertically** | `VerticalPager` |
| `Slider` / `Stepper` | M3 slider / wide label-and-buttons row | too wide, drag is fiddly | `InlineSlider` / round −/+ buttons |
| `Gauge` | linear bar | watches use rings | `CircularProgressIndicator` |

Everything else — text, stacks, shapes, images, the whole modifier pass — renders fine and is left to the
phone interpreter, drawn in a dark Material 3 theme.

## Decision 1 — a separate `.aar` + package, not a runtime "wear mode" in the main bridge

**Adopted:** a second Gradle module (`native/SwiftDotNetComposeBridge/wear`, built by the same
`assembleRelease`) → `build/SwiftDotNetWearBridge.aar`, bound by a new `src/SwiftDotNet.Wear`
(`net10.0-android`, minSdk 30) that references `Xamarin.AndroidX.Wear.Compose.{Foundation,Material}`.

**Rejected: a runtime flag inside `Bridge.kt`.** It would have to either bundle `androidx.wear.compose` into
the main `.aar` (every phone app carries it) or `compileOnly` it and rely on lazy class loading never
touching a Wear class on a phone. More importantly it would have put the Wear packages in
[`SwiftDotNet.csproj`](../src/SwiftDotNet/SwiftDotNet.csproj)'s Android `PackageReference`s — the exact
place the **NU1107** AndroidX pin trap lives. Confining the Wear graph to the Wear package means a bad Wear
bump can only break watch apps.

**The pin check that made this cheap:** `Xamarin.AndroidX.Wear.Compose.Material 1.6.2.1` depends on
Compose `1.11.3.1` and `Activity.Compose 1.13.0.1` — the same line `SwiftDotNet.csproj` already pins
(Compose `1.11.4`, Activity.Compose `1.13.0.1`). No re-pinning of the phone graph was needed. There is no
.NET binding for Wear **Material 3** (`androidx.wear.compose:compose-material3`) at the time of writing, so
the skin uses Wear Material (2) components; revisit when the binding ships.

## Decision 2 — a form-factor seam in the main interpreter, not a fork

The Wear module needs to re-skin *built-in* types, which the existing `registerRenderer` cannot do (it only
fills in unknown custom types). The main bridge gained three Kotlin-only hooks:

- `overrideRenderer(type, @Composable (VNode) -> Unit)` — consulted **before** the built-in `when`.
- `setRootContainer(@Composable (content) -> Unit)` — wraps the whole tree (theme, `Scaffold`, `TimeText`).
- `RenderNode(node)` — the interpreter's entry point, so an override can recurse into children with the full
  modifier pass.

plus `LocalRootAlignment` (a round face centres the root; phones keep top-start) and `systemImageGlyph`.

All are `@JvmSynthetic`: the .NET binding generator never sees `Composer` parameters or function types, and
C# installs the skin through the Wear module's plain `SwiftDotNetWear.install()`. The seam is generic —
nothing in it says "watch" — so a future form factor (TV/Leanback, Android Auto's non-template surfaces)
can use it the same way.

## Decision 3 — the rotary modifier lives in the main bridge

`.DigitalCrownRotation(value, onChange, from, through, by)` is a Core modifier (wire
`{"type":"digitalCrown","value","from","through","by","event"}`; event payload = new value, invariant
culture), shared with the watchOS work. Its Compose side uses `Modifier.onRotaryScrollEvent`, which is
plain `androidx.compose.ui` — **no Wear dependency** — so it is implemented in the main bridge and works on
any Android device with a rotary encoder.

- **Calibration:** rotary arrives as scroll *pixels* (axis value × `ViewConfiguration.scaledVerticalScrollFactor`).
  Dividing the factor back out gives encoder units, and **one unit = one `by` step**. A first cut used a
  fixed 48 px/step and moved ~2.6 steps per emulator tick; the factor-based version was verified at exactly
  one step per tick in both directions.
- **Focus:** rotary events only reach the focused node, so the modified view requests focus on first
  composition. A `ScalingLazyColumn` also requests rotary focus; the last one composed wins.
- **Latency:** the in-flight value is tracked locally between events so a fast spin isn't throttled by the
  C# round-trip, and re-synced whenever C# sends a different value.

## Phases

- **Phase 1 — seam + rotary + Core modifier.** ✅ Built. Core tests (`DigitalCrownTests`) for the wire
  contract; phone `SampleApp` (Android) still builds against the modified `.aar`.
- **Phase 2 — Wear skin.** ✅ Built (`WearBridge.kt`, table above).
- **Phase 3 — package + sample.** ✅ `src/SwiftDotNet.Wear` (`SwiftDotNetWear.Install()`,
  `SwiftDotNetWearActivity`), `sample/SampleApp.Wear`. **Verified on the `Wear_OS_Large_Round` AVD (API 36,
  `android-wear-signed` arm64 image)**: list + header + chips, navigation push and system back, scroll
  position restored on back, rotary list scroll, rotary crown value (±1 per tick), toggle, keyed-list row
  taps, stepper, inline slider, vertical pager — each driven with `adb` and checked by screenshot.
- **Phase 4 — glanceables (open).** Wear **Tiles** (ProtoLayout) and watch-face **complications**. These are
  not Compose: they are template/`RemoteViews`-shaped surfaces rendered by another process, so they belong
  with [Live surfaces](live-activities-plan.md) / [Widgets](widgets-plan.md), not here. Decide whether one
  model covers Tiles and watchOS complications (WidgetKit accessory families) — the MauiNext plan's Phase-4
  gate.
- **Phase 5 — polish (open).** A vertical page indicator for the pager (Wear Material 2 only ships a
  horizontal one); `Picker` → Wear `Picker`; `Sheet`/`Alert` → Wear `Dialog`/`Confirmation`; swipe-to-dismiss
  as a gesture *animation* (system back already pops, but without the drag-follow visual); `TabView`
  selection binding; a physical-device run; Rider plugin awareness of Wear AVDs.

## Open questions

- **Phone + watch in one solution:** the Data Layer (`Xamarin.GooglePlayServices.Wearable`) for phone ↔ watch
  messaging. Out of scope for rendering; likely a companion service like MauiNext's `PhoneConnection`.
- **Round vs square:** everything was verified on a round face. Square Wear devices exist but are rare; the
  skin uses no round-only API, so it should degrade fine — unverified.
- **Physical crown feel:** calibration is per-unit; a Pixel Watch crown reports many small fractional
  events. The carry logic handles fractions, but "one step per detent" on real hardware is unverified.

## Risks

- **Wear Material 3 binding.** Google's current Wear design system is Material 3 Expressive; we render
  Material 2 components until `Xamarin.AndroidX.Wear.Compose.Material3` exists.
- **Rotary focus contention** between a `.DigitalCrownRotation` view and a scrolling list on the same screen.
