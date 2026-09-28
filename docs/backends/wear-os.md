# Wear OS (Compose for Wear OS)

Wear OS is Android, so a SwiftDotNet watch app runs on the **ordinary [Android backend](android.md)** — the
same Kotlin/Compose interpreter, the same `IBridge`, the same patch protocol. The
[`SwiftDotNet.Wear`](../../src/SwiftDotNet.Wear) companion adds a **Wear skin**: the handful of node types
whose phone rendering is the wrong shape on a round 1.3" screen are re-mapped to their Compose-for-Wear
equivalents, and the root is wrapped in a Wear `Scaffold` with `TimeText`. Your views don't change.

- **Verified** on the `Wear_OS_Large_Round` AVD (API 36, `android-wear-signed` arm64 image) with
  [`sample/SampleApp.Wear`](../../sample/SampleApp.Wear) — every row of the mapping table below was driven with
  `adb` and checked by screenshot.
- The SwiftUI analog is a watchOS app: the same `List`/`NavigationStack`/`Toggle` code, rendered in the
  platform's watch idiom.

## How to use it

Reference `SwiftDotNet.Wear`, subclass `SwiftDotNetWearActivity` instead of `SwiftDotNetActivity`, and mark
the app as a watch app in `AndroidManifest.xml`:

```csharp
[Activity(Label = "My Watch App", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar")]
public sealed class MainActivity : SwiftDotNetWearActivity
{
    protected override SwiftDotNetApp CreateSwiftApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(_ => new WatchRootView());
        return builder.Build();
    }
}
```

```xml
<uses-feature android:name="android.hardware.type.watch" />
<application …>
    <meta-data android:name="com.google.android.wearable.standalone" android:value="true" />
</application>
```

The views are plain SwiftDotNet — from [`WatchRootView`](../../sample/SampleApp.Wear/WatchRootView.cs):

```csharp
public override View Body => new NavigationStack(
    new List(
        new NavigationLink(new Label("Counter", "hand.tap"), CounterPage),
        new NavigationLink(new Label("Crown", "gauge"), CrownPage),
        new Toggle("Metric units", _metric),
        new Section("About", new Text($"Count {_count.Value}").ForegroundColor(Color.Secondary))
    ).NavigationTitle("SwiftDotNet"));

View CrownPage => new VStack(
        new Gauge(_crown.Value, 0, 100, $"{_crown.Value:0}"),
        new Text("Turn the crown").Font(Font.Caption))
    .DigitalCrownRotation(_crown.Value, v => _crown.Value = v, from: 0, through: 100, by: 1)
    .NavigationTitle("Crown");
```

Hosting with `SwiftDotNetHost.CreateRootView` directly? Call `SwiftDotNetWear.Install()` first.

## The Wear mapping

| Node | Phone (Compose / M3) | Wear OS | Verified |
|---|---|---|---|
| `List`, `Form`, vertical `ScrollView` | `Card` / scrolling `Column` | `ScalingLazyColumn` + `PositionIndicator`, rotary scrolls it | ✅ |
| `Section` inside a list | inset-grouped card | `ListHeader` + its rows, flattened into the column | ✅ |
| `.NavigationTitle` on a list screen | `TopAppBar` title | the list's first item, a `ListHeader` | ✅ |
| `.NavigationTitle` on a non-list screen | `TopAppBar` title | a caption above the content | ✅ |
| `Button` as a list row | M3 `Button` | full-width `Chip` | ✅ |
| `Button` with a ≤ 2-character title (`+`, `−`) | M3 `Button` | round Wear `Button` | ✅ |
| `Toggle` | label + `Switch` | `ToggleChip` + Wear `Switch` | ✅ |
| `NavigationStack` / `NavigationLink` | `TopAppBar` + back arrow | own back stack, `Chip` rows; **system back** (swipe right / back button) pops; scroll position restored on back | ✅ |
| `TabView` (any style) | bottom bar / horizontal pager | `VerticalPager` — watchOS 10 and Wear page vertically | ✅ |
| `Slider` | M3 `Slider` | `InlineSlider` (10 steps across the range) | ✅ |
| `Stepper` | label + two outlined buttons in a row | caption over round −/+ buttons | ✅ |
| `Gauge` | linear bar | `CircularProgressIndicator` ring with the label inside | ✅ |
| `.DigitalCrownRotation(…)` | — | rotary input drives the value, one step per encoder unit | ✅ |
| Everything else | — | the phone interpreter, in a dark Material 3 theme | text / stacks ✅; others unexercised on a watch |

**Fallbacks to know about:** `Picker`, `DatePicker`, `Menu` (dropdown menus), `Sheet` (a bottom sheet) and
`Alert` still render with their phone Material 3 components — usable, but not the Wear idiom. SF Symbol
names become emoji glyphs, exactly as on phones (a name missing from the map shows `•`). Swipe-to-dismiss
pops via **system back**, but without the drag-follow animation.

## Rotary input — `.DigitalCrownRotation`

