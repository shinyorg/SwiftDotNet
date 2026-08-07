# Apple — iOS / macOS / tvOS (SwiftUI)

The three Apple platforms share **one Swift interpreter** ([`native/SwiftDotNetBridge/Sources/.../Bridge.swift`](../../native/SwiftDotNetBridge/Sources))
that reconstructs *real* SwiftUI from the C# node tree. This is a **native-shim** backend (SwiftUI is a
compiler-plugin framework — you can't author a SwiftUI `View` from C#).

- **Verified:** iOS (iPhone Air / iOS 26.5 simulator), macOS (desktop `NSWindow` + `NSHostingController`),
  tvOS (Apple TV 4K simulator).
- **Route:** C# owns the tree; a Swift shim reconstructs SwiftUI. Native fidelity, no layout engine to
  reimplement.

## The bridge

C# talks to the xcframework over a C ABI (`@_cdecl` entry points, P/Invoke via `DllImport("__Internal")`):

| Symbol | Role |
|--------|------|
| `swiftdotnet_render(json)` | Apply a patch (`replace` / `updateProps` / `setChildren`) to the observed `@Observable` `VNode` tree. |
| `swiftdotnet_set_event_callback(fn)` | Swift calls it on events with a node id + optional value. |
| `swiftdotnet_make_host_controller()` | Returns a `UIHostingController` (iOS/tvOS) / `NSHostingController` (macOS) C# hosts as root. |

Two-way controls (`TextField`, `Toggle`) are SwiftUI **controlled components** whose local `@State` syncs both
directions via `onChange`. `@Observable` requires **iOS 17+ / macOS 14+ / tvOS 17+**.

## One interpreter, a few `#if`s

- **macOS** swaps UIKit → AppKit hosting via `#if canImport(UIKit)` / `#elseif canImport(AppKit)`:
  `UIHostingController` → `NSHostingController`, `UIColor` → `NSColor`. Paged `TabView` is guarded off
  (`PageTabViewStyle` is iOS-only).
- **tvOS** is focus-driven with no pointer, so Apple marks several SwiftUI controls unavailable there. The
  bridge adds `#if os(tvOS)` fallbacks (compile errors otherwise):

  | Control | tvOS fallback |
  |---------|---------------|
  | `Slider` | `Text(value)` |
  | `Stepper` | focusable −/+ Buttons that emit value±1 (stays functional) |
  | `DatePicker` | `Text(formatted date)` |
  | `ColorPicker` | `Text` + `RoundedRectangle` swatch |
  | `DisclosureGroup` | header Button + conditional children |
  | `Gauge` | `VStack { label + ProgressView }` |
  | `TextEditor` | `TextField` |
  | paged `TabView` | standard `TabView` |

## Grid and AbsoluteLayout are custom `Layout`s

Neither container maps onto anything SwiftUI ships. `LazyVGrid` sizes columns but has no concept of a cell
span or an explicit cell; SwiftUI's own `Grid` has `gridCellColumns` but no row span and no per-column
sizing; and there is no absolute-positioning container at all. So the bridge implements both against the
`Layout` protocol — `SDNGridLayout` and `SDNAbsoluteLayout` — which is available from iOS 16 / macOS 13 /
tvOS 16, all below this bridge's deployment targets (17.0 / 14.0 / 17.0).

