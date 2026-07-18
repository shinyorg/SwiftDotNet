# SwiftDotNet

**SwiftUI for .NET — everywhere.** Write declarative UI once in C# and render it as *real* native UI on
each platform: **SwiftUI** on iOS/macOS/tvOS, **Jetpack Compose** on Android, **GTK4** on Linux, **WinUI 3**
on Windows, and **HTML/DOM** on the Web. Not a reimplementation of each toolkit — the actual native controls,
with the platform's own layout, fonts, animations, and accessibility.

One `View` subclass, eight rendering backends:

| Platform | Renders as | Route | Status |
|----------|-----------|-------|--------|
| iOS | SwiftUI | Swift shim (xcframework, P/Invoke) | ✅ Verified on simulator |
| macOS | SwiftUI (AppKit-hosted) | Same Swift shim (`#if` UIKit→AppKit) | ✅ Verified on desktop |
| tvOS | SwiftUI | Same Swift shim (`#if os(tvOS)` fallbacks) | ✅ Verified on Apple TV sim |
| Android | Jetpack Compose | Kotlin shim (`.aar`, JNI) | ✅ Verified on emulator |
| Linux | GTK4 | Pure C# (Gir.Core, no shim) | ✅ Verified on desktop |
| Windows | WinUI 3 | Pure C# (no shim) | 🧩 Scaffolded (needs Windows to build) |
| Web | HTML/DOM | Pure C# (Blazor WASM, no shim) | ✅ Verified in Chrome |

Two backend routes: SwiftUI and Compose are **compiler-plugin frameworks**, so they need a thin native shim
(Swift/Kotlin) that reconstructs the tree; GTK, WinUI, and the Web are fully C#-bindable, so those backends are
**pure C#** with **no native code** — a retained-mode interpreter that maps the node tree straight to native
controls (or DOM elements) and applies the same diff patches.

```csharp
public sealed class ContentView : View
{
    readonly State<int> _count = State(0);      // mirrors @State private var count = 0

    public override View Body =>
        new VStack(
            new Text($"Count: {_count.Value}").Font(Font.LargeTitle),
            new Text("Tap the button to increment").Font(Font.Caption).ForegroundColor(Color.Secondary),
            new Button("Increment", () => _count.Value++)
        ).Spacing(24);
}
```

## Custom controls

Two ways to add your own control:

1. **Composite (the common case)** — subclass `View`, compose existing views in `Body`. Pure C#, no native
   code, renders on **every** backend automatically. Example: [`sample/SharedUI/Rating.cs`](sample/SharedUI/Rating.cs)
   is a ★/☆ rating built from `HStack` + `Button`.
2. **Custom native primitive** — for a control that isn't a composition (a native map, gauge, etc.): subclass
   `CustomView`, emit props under a `TypeName`, then register a per-backend renderer. On the pure-C# backends
   this needs **no interpreter fork**:
   ```csharp
   GtkRenderers.Register("NativeRating", ctx => {
       var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 5, 1);
       scale.SetValue(ctx.Number("value") ?? 0);
       scale.OnValueChanged += (_, _) => ctx.Emit(((int)scale.GetValue()).ToString());
       return scale;
   });
   ```
   `WinRenderers.Register` is the WinUI equivalent; SwiftUI/Compose expose `swiftDotNetRegisterRenderer` /
   `registerRenderer` for native (Swift/Kotlin) extensions. Unregistered types render a `⚠️` placeholder, not a crash.

## Views & controls

- **Layout**: `VStack`, `HStack`, `ZStack`, `ScrollView`, `Grid`, `List` (+ `List.ForEach`),
  `Form`, `Section`, `Group`, `Spacer`, `Divider`
- **Navigation & presentation**: `NavigationStack`, `NavigationLink`, `TabView` (+ `.Paged()` carousel),
  `Tab`, `Sheet`, `Alert`, `DisclosureGroup`, `Menu`
- **Inputs (two-way bound)**: `TextField`, `SecureField`, `TextEditor`, `Toggle`, `Slider`,
  `Stepper`, `Picker`, `DatePicker`, `ColorPicker`
- **Display**: `Text`, `Label`, `Image` (SF Symbols), `ProgressView`, `Gauge`, `Link`,
  and shapes `Rectangle` / `Circle` / `Capsule` / `RoundedRectangle`
- **Modifiers** (order-preserving): `.Font`, `.ForegroundColor`, `.Background`, `.Padding` (uniform or
  per-`Edge`), `.Frame` (+ alignment), `.CornerRadius`, `.Border`, `.Shadow` (+ color/offset), `.Opacity`,
  `.Align` (fill width + align), `.NavigationTitle`, `.OnTapGesture`
- **Alignment**: `VStack.Alignment(HorizontalAlignment)`, `HStack.Alignment(VerticalAlignment)`,
  `ZStack.Alignment(Alignment)`; colors also via `Color.Hex("#RRGGBB")`

## Architecture

C# owns the view tree (React-Native style); a thin Swift shim reconstructs SwiftUI. A **diff engine**
turns each re-render into a minimal patch so only changed nodes cross the bridge:

