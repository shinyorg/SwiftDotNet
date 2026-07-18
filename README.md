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
  per-`Edge`), `.Frame` (+ alignment), `.CornerRadius`, `.Border`, `.Shadow` (+ color/offset), `.Opacity`
  (clamped 0–1), `.Disabled` (dim + block interaction), `.Align` (fill width + align), `.NavigationTitle`,
  `.OnTapGesture`. Modifiers are a **universal wrapper** applied to any view via a single generic pass per
  backend — so `.Opacity`/`.Disabled` work on every control, not a hand-picked subset.
- **Alignment**: `VStack.Alignment(HorizontalAlignment)`, `HStack.Alignment(VerticalAlignment)`,
  `ZStack.Alignment(Alignment)`; colors also via `Color.Hex("#RRGGBB")`

## Architecture

C# owns the view tree (React-Native style); each backend reconstructs native UI from it. A **diff engine**
turns every re-render into a minimal patch so only changed nodes reach the renderer. The diagram below shows
the iOS/SwiftUI path — the bridge is a native shim there and on Android, and an in-process interpreter on the
pure-C# backends (GTK/WinUI/Web), but the patch protocol and event round-trip are identical everywhere:

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
| `sample/SampleApp` | **multi-target** | **One sample app**, multi-targeted like the library: `net10.0-android` always, `+ios;-macos;-tvos` on a Mac, `+windows` on Windows. `Platforms/{iOS,macOS,tvOS,Android,Windows}/` hold the thin per-OS entry points; the root view is registered once in `AppRoot.cs`. |
| `sample/SampleApp.Gtk` | `net10.0` | Thin GTK app: references `SwiftDotNet.Gtk` (separate — no distinct TFM) |
| `sample/SampleApp.Web` | `net10.0` (Blazor WASM) | Thin web app: hosts `<SwiftDotNetView Root="new ContentView()">` (separate — no distinct TFM) |

All projects are wired into **`SwiftDotNet.slnx`** at the repo root.

### Centralized hosting & registration

The per-OS bootstrap lives **in the library** as reusable abstract hosts, so an app's platform entry point
is a one-liner that just names its root view:

| Base host (in `SwiftDotNet`) | Platform | Subclass in the app |
|------------------------------|----------|---------------------|
| `SwiftDotNetAppDelegate : UIApplicationDelegate` | iOS / tvOS | `[Register("AppDelegate")] class AppDelegate : SwiftDotNetAppDelegate` |
| `SwiftDotNetAppDelegate : NSApplicationDelegate` | macOS | same (creates + sizes the `NSWindow` for you) |
| `SwiftDotNetActivity : ComponentActivity` | Android | `[Activity(MainLauncher=true)] class MainActivity : SwiftDotNetActivity` |
| `SwiftDotNetApplication : Application` | Windows | `class App : SwiftDotNetApplication` |