Placement is resolved **before** the layout runs, in `gridView`, where the whole child list is in hand; each
subview then carries its cell as a `LayoutValueKey`. The track-sizing algorithm is a line-for-line port of
`SkiaNode.ResolveTracks`, so the same C# lays out identically on Skia and SwiftUI. `List.Columns(n)` is
unaffected — it still lowers to a virtualizing `LazyVGrid`. See [Grid](../views-and-controls.md#grid).

## Building the xcframework

```bash
native/SwiftDotNetBridge/build-xcframework.sh
```

Produces `build/SwiftDotNetBridge.xcframework` with **5 slices**: `ios-arm64`, `ios-arm64-simulator`,
`tvos-arm64`, `tvos-arm64-simulator`, `macos-arm64`. macOS is assembled as a *versioned* framework
(`Versions/A/…` with symlinks), unlike the flat iOS/tvOS layout. Min iOS/tvOS 17, min macOS 14 (for
`@Observable`).

## The library & samples

- [`src/SwiftDotNet`](../../src/SwiftDotNet) is one multi-target library; `Platforms/{iOS,macOS,tvOS}/` hold
  `IosBridge` / `MacBridge` / `TvBridge` (all near-identical: `__Internal` P/Invoke, host-controller pointer →
  `UIViewController`/`NSViewController`) and `SwiftDotNetHost`.
- The Apple `NativeReference` is declared in
  [`src/SwiftDotNet/SwiftDotNetBridge.targets`](../../src/SwiftDotNet/SwiftDotNetBridge.targets) (gated to
  ios/macos/tvos).
- Reusable host bases: `SwiftDotNetAppDelegate` (iOS/tvOS `: UIApplicationDelegate`, macOS
  `: NSApplicationDelegate` — owns the NSWindow sizing fix).

See [Getting Started](../getting-started.md#ios-swiftui) for build/run commands.

## Gotchas

- **`NativeReference` doesn't flow transitively** — the app's `.csproj` must also
  `<Import Project="…/SwiftDotNetBridge.targets" />`, or the link fails with `Undefined symbols _swiftdotnet_*`.
- **`DllImport("__Internal")`**, not a leaf-name `dlopen` (which ignores `@rpath`).
- **macOS window sizing:** add the host view as a resizable subview filling the `NSWindow`; setting
  `ContentViewController` collapses the window to the SwiftUI intrinsic size (the 213×92 window bug).
- **iOS launch screen / letterbox:** the `Info.plist` needs `Link="Info.plist"` in the csproj `None` item so
  the SDK's `_DetectAppManifest` picks it up (else no `UILaunchScreen`, full-screen letterbox).
- **Safe area is iOS-only, not macOS/tvOS.** The `safeAreaPadding` / `ignoresSafeArea` cases in
  `applyModifiers` are inside `#if os(iOS)`; the other Apple slices fall through to `default: break`, and
  the C# API is annotated so those platforms can't call it. Insets are read from the **key window**
  (not a `GeometryReader`) by a zero-size `SafeAreaReporter` overlay, so reporting contributes nothing to
  layout — wrapping the root in a `GeometryReader` would have re-aligned every existing app. Keyboard
  height comes from `UIResponder.keyboardWillChangeFrame`/`WillHide`, which SwiftUI has no equivalent for.
  See [Safe area](../modifiers-gestures-animation.md#safe-area-ios--android-only).
- **Maps:** MapKit ships as a separate companion xcframework (`SwiftDotNetMaps`), registered via
  `AppleMaps.Register()` — it stays out of the core bridge. See [Maps](../maps.md).
- **Keyframe timelines use a real `KeyframeAnimator`**, so SwiftUI owns the clock (the deployment floor is
  already iOS 17 / macOS 14 / tvOS 17, which is exactly what `KeyframeAnimator` needs — no availability
  guard). Three things the implementation is deliberately shaped around, all in
  [`Bridge.swift`](../../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift):
  - **All eight property tracks are emitted unconditionally**, with untracked ones holding a constant. Eight
    *optional* tracks is 2⁸ type combinations in the `keyframes` result builder, which blows the Swift
    type-checker up so badly it can't even produce a diagnostic (`failed to produce diagnostic for
    expression`). If you add a property, keep it unconditional.
  - **`KFValues` hand-rolls `animatableData`** as a tree of `AnimatablePair`s. `KeyframeAnimator` requires
    `Animatable`, and the default `animatableData` only exists for `VectorArithmetic` types.
  - **`autoreverse` is a mirrored return leg**, not a flag — `KeyframeAnimator` only loops forwards. A
    finite repeat count above 1 loops forever, since `repeating:` is all-or-nothing.
  See [keyframe animations](../modifiers-gestures-animation.md#keyframe-animations).

## Hot reload

⚠️ **Not working from the CLI.** The iOS/tvOS SDK requires `UseInterpreter=true` (opt in with
`-p:SwiftDotNetHotReload=true`), and with it the app installs and launches but aborts during startup —
`Socket error while connecting to IDE on 127.0.0.1:10000: Connection refused` — because bare `dotnet watch`
does not stand up the IDE-side debug tunnel the interpreter build expects. Drive hot reload from Visual
Studio, VS Code, or Rider instead. Nothing in the Swift shim is involved either way: a reload is an
ordinary `replace` patch. See [Hot Reload](../hot-reload.md).