```
 C# DSL (View/State)
   │  ToNode() → TreeDiffer         ┌─────────────────────────────┐
   ▼                                │  SwiftDotNetBridge.xcframework│
 Patch ──JSON──► swiftdotnet_render ──► apply to @Observable VNode tree ──► NodeView → real SwiftUI
   ▲                                │                                   │ tap / edit / toggle
   │  State.Value = …  ◄── SwiftApp.OnEvent(id,value) ◄── [UnmanagedCallersOnly] ◄──┘ @convention(c)
   └── re-render ───────────────────┘                                     (node id + value payload)
```

- **`swiftdotnet_render(json)`** — C# pushes a **patch** (`replace` / `updateProps` / `setChildren`);
  Swift applies it to the observed `VNode` tree, so unchanged subtrees never rebuild.
- **`swiftdotnet_set_event_callback(fn)`** — Swift calls it on events with a node id + optional value
  (TextField text, Toggle `"true"`/`"false"`, null for a Button).
- **`swiftdotnet_make_host_controller()`** — returns a `UIHostingController` C# hosts as the root VC.

### Diff engine

Node ids are **structural paths** (`"0.2.1"`), stable across renders, so the differ targets nodes by id:
a prop change emits `updateProps` for just that node; a changed child list emits `setChildren` on the
parent; identical renders emit nothing. Two-way-bound controls (`TextField`, `Toggle`) are Swift
"controlled components" whose local `@State` syncs both directions via `onChange`.

## Projects

| Path | TFM | Role |
|------|-----|------|
| `src/SwiftDotNet` | **multi-target** | **One library.** `Core/` (platform-neutral DSL, `State<T>`, `Node`/JSON, diff engine, `IBridge`, `SwiftApp`) compiles for every TFM; `Platforms/{iOS,macOS,tvOS,Android,Windows}/` (the bridges + `SwiftDotNetHost`) are opted in per TFM. TFMs: `net10.0;net10.0-android` always, `net10.0-ios;-macos;-tvos` on a Mac, `net10.0-windows10.x` on Windows. iOS/macOS/tvOS pull the Swift xcframework (`SwiftDotNetBridge.targets`); Android binds the Compose `.aar` + `Xamarin.AndroidX.Compose.*`; Windows pulls WinUI 3. |
| `src/SwiftDotNet.Gtk` | `net10.0` | **Separate** (Linux/GTK shares the `net10.0` TFM with Core, so folding it in would force every neutral consumer to take the GTK dependency). Pure-C# GTK4 backend over Gir.Core; references the combined `SwiftDotNet`. |
| `src/SwiftDotNet.Web` | `net10.0` (Razor lib) | **Separate** (Blazor has no distinct TFM either). Pure-C# **Blazor WebAssembly** backend — `SwiftDotNetView` renders the node tree to HTML/CSS via `RenderTreeBuilder`; DOM events call back into C#. |
| `native/SwiftDotNetBridge` | Swift | `Bridge.swift` + build script → `build/SwiftDotNetBridge.xcframework` (SwiftUI interpreter; 5 slices — iOS device/sim, tvOS device/sim, macOS) |
| `native/SwiftDotNetComposeBridge` | Kotlin | `Bridge.kt` + Gradle → `build/SwiftDotNetComposeBridge.aar` (Jetpack Compose interpreter) |
| `sample/SharedUI` | `net10.0` | The demo `ContentView` (5-tab tour) + composite `Rating` control — one file, shared by all apps |
| `sample/SampleApp` | `net10.0-ios` | Thin iOS app: `SwiftDotNetHost.CreateRootController(new ContentView())` |
| `sample/SampleApp.Mac` | `net10.0-macos` | Thin macOS app: hosts the root controller in an `NSWindow` |
| `sample/SampleApp.tvOS` | `net10.0-tvos` | Thin Apple TV app: same host controller, focus-driven UI |
| `sample/SampleApp.Android` | `net10.0-android` | Thin Android app: `SwiftDotNetHost.CreateRootView(this, new ContentView())` |
| `sample/SampleApp.Windows` | `net10.0-windows` | Thin WinUI 3 app (unpackaged + self-contained) |
| `sample/SampleApp.Gtk` | `net10.0` | Thin GTK app: references `SwiftDotNet.Gtk` |
| `sample/SampleApp.Web` | `net10.0` (Blazor WASM) | Thin web app: hosts `<SwiftDotNetView Root="new ContentView()">` |

All ten projects are wired into **`SwiftDotNet.slnx`** at the repo root.

The **same `SharedUI.ContentView`** renders as **SwiftUI on iOS**, **SwiftUI (AppKit-hosted) on macOS**,
**SwiftUI on tvOS**, **Jetpack Compose on Android**, **GTK4 on Linux**, and **HTML/DOM on the Web** — verified
on device/emulator/desktop/Apple TV sim/browser.
The Apple platforms share one Swift interpreter with a few `#if` conditionals: macOS swaps UIKit→AppKit hosting;
tvOS (focus-driven, no pointer) falls back for the controls Apple omits there — `Slider`/`DatePicker`/`ColorPicker`
show a value/swatch, `Stepper`→focusable −/+ buttons, `DisclosureGroup`→a header button, `Gauge`→`ProgressView`.

