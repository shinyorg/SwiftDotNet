# Foldables & iPhone Duo — posture channel + `TwoPaneView`

**Status — 2026-10-25: implemented, all six phases.** What shipped, and where it was verified, is in the
docs — [Window posture](../docs/modifiers-gestures-animation.md#window-posture-foldables--iphone-duo) and
[`TwoPaneView`](../docs/views-and-controls.md#twopaneview). ✅ Unit + Skia tests (`WindowPostureTests`, 26);
✅ Android Compose and Skia MAUI hosts on a Pixel Fold AVD (book, tabletop, folded); ✅ iPhone Duo simulator
(iOS 27.1) *closed* — root size, hinge updates, side bars. ❓ The Duo *opened* is unverified (its hinge is driven
only from DeviceHub's GUI); 🧩 the Web reporter is built but unrun.

**Where the build departed from this design** (the body below is the original, kept as the record):

- **`PostureInfo` also carries the root size** (`WindowWidth`/`WindowHeight`, two more wire fields). Core had no
  window size at all, and `TwoPaneView`'s width rule needs one; the posture hosts were the natural reporters.
  Where nothing reports, `TwoPaneView` assumes a wide window (two panes).
- **Coordinates are root-relative**, not window-relative: the safe area on iOS, the window on Android
  (edge-to-edge). That's the space a root `TwoPaneView` lays out in.
- **`WindowPosture.Current` is readable everywhere** (no CA1416 annotation); `IsSupported` is informational
  and includes the browser.
- **The composite is a `Grid`** (fixed tracks at the fold, star tracks for ratios), not `HStack` + frames —
  star tracks fill the proposed size on every backend without C# knowing it. The fold beats `MinWideWidth`.
- **Native containers go through a new optional `IBridgeCapabilities`**, not the `customRenderers` seam —
  `TwoPaneView` emits a native node only where the bridge draws it, and only for side-by-side layouts.
- **Phase 3's risk resolved differently:** a non-scene app doesn't launch at all when built with the iOS 27
  SDK, so the sample's iOS head moved to the scene manifest + `SwiftDotNetSceneDelegate` (required, not optional).
- **Compose moved to 1.12.1** (BOM 2026.09.00) because the `Xamarin.AndroidX.Compose.Material3.Adaptive.Layout`
  1.3.0 binding requires it; no NU1107/NU1605 across the phone, Wear, Car and Skia MAUI heads.
- **The Skia MAUI host uses `objc_msgSend` for the iOS hinge API**: the MAUI workload compiles `net10.0-ios`
  against the iOS 26 reference pack, which lacks the 27.1 bindings.
- **Open decision 3 (side bars): confirmed** on the Duo simulator's outer display. Decisions 1, 2, 4 resolved as
  proposed (`WindowPosture`, one posture per process, angle kept for effects only).

## What we're targeting

| | Android foldables | iPhone Duo (iOS 27.1+, shipped 2026-10-23) |
|---|---|---|
| Fold geometry | Jetpack WindowManager `FoldingFeature.bounds` (window px) | SwiftUI `GeometryProxy.reservedRegions(kind: .division)` → `ReservedRegion.frame` / `.margins` — **present only while partly open** |
| State | `FoldingFeature.state`: `FLAT` / `HALF_OPENED`; `orientation`; `isSeparating` | `DeviceHinge.status`: `.closed` / `.partiallyOpen` / `.fullyOpen`, via `.onHingeChange { old, new in }` |
| Hinge angle | none | `DeviceHinge.angle: Angle` — Apple: for live effects, **not** layout |
| Camera cut-outs | not reported | `reservedRegions(kind: .occlusion)` |
| Native two-pane | material3-adaptive 1.3.0 `ListDetailPaneScaffold` / `SupportingPaneScaffold` | `ArrangementView(primary:secondary:)` + `.arrangementViewStyle(.split / .overlay)` |
| Test rig | Pixel Fold / resizable AVD; `adb emu posture 2` (tabletop), `adb emu fold` | `iPhone-Duo` simulator device type (iPhone19,4), iOS 27.1 runtime |

The SwiftUI symbols were verified in `SwiftUICore.swiftmodule/arm64e-apple-ios.swiftinterface` from the
local Xcode 27.1 SDK. All are `@available(anyAppleOS 27.1, *)`.

**There is no spanning mode on iPhone Duo.** Apple's guidance is compact width on the outer display and
regular on the inner. Android behaves the same when fully flat (`isSeparating == false`). So the
framework only needs to describe a fold **while it divides the window**; flat and closed both read as
"no fold". The shape is shared with Shiny.Core's proposed `ScreenPosture`
(`~/Desktop/dev/shiny/plans/screen-posture.md`). The record is duplicated rather than referenced,
because SwiftDotNet doesn't depend on Shiny.

## Design principles

1. **Copy the safe-area channel exactly.** [Safe area](safe-area-insets-plan.md) already solved
   native→C# device values: a `$`-prefixed reserved event id, a static `Current`, de-dupe in `Update`,
   `SwiftApp.RequestRender()`. A second channel built the same way costs nothing to review.
2. **Layout from width first, fold second.** `TwoPaneView` collapses to one pane at compact width,
   whatever the posture. The fold only places the split.
3. **Composite before native.** The first `TwoPaneView` is a C# `View` whose `Body` is an
   `HStack`/`VStack`. That means zero renderer changes, and all 15 backends work on day one. Native
   `ArrangementView` / `ListDetailPaneScaffold` come later through `customRenderers`, not a fork.
4. **Honest degradation.** Every backend without a fold reports `WindowPosture.None`, and `TwoPaneView`
   still splits by width there. Document this; don't fake it.

## Phase 1 — Core: `WindowPosture`

New `src/SwiftDotNet/Core/WindowPosture.cs`, shaped like
[`SafeArea.cs`](../src/SwiftDotNet/Core/SafeArea.cs):

```csharp
public enum FoldState { None, Flat, HalfOpen }
public enum FoldOrientation { None, Vertical, Horizontal }   // book / tabletop

public readonly record struct PostureInfo(
    FoldState State,
    FoldOrientation Orientation,
    double FoldX, double FoldY, double FoldWidth, double FoldHeight,   // window pts/dp; 0×0 when no fold
    double? HingeAngleDegrees)
{
    public static PostureInfo None => default;
    public bool HasFold => State == FoldState.HalfOpen;
    public bool IsTabletop => HasFold && Orientation == FoldOrientation.Horizontal;
    public bool IsBook => HasFold && Orientation == FoldOrientation.Vertical;
}

public static class WindowPosture
{
    internal const string EventId = "$posture";
    [SupportedOSPlatformGuard("ios")] [SupportedOSPlatformGuard("android")]
    [UnsupportedOSPlatformGuard("maccatalyst")]
    public static bool IsSupported { get; }          // same expression as SafeArea.IsSupported
    public static PostureInfo Current { get; }
    internal static void Update(string? payload);    // "state;orientation;x;y;w;h;angle", invariant culture, de-duped
    public static void Report(PostureInfo value);    // for Skia/Unity/test hosts, like SafeArea's Unity path
}
```

- Add `WindowPosture.EventId` → `WindowPosture.Update(value)` in `SwiftApp.OnEvent`
  ([`SwiftApp.cs:156`](../src/SwiftDotNet/Core/SwiftApp.cs)), next to the `$safeArea` branch.
- Use a flat struct, not a `Rect` type. The wire stays `;`-separated doubles, like safe area, and
  there's no new geometry type in Core.
- No occlusion (camera) regions in v1. They're iOS-only, and `SafeAreaPadding` already keeps content
  off the outer camera.
- Tests: `tests/SwiftDotNet.Tests/WindowPostureTests.cs`, mirroring `SafeAreaTests.cs` — parse,
  malformed payload keeps the last good value, de-dupe doesn't render, and a changed value renders once.

**Not** an `EnvironmentValues` property. The environment cascade is app-set and top-down
([`EnvironmentValues.cs:18-57`](../src/SwiftDotNet/Core/EnvironmentValues.cs)), and nothing in it comes
from the host. A static follows the precedent. Previews and tests use `Report()`.

## Phase 2 — Android: Kotlin reporter

In [`Bridge.kt`](../native/SwiftDotNetComposeBridge/src/main/kotlin/com/swiftdotnet/bridge/Bridge.kt):

- `ReportPosture()` as a sibling of `ReportSafeAreaInsets()` (L949), called from `RootHostView`
  (L929-939).
- Read `WindowInfoTracker.getOrCreate(context).windowLayoutInfo(context)` as a Flow inside a
  `LaunchedEffect`, with `context = LocalContext.current` (an `Activity`, because the host is
  `SwiftDotNetActivity`). Take the first `FoldingFeature`, convert bounds from px to dp with
  `LocalDensity`, and convert to window coordinates with `onGloballyPositioned` on the root if the
  host isn't full-window. Emit through `emit("$posture", payload)` only when the payload changes.
- Kotlin uses the **Flow** API directly, so the deprecated Java callback adapter doesn't matter here.
  That's a real advantage over the MAUI path.

Build changes:

- `build.gradle.kts`: add `androidx.window:window:1.5.1`.
- **The C# side must supply the runtime jar.** Add `Xamarin.AndroidX.Window` 1.5.1.3 to the Android
  item group of [`SwiftDotNet.csproj`](../src/SwiftDotNet/SwiftDotNet.csproj) (L89-95). Check NU1107
  against the existing Compose pins (L84-88) first: Window pulls KotlinX Coroutines 1.11.
- **Config changes.** `sample/SampleApp/Platforms/Android/MainActivity.cs:8` declares no
  `ConfigurationChanges`, so folding restarts the Activity and the render tree with it, and state is
  lost. Add `ScreenSize | SmallestScreenSize | ScreenLayout | Orientation` there, as
  `SampleApp.Skia.Maui`'s activity already does. Document it as a requirement in `docs/getting-started.md`,
  or set it on `SwiftDotNetActivity` if an attribute on the base class carries through. Check that it
  does.

## Phase 3 — iOS: Swift reporter

In [`Bridge.swift`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift):

- `PostureReporter`, a sibling of `SafeAreaReporter` (L1605-1643). It's a zero-size overlay on
  `RootHostView` (L1578-1598), so it adds nothing to layout.
- Inside `if #available(iOS 27.1, *)`:
  - `.onHingeChange { _, ctx in … }` for status and angle.
  - A `GeometryReader` (or `onGeometryChange`) calling
    `proxy.reservedRegions(kind: .division)` for the frame, in the root's coordinate space, which is
    the window for a full-screen host.
  - Emit through `emitEvent(POSTURE_EVENT_ID, payload)` with the same de-dupe.
- Map the state:
  - `.partiallyOpen` with an active division region → `HalfOpen`.
  - `.fullyOpen` → `Flat`, with no fold.
  - `.closed` → `None`.
- Below 27.1, or on macOS/tvOS/watchOS: report nothing. C# stays at `None`.
- **The shim's deployment target stays iOS 17** (L12-14); everything goes behind `#available`. The
  xcframework must now be built with **Xcode 27.1+** or the symbols won't resolve. Note this in
  `build-xcframework.sh` and in `docs/getting-started.md`.
- Hosting is `UIHostingController(rootView: RootHostView())` from a non-scene `AppDelegate`
  ([`SwiftDotNetAppDelegate.cs:38`](../src/SwiftDotNet/Platforms/iOS/SwiftDotNetAppDelegate.cs)).
  **Verify hinge updates arrive without a scene manifest.** `UIHingeInteraction.Update.hinge` is nil
  "when the hierarchy doesn't provide hinge updates", and a sceneless window is the prime suspect. If
  they don't arrive, the fix is switching the iOS head to `SwiftDotNetSceneDelegate`
  ([`SwiftDotNetSceneDelegate.cs:26-39`](../src/SwiftDotNet/Platforms/iOS/SwiftDotNetSceneDelegate.cs)),
  which is used only for CarPlay today.

## Phase 4 — `TwoPaneView` (composite)

```csharp
new TwoPaneView(
    primary:   new List(...),
    secondary: new DetailView(selected))
  .MinWideWidth(600)                 // below this: one pane (secondary when ShowSecondary, else primary)
  .PanePriority(Pane.Primary)        // which pane shows when collapsed
  .TallModeConfiguration(TallMode.TopBottom)
```

`Body` decides, in order:

1. **Book fold crossing the view** → `HStack(primary.Frame(width: foldX - x), Spacer(foldWidth),
   secondary)`. The split is on the fold; nothing sits under the hinge.
2. **Tabletop fold** → `VStack(primary above the fold, gap, secondary below)`.
3. **Width ≥ `MinWideWidth`** → `HStack` with a ratio (default 1:1; `.SplitRatio(0.4)`).
4. Otherwise → one pane.

Needs:

- **The view's own frame in window coordinates**, to intersect with the fold. Core doesn't have that.
  Options:
  - (a) Assume `TwoPaneView` fills the window — true for the common root use. Document it as a v1
    constraint.
  - (b) A geometry-reader node — a bigger, separate feature.
  - **Take (a)** for v1.
- File: `src/SwiftDotNet/Core/Views/TwoPaneView.cs`. No wire, `TreeDiffer` or renderer changes.
- Docs: a section in [`docs/views-and-controls.md`](../docs/views-and-controls.md), with a per-backend
  table: fold-aware on iOS 27.1+/Android, width-only everywhere else.

## Phase 5 — native two-pane (later, optional)

Swap the composite for the platforms' own containers through the existing custom-renderer seam
(Swift `customRenderers` fallback at L893; Kotlin `customRenderers` at L1077):

- iOS 27.1+: `ArrangementView(primary:secondary:).arrangementViewStyle(.split)`. Apple adjusts it near
  the fold automatically and handles side bars. Below 27.1, keep the composite.
- Android: `ListDetailPaneScaffold` from `androidx.compose.material3.adaptive:adaptive-layout:1.3.0`.
  This needs a newer Compose BOM than the pinned `2025.01.00`, which is the real cost, and a matching
  `Xamarin.AndroidX.Compose.Material3.Adaptive.*` binding (check that one exists).
- Only worth doing if the composite looks wrong in practice (animation, predictive back, Apple's
  side-bar migration).

## Phase 6 — the other backends

| Backend | Behaviour |
|---|---|
| Web (Blazor WASM) | Could report from `navigator.devicePosture` + `window.viewport.segments` (Chromium/Android foldables only). Cheap; do it with Phase 2 if wanted |
| Skia hosts (MAUI, Android, iOS) | The host knows the native window; it can call `WindowPosture.Report()` from the platform API |
| GTK, WinUI, WPF, TUI, Godot, Unity, MonoGame, Wayland | `None`; `TwoPaneView` splits by width only |
| watchOS, tvOS, macOS | `None` |

## Verification plan

The status tables must stay honest, so nothing is ✅ until it has been run here.

1. **Unit:** `WindowPostureTests`, plus `TwoPaneView` body selection driven through `Report()`.
2. **Android emulator (Pixel Fold AVD):** `adb emu posture 2` → tabletop VStack; book posture →
   HStack split on the fold; `adb emu fold` → one pane on the outer screen.
3. **iPhone Duo simulator:** first find out how the simulator changes pose (unverified; Apple
   mentions Xcode's Device Hub). Then the same three cases. Also confirm hinge updates on the
   non-scene host (Phase 3 risk).
4. **Sample:** a "Foldables" tab in [`sample/SharedUI/ContentView.cs`](../sample/SharedUI/ContentView.cs)
   showing `WindowPosture.Current` live and a `TwoPaneView`.

## Open decisions

1. **Name:** `WindowPosture` / `PostureInfo` vs Shiny's `ScreenPosture`. Matching Shiny helps people
   who use both, but SwiftDotNet's convention is SwiftUI-analog names, and SwiftUI has none here
   (`DeviceHinge` is the closest). Proposed: `WindowPosture`, because it's per window, not per screen,
   on both platforms.
2. **Per-window posture** waits on [Windows / Scenes](windows-plan.md) Step 0 (de-singletoning
   `SwiftApp`). v1 is one posture per process.
3. **Side ("vertical") bars on Duo:** iOS moves toolbars and tab bars to the side automatically for apps
   built with the 27.1 SDK. Our `TabView` / toolbar are real SwiftUI, so we should get this for free
   once the shim is built with Xcode 27.1 — verify, don't assume, and document the result in
   `docs/backends/`.
4. **Expose the hinge angle at all?** Keeping it invites misuse for layout; dropping it loses
   lid-angle effects. Proposed: keep it, documented as "effects only".