Each override is just `protected override View CreateRoot() => AppRoot.Create();`, and `AppRoot.Create()`
(the single registration point) returns `new ContentView()`. So the window/host/activation wiring is written
once in the framework, and the sample declares its UI in exactly one place. (The bases are non-generic abstract
classes — a generic `NSObject`/`Java.Lang.Object` subclass can't be registered with the ObjC/Android runtimes.)

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
dotnet run --project sample/SampleApp -f net10.0-windows10.0.19041.0
```

**Web/Blazor** is the third pure-C# "translate to controls" backend, where the "control" is an HTML element.
`SwiftDotNet.Web` is a separate Razor class library: `SwiftDotNetView : ComponentBase` walks the node tree in
`BuildRenderTree`, emitting HTML/CSS through Blazor's `RenderTreeBuilder` — so **Blazor's own render-tree→DOM
diff is the write layer** (no manual DOM manipulation). VStack/HStack→flex `div`, `TextField`→`<input>`,
`Toggle`→`<input type=checkbox>`, `Slider`→range input, `Picker`→`<select>`, `DatePicker`/`ColorPicker`→native
inputs, shapes→styled `div`s, modifiers→inline CSS; DOM events (`onclick`, `onchange`) call back into C# via
`EventCallback`. It runs in **Blazor WebAssembly** — the whole framework and your C# UI execute in the browser:
```bash
dotnet run --project sample/SampleApp.Web    # → http://localhost:5000
```

**One Core, three interpreter families.** The DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol, and
`SwiftApp` are shared verbatim across every backend. Only the leaf renderer differs: a **native shim** for the
compiler-plugin frameworks (SwiftUI via `@_cdecl`/P-Invoke, Compose via `@JvmStatic`/JNI — `@Observable` VNode ↔
`mutableStateOf` VNode ↔ bound `EventCallback`), or a **pure-C# interpreter** for the bindable ones (GTK
widgets, WinUI controls, Blazor DOM), all applying the identical diff patches.

> **Consuming the library:** reference the combined `SwiftDotNet` package. For the Apple targets, also add
> `<Import Project="…/SwiftDotNet/SwiftDotNetBridge.targets" />` to your app's `.csproj` — required because
> `NativeReference` items don't flow transitively into the app's native link. GTK and Web are plain project
> references (`SwiftDotNet.Gtk` / `SwiftDotNet.Web`); no import needed.

## How this differs from .NET Comet

The closest prior art is [.NET Comet](https://github.com/dotnet/Comet) — James Clancey's SwiftUI-inspired
C# UI toolkit. The **authoring surface looks similar** (both give you `Text(...).Font(...)`, `VStack`,
`State<T>`, a recomputed body), but the substrate is fundamentally different:

> **Comet renders through .NET MAUI's handler abstraction. SwiftDotNet bypasses MAUI and renders to each
> platform's own toolkit directly — including the modern *declarative* ones (SwiftUI, Jetpack Compose) that
> MAUI predates and doesn't use.**

| | **.NET Comet** | **SwiftDotNet** |
|---|---|---|
| **Rendering substrate** | .NET **MAUI handlers** (implements `Microsoft.Maui.IButton` etc.); MAUI maps to native | The platform's **own toolkit, directly** |
| **iOS output** | UIKit via MAUI handler | **Real SwiftUI** |
| **Android output** | Android Views via MAUI handler | **Real Jetpack Compose** |
| **macOS** | Mac Catalyst (iOS-on-Mac) | Native **AppKit-hosted SwiftUI** |
| **Update mechanism** | MVU over MAUI's in-process object graph | Structural-path **diff engine** → JSON patch → native `@Observable`/`mutableStateOf` VNode tree across a C-ABI/JNI **bridge** |
| **Dependencies** | The **entire MAUI stack** | Core is dependency-free platform-neutral C#; each backend pulls only its toolkit (GTK/WinUI/Web are pure C#, no shim) |
| **Platform reach** | Wherever MAUI runs: Win, Android, iOS, macOS (Catalyst), Blazor | iOS, **tvOS**, native macOS/AppKit, Android, **Linux/GTK**, Windows/WinUI, **Web/DOM** |
| **Status** | **Archived July 11, 2025** — *"a proof of concept… no official support"*, read-only | Active, early-stage |

**Why the substrate choice matters.** Comet's bet was to lean on MAUI's abstraction and inherit its
platforms for free — the cost being MAUI's control model and its lowest-common-denominator handler layer,
and no access to the platforms' modern declarative frameworks (MAUI itself doesn't render through them).
SwiftDotNet takes the opposite bet: render as the *real* native declarative toolkit on each platform, so on
iOS you get Apple's own SwiftUI layout/animation/accessibility rather than a UIKit approximation. The price
is that SwiftUI and Compose are compiler-plugin-locked, which is exactly why those two backends need a thin
Swift/Kotlin shim plus the diff-over-a-bridge machinery — the part Comet never needs because it stays inside
MAUI's .NET process. It also shows up in the architecture: SwiftDotNet has **two backend routes** (native-shim
hosts for the compiler-locked toolkits, pure-C# interpreters for the bindable ones), where Comet has one
route — MAUI handlers — for everything.

_(Both are experimental. The distinction is that Comet is archived; SwiftDotNet is still a live design space —
which is why the DI, native-view-access, and per-view-reconciliation questions in [`plans/`](plans/) are open.)_

## Build & run

**iOS** (SwiftUI):
```bash
# 1. Build the Swift bridge (iOS/tvOS/macOS slices, min iOS 17)
native/SwiftDotNetBridge/build-xcframework.sh
# 2. Build the sample app for the simulator
dotnet build sample/SampleApp/SampleApp.csproj -f net10.0-ios -r iossimulator-arm64
# 3. Install + launch
xcrun simctl install booted sample/SampleApp/bin/Debug/net10.0-ios/iossimulator-arm64/SampleApp.app
xcrun simctl launch booted com.swiftdotnet.sample
```

**Other platforms** — the same `sample/SampleApp` project, selected by `-f`:
```bash
# macOS / tvOS — reuse the same xcframework from step 1
dotnet build sample/SampleApp -f net10.0-macos
dotnet build sample/SampleApp -f net10.0-tvos

# Android (Compose) — build the .aar first, then the app
native/SwiftDotNetComposeBridge/gradlew -p native/SwiftDotNetComposeBridge assembleRelease
dotnet build sample/SampleApp -f net10.0-android

# Windows (WinUI 3) — on a Windows machine
dotnet run --project sample/SampleApp -f net10.0-windows10.0.19041.0

# Linux/GTK — separate project; needs GTK4 (brew install gtk4 / apt install libgtk-4-1)
dotnet run --project sample/SampleApp.Gtk

# Web (Blazor WASM) — separate project; runs the whole framework in the browser
dotnet run --project sample/SampleApp.Web           # → http://localhost:5000
```

## Status

The **same 5-tab `ContentView`** (one file in `sample/SharedUI`) has been verified rendering natively on six
platforms; Windows is scaffolded pending a Windows build host:

| Platform | Verified on | Notes |
|----------|-------------|-------|
| iOS | iPhone Air / iOS 26.5 (simulator) | Real SwiftUI |
| macOS | Desktop (`NSWindow` + `NSHostingController`) | SwiftUI via AppKit hosting |
| tvOS | Apple TV 4K (simulator) | Focus-driven; `#if os(tvOS)` fallbacks for controls Apple omits |
| Android | Emulator | Real Jetpack Compose |
| Linux | GTK4 desktop | 325 real `Gtk.Widget`s, pure C# |
| Web | Chrome (Blazor WASM) | Real HTML/DOM, pure C# |
| Windows | — | WinUI 3 backend scaffolded, not yet compiled |

The demo exercises the whole vocabulary:
- **Inputs** tab: TextField, SecureField, Slider, Stepper, Picker, DatePicker, ColorPicker, Toggle, + composite `Rating`.
- **Layout** tab: Grid of shapes (color/frame/opacity), Divider, ZStack, HStack + SF Symbol Images/Label,
  ProgressView, Gauge.
- **Carousel** tab: paged `TabView` with page dots.
- **Lists** tab: Form + Sections + List + DisclosureGroup + Menu.
- **Nav** tab: NavigationStack + NavigationLink (push verified), Link, Sheet, Alert.
- Interactions confirmed live on each backend: tab switching, navigation push, Menu action, and
  Button/TextField/Toggle/Slider bindings — including the full **event → C# `State` → re-render** round-trip
  (e.g. tapping a star updates the composite `Rating` to "5/5" in the browser).
- Diff engine + bool/text/value bindings verified deterministically via a Core test harness.

### Notes
- P/Invoke resolves the bridge via `DllImport("__Internal")` — the framework is a load-time
  dependency, so its `@_cdecl` symbols are in the global namespace (a leaf-name `dlopen` ignores `@rpath`).
- `@Observable` requires iOS 17+.
- JSON is hand-rolled (`NodeJson`) — zero reflection, trim/AOT-safe (no IL2026).

### Next steps
- Compile + verify the **Windows/WinUI 3** backend on a Windows host (expect minor WinUI API fixes).
- Per-view local state ownership (child composite views keep local state across renders → view-instance reconciliation).
- Binary bridge protocol (replace JSON on the hot path); physical-device runs on iOS/Android.
- Keyed `ForEach` for animated list insert/remove/move.
- Publish the combined `SwiftDotNet` + `SwiftDotNet.Gtk` + `SwiftDotNet.Web` as NuGet packages.