**Linux/GTK** is different: GTK is a C/GObject library (not a compiler-plugin framework), so its backend is
**pure C#** with **no native shim** — a retained-mode interpreter mapping the node tree to real `Gtk.Widget`s
via Gir.Core, applying the same diff patches. It is at **full vocabulary parity**: the same `ContentView`
builds 325 GTK4 widgets (`Gtk.Entry`, `Gtk.Switch`, `Gtk.Scale`, `Gtk.SpinButton`, `Gtk.DropDown`,
`Gtk.Calendar`, `Gtk.ColorDialogButton`, `Gtk.ListBox`, `Gtk.Notebook`, `Gtk.Expander`, `Gtk.Grid`,
`Gtk.Overlay`, …), with modifiers via a GTK **CSS provider** (border/shadow/corner-radius/background/padding)
and alignment via `halign`/`valign`. Run needs GTK4 (`brew install gtk4` / `apt install libgtk-4-1`); on
non-Linux set `DYLD_FALLBACK_LIBRARY_PATH`/`LD_LIBRARY_PATH` to the GTK libs.

**Windows/WinUI** is the same pure-C# "translate to controls" route: `SwiftDotNet.Windows` maps the node
tree to real WinUI 3 controls (`TextBox`, `ToggleSwitch`, `Slider`, `NumberBox`, `ComboBox`, `TabView`,
`Expander`, `CalendarDatePicker`, `ColorPicker`, `ContentDialog`, `HyperlinkButton`, shapes, …). WinUI is
fully C#-bindable, so no native shim is needed — unlike SwiftUI/Compose. **This backend is scaffolded but
not yet compiled** (WinUI 3 / Windows App SDK require Windows to build); expect minor WinUI API fixes on the
first Windows build. Notably, `microsoft-ui-reactor` is a WinUI-only project with this same architecture —
a sibling, not a dependency; this backend keeps SwiftDotNet's own reconciler.

The sample app is **unpackaged + self-contained** (`WindowsPackageType=None`, `SelfContained=true`,
`WindowsAppSDKSelfContained=true`), so on a Windows machine it runs with no prerequisites beyond the .NET SDK:
```powershell
dotnet run --project sample/SampleApp.Windows
```

**One Core, two native backends.** The DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol, and `SwiftApp`
are shared verbatim; iOS renders them as **SwiftUI** (Swift shim, P/Invoke) and Android as **Jetpack Compose**
(Kotlin shim, JNI). SwiftUI's `@Observable` VNode ↔ Compose's `mutableStateOf` VNode; `@_cdecl` ↔ `@JvmStatic`;
function-pointer callback ↔ bound `EventCallback` interface.

> **Consuming the library:** reference `SwiftDotNet.iOS`, then add
> `<Import Project="…/SwiftDotNet.iOS/SwiftDotNetBridge.targets" />` to your app's `.csproj`.
> The import is required because `NativeReference` items don't flow transitively into the app's native link.

## Build & run

```bash
# 1. Build the Swift bridge (device + simulator slices, min iOS 17)
native/SwiftDotNetBridge/build-xcframework.sh

# 2. Build the sample app for the simulator
dotnet build sample/SampleApp/SampleApp.csproj -f net10.0-ios -r iossimulator-arm64

# 3. Install + launch
xcrun simctl install booted sample/SampleApp/bin/Debug/net10.0-ios/iossimulator-arm64/SampleApp.app
xcrun simctl launch booted com.swiftdotnet.sample
```

## Status

Verified on iPhone Air / iOS 26.5 (simulator) — a 5-tab demo (`ContentView`) exercising the whole set:
- **Inputs** tab: TextField, SecureField, Slider, Stepper, Picker, DatePicker, ColorPicker, Toggle.
- **Layout** tab: Grid of shapes (color/frame/opacity), Divider, ZStack, HStack + SF Symbol Images/Label,
  ProgressView, Gauge.
- **Carousel** tab: paged `TabView` with page dots.
- **Lists** tab: Form + Sections + List + DisclosureGroup + Menu.
- **Nav** tab: NavigationStack + NavigationLink (push verified), Link, Sheet, Alert.
- Interactions confirmed live: tab switching, navigation push, Menu action, Button/TextField/Toggle bindings.
- Diff engine + bool/text/value bindings verified deterministically via a Core test harness.

### Notes
- P/Invoke resolves the bridge via `DllImport("__Internal")` — the framework is a load-time
  dependency, so its `@_cdecl` symbols are in the global namespace (a leaf-name `dlopen` ignores `@rpath`).
- `@Observable` requires iOS 17+.
- JSON is hand-rolled (`NodeJson`) — zero reflection, trim/AOT-safe (no IL2026).

### Next steps
- Per-view local state ownership (child composite views keep `@State` across renders → needs view-instance reconciliation).
- More views/modifiers (ScrollView, Image, Picker, `.background`, `.cornerRadius`, gestures).
- Binary bridge protocol (replace JSON on the hot path); physical-device run.
- Keyed `ForEach` for animated list insert/remove/move.
