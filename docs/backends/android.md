# Android (Jetpack Compose)

Android renders as **real Jetpack Compose**. Like SwiftUI, Compose is a compiler-plugin framework — you can't
author a `@Composable` from C# — so this is a **native-shim** backend: a Kotlin shim
([`native/SwiftDotNetComposeBridge`](../../native/SwiftDotNetComposeBridge), `Bridge.kt`) mirrors the Swift
bridge and interprets the node tree into Compose over JNI.

- **Verified** on the emulator (Pixel 9a AVD): the shared [`ContentView`](../../sample/SharedUI/ContentView.cs)
  renders identically to iOS.

## The Compose bridge

`Bridge.kt` interprets the **entire** control set — Text/Button/stacks/ZStack/ScrollView/Grid/List/Form/
Section/Group/DisclosureGroup/TabView(+paged carousel)/Tab/Menu, all inputs, NavigationStack/Link, Sheet,
Alert, Image/Label, ProgressView/Gauge, Link, shapes — plus the modifier set.

- **Nav** = a lightweight `NavStack` via `CompositionLocal` + `Scaffold`/`TopAppBar`.
- **Sheet** = `ModalBottomSheet`; **Alert** = `AlertDialog`.
- **SF Symbols** → an emoji map (avoids a material-icons dependency).
- **Modifiers** apply via a `Modified()` wrapper (box modifiers + `CompositionLocalProvider` for font/color);
  shapes fill from the `foregroundColor` modifier.

## Building & wiring

```bash
# 1. Build the .aar (JDK 21 + Android SDK)
native/SwiftDotNetComposeBridge/gradlew -p native/SwiftDotNetComposeBridge assembleRelease
# 2. Build the app
dotnet build sample/SampleApp -f net10.0-android
```

Toolchain: Gradle wrapper 8.14.3, AGP 8.11.1, Kotlin 2.1.0, compileSdk 36, Compose BOM 2025.01.00.

`src/SwiftDotNet` (net10.0-android TFM) binds the `.aar` via `<AndroidLibrary … Bind="true">` and references
`Xamarin.AndroidX.Compose.*` (Ui/Foundation, Material3, Activity.Compose). `AndroidBridge : IBridge` calls
the bound `Com.Swiftdotnet.Bridge.SwiftDotNetBridge` static methods; events come back via a C#
`EventProxy : Java.Lang.Object, IEventCallback`.

The reusable host base is `SwiftDotNetActivity : ComponentActivity` (a `ComponentActivity` is needed so the
`ComposeView` gets its ViewTree owners).

## Gotchas

- **Rebind after rebuilding the `.aar`.** Copy the freshly-built AAR from
  `native/…/build/outputs/aar/` to `build/`, then `rm -rf` the `obj`/`bin` of both `src/SwiftDotNet` and
  `sample/SampleApp` — an incremental build reuses the stale binding (→ "unknown view" or FileNotFound).
- **Compose strong-skipping** (Kotlin 2.x default) compares composable params by *reference identity* —
  mutating a VNode in place is **skipped**. Fix: `VNode.props`/`children`/`type` must be `mutableStateOf`
  (the Compose analog of iOS `@Observable`).
- **`Button` name collision** with `Android.Widget.Button` → alias it.
- Composables are kept `private` so the .NET binding generator doesn't bind synthetic `Composer` params.
- Kotlin VNode props are `Any?` — cast with `as?` (Swift's are `PropValue`).
- Minor UX: the app currently shows both the Activity `ActionBar` and the Compose `TopAppBar` in nav
  (disable the ActionBar via theme). `ColorPicker` cycles a palette on tap (no native Android color picker).

### Modifier behaviour

- **`.Repeating()` animations** use `rememberInfiniteTransition` + `infiniteRepeatable` (for `-1`) or
  `repeatable` with `RepeatMode.Reverse`/`Restart`. Like Web and Skia, a repeating animation pulses
  **opacity** 1 → 0.4 — the wire carries no from/to pair, so every backend agrees on that one convention.
  Consequence: `BadgeView.Pulse` reads as an opacity pulse rather than a size pulse (its `.ScaleEffect(1.0)`
  is identity), and `SkeletonView`'s shimmer is a fading static gradient, not a travelling highlight. A
  `spring` curve combined with a repeat degrades to a tween — springs have no duration and can't feed
  `infiniteRepeatable`.
- **`Image.FromUrl`** loads dependency-free (coroutine + `URL.openConnection` + `BitmapFactory`, cached by
  URL in-process). No Coil dependency was added. **The bridge AAR's manifest declares
  `android.permission.INTERNET`**, which merges into every consuming app. No disk cache, no downsampling,
  no in-flight dedup — two nodes with the same URL fetch twice before the first caches.
- **`.Material` is a translucent tint, not a backdrop blur — and this is deliberate.** `Modifier.blur` /
  `RenderEffect.createBlurEffect` blur a composable's *own content*, whereas `.Material` is a SwiftUI
  *backdrop* blur. Using them would smear the node's children, which is worse than the tint. Real backdrop
  blur on Android needs `Window.setBackgroundBlurRadius`, which is window-level and can't be expressed
  per-node.

### `CameraView` and `Map` are not in the AAR

[`native/camera/CameraRenderer.kt`](../../native/camera/CameraRenderer.kt) and
[`native/maps/MapRenderer.kt`](../../native/maps/MapRenderer.kt) sit **outside**
`native/SwiftDotNetComposeBridge/src/main/kotlin/`, the only source root the AAR compiles, and
`build.gradle.kts` declares neither a `sourceSets` block nor the CameraX / ML Kit / MapLibre dependencies
their headers require. Both files are authored but **not shipped** — confirmed by their absence from
`classes.jar`. Pulling them in means adding those dependencies to the bridge AAR that every consumer would
then carry, which is a design decision, not a build fix.

## Maps

A Compose/MapLibre renderer is **authored** in [`native/maps`](../../native/maps) (`MapRenderer.kt`) against
the same native `registerRenderer` seam, but it is not compiled into the bridge AAR — see
[above](#cameraview-and-map-are-not-in-the-aar). See [Maps](../maps.md).

## Hot reload

🧩 **Expected, not run** (needs an emulator). `dotnet watch run -f net10.0-android` should work with no
extra setup — the Kotlin shim is not involved, since a reload arrives as an ordinary `replace` patch. See
[Hot Reload](../hot-reload.md).