The Core modifier `.DigitalCrownRotation(value, onChange, from, through, by)` (see
[Modifiers](../modifiers-gestures-animation.md#rotary-input-digital-crown)) is interpreted by the **main**
Compose bridge through `Modifier.onRotaryScrollEvent` — plain `androidx.compose.ui`, no Wear dependency — so
it works on any Android device with a rotary encoder, not only under the Wear skin.

- **One encoder unit = one `by` step.** Rotary arrives as scroll pixels (axis value ×
  `ViewConfiguration.scaledVerticalScrollFactor`); the factor is divided back out. Fractional units from a
  high-resolution crown are carried until a whole step builds up. With `by: 0` the value is continuous and
  48 units sweep the whole range.
- **Clockwise increases**, matching watchOS.
- **Focus:** rotary events go to the focused node, so the modified view requests focus when it first
  composes. A `ScalingLazyColumn` also takes rotary focus to scroll — on one screen, the last one composed
  wins. Put the crown on a non-list screen (as the sample does).
- **Emulator:** the AVD's *Extended controls → Rotary input*, or
  `adb shell input rotaryencoder scroll --axis SCROLL,-1` (one clockwise unit).

## Building & running

```bash
# 1. Both .aars come out of one Gradle run (the Wear skin is the `wear` module)
native/SwiftDotNetComposeBridge/gradlew -p native/SwiftDotNetComposeBridge assembleRelease
cp native/SwiftDotNetComposeBridge/build/outputs/aar/SwiftDotNetComposeBridge-release.aar build/SwiftDotNetComposeBridge.aar
cp native/SwiftDotNetComposeBridge/wear/build/outputs/aar/wear-release.aar build/SwiftDotNetWearBridge.aar
# 2. Boot a Wear AVD, then build + install
emulator -avd Wear_OS_Large_Round &
dotnet build sample/SampleApp.Wear -t:Install -p:AdbTarget="-s emulator-5554"
```

Toolchain: as [Android](android.md), plus `androidx.wear.compose:compose-{foundation,material}:1.6.2` in the
`wear` module and `Xamarin.AndroidX.Wear.Compose.{Foundation,Material}` 1.6.2.1 in `SwiftDotNet.Wear`.
minSdk is **30** (Wear OS 3) for the skin and the sample; the phone backend stays at 24.

## How it's built

- **A second Gradle module, a second package.** `native/SwiftDotNetComposeBridge/wear` compiles against the
  main bridge (`compileOnly(project(":"))`, never bundled) and ships as `build/SwiftDotNetWearBridge.aar`,
  bound by [`SwiftDotNet.Wear.csproj`](../../src/SwiftDotNet.Wear/SwiftDotNet.Wear.csproj). A phone app never
  carries the Wear graph. Rationale: [plan, Decision 1](../../plans/wear-os-plan.md#decision-1--a-separate-aar--package-not-a-runtime-wear-mode-in-the-main-bridge).
- **A form-factor seam in the interpreter.** `Bridge.kt` exposes `overrideRenderer(type, …)` (consulted before
  the built-in mapping), `setRootContainer(…)`, `RenderNode(node)` and `LocalRootAlignment`. The Wear module's
  [`WearBridge.kt`](../../native/SwiftDotNetComposeBridge/wear/src/main/kotlin/com/swiftdotnet/wear/WearBridge.kt)
  registers its overrides through them when C# calls `SwiftDotNetWear.Install()`. Unlike
  [`registerRenderer`](../custom-controls.md), which only fills in *unknown* custom types, an override
  replaces a built-in one. The hooks are `@JvmSynthetic`, so the .NET binding never sees them.

## Gotchas

- **Rebuild *and restage* both `.aar`s together.** `gradlew :wear:assembleRelease` compiles the main module's
  classes but does **not** refresh its `.aar`; copying a stale main `.aar` next to a fresh Wear one fails at
  launch with `NoSuchMethodError: …BridgeKt.getLocalRootAlignment()`. Run the root `assembleRelease`, copy
  both, and clean `obj`/`bin` as the [Android gotcha](android.md#gotchas) describes.
- **Pinned to the phone's Compose line.** `Xamarin.AndroidX.Wear.Compose.* 1.6.2.1` requires Compose
  `1.11.3.1` and `Activity.Compose 1.13.0.1` — what [`SwiftDotNet.csproj`](../../src/SwiftDotNet/SwiftDotNet.csproj)
  already pins. Bump them together or hit NU1107.
- **No Wear Material 3 binding exists yet**, so the skin draws Wear Material (2) components.
- **Per-screen state.** Every navigation screen renders from one call site, so the stack wraps each screen in a
  `SaveableStateHolder` keyed by the screen's node id. Without it a pushed list inherited the previous list's
  scroll position (seen, then fixed, on the emulator).
- **`Button` name collision** with `Android.Widget.Button` in the app project — alias it, as on phones.
- **Rider plugin:** a Wear head is `SwiftDotNetPlatform=android`; device pickers don't yet distinguish Wear AVDs.

## Status

| Piece | Status |
|---|---|
| Wear skin (table above), rotary modifier, `SwiftDotNet.Wear`, sample | ✅ Verified on the Wear OS emulator (API 36 round AVD) |
| Physical watch | ⏳ Not run — calibration of a real crown's detents unverified |
| Square Wear devices | ⏳ Not run — no round-only API is used |
| `Picker` / `Sheet` / `Alert` in the Wear idiom, vertical page indicator, drag-follow swipe-to-dismiss | 📋 Planned — [plan Phase 5](../../plans/wear-os-plan.md#phases) |
| Tiles & complications | 📋 Planned — a glanceables decision shared with watchOS; see [plan Phase 4](../../plans/wear-os-plan.md#phases) and [Live Surfaces](../live-surfaces.md) |

See also: [Android](android.md) · [Modifiers → rotary input](../modifiers-gestures-animation.md#rotary-input-digital-crown) ·
[plan](../../plans/wear-os-plan.md).
