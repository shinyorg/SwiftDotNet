# Architecture

SwiftDotNet has **one Core and three interpreter families**. The C# side owns the view tree
(React-Native style); each backend reconstructs native UI from it. A **diff engine** turns every re-render
into a minimal patch so only changed nodes reach the renderer.

## The big picture

```
 C# DSL (View/State)
   │  ToNode() → TreeDiffer         ┌──────────────────────────────┐
   ▼                                │  SwiftDotNetBridge.xcframework │
 Patch ──JSON──► swiftdotnet_render ──► apply to @Observable VNode tree ──► NodeView → real SwiftUI
   ▲                                │                                    │ tap / edit / toggle
   │  State.Value = …  ◄── SwiftApp.OnEvent(id,value) ◄── [UnmanagedCallersOnly] ◄──┘ @convention(c)
   └── re-render ───────────────────┘                                      (node id + value payload)
```

The diagram shows the iOS/SwiftUI path. The **bridge** is a native shim on iOS/tvOS/macOS (Swift) and
Android (Kotlin), and an **in-process interpreter** on the pure-C# backends (GTK / WinUI / Web / Skia) — but
the patch protocol and event round-trip are **identical everywhere**.

## The Core

Everything platform-neutral lives in [`src/SwiftDotNet/Core`](../src/SwiftDotNet/Core) and compiles for
*every* TFM:

| Piece | File | Role |
|-------|------|------|
| DSL / view base | `View.cs`, `Views/*` | The declarative surface: `View`, `VStack`, `Text`, … |
| Reactive state | `State.cs` | `State<T>`; assigning `.Value` invalidates + re-renders |
| Node model | `Node.cs`, `NodeBuilder.cs` | The serializable tree a `View` lowers to via `ToNode()` |
| Serialization | `NodeJson.cs` | Hand-rolled JSON — **zero reflection, trim/AOT-safe** (no IL2026) |
| Diff engine | `TreeDiffer.cs` | Produces `replace` / `updateProps` / `setChildren` patches |
| Bridge contract | `IBridge.cs` | The one interface each backend implements |
| Runtime | `SwiftApp.cs` | Drives render, applies patches through `IBridge`, routes events |
| Styling | `EnvironmentValues.cs`, `Styles.cs`, `Theme.cs`, `Modifier.cs` | See [Global Styles](global-styles.md) |

The Core is **dependency-free**. Each backend pulls in only its own toolkit.

## Diff engine

Node ids are **structural paths** (`"0.2.1"` = root → child 2 → child 1), stable across renders, so the
differ targets nodes by id:

- a prop change emits **`updateProps`** for just that node;
- a changed child list emits **`setChildren`** on the parent;
- identical renders emit **nothing**.

Two-way-bound controls (`TextField`, `Toggle`, …) are backend "controlled components" whose local state syncs
both directions — on SwiftUI/Compose via an observable `@State`/`mutableStateOf` synced through `onChange`.

**Keyed containers.** For a keyed `List`, `DiffNode` emits `setChildren` when the child **key sequence**
changes (otherwise it recurses positionally). Ids stay positional; identity rides as a `key` prop. This is
what makes reorders cheap instead of looking like N in-place `updateProps`. See
[Collection View](collection-view.md).

## The two backend routes

Two families, chosen by whether the target toolkit is C#-bindable:

### 1. Native-shim hosts (compiler-plugin toolkits)

SwiftUI and Jetpack Compose are **compiler-plugin frameworks** — you cannot author a SwiftUI `View` or a
Compose `@Composable` from C#. So these backends ship a thin native shim that reconstructs the tree:

- **Swift** ([`native/SwiftDotNetBridge`](../native/SwiftDotNetBridge)) → `SwiftDotNetBridge.xcframework`.
  C# talks to it over a C ABI:
  - **`swiftdotnet_render(json)`** — C# pushes a patch; Swift applies it to an observed `VNode` tree so
    unchanged subtrees never rebuild.
  - **`swiftdotnet_set_event_callback(fn)`** — Swift calls it on events with a node id + optional value.
  - **`swiftdotnet_make_host_controller()`** — returns a `UIHostingController` (or `NSHostingController`)
    that C# hosts as the root.
- **Kotlin** ([`native/SwiftDotNetComposeBridge`](../native/SwiftDotNetComposeBridge)) → `.aar`, same protocol
  over JNI, with `mutableStateOf` VNodes.

P/Invoke resolves the Swift bridge via `DllImport("__Internal")` — it's a load-time dependency, so its
`@_cdecl` symbols are in the global namespace (a leaf-name `dlopen` would ignore `@rpath`). See
[Apple backend](backends/apple.md) and [Android backend](backends/android.md).

### 2. Pure-C# interpreters (bindable toolkits)

GTK4, WinUI 3, Blazor/DOM, and the Skia canvas are all fully C#-bindable (or self-drawn), so those backends
are **pure C# with no native code** — a retained-mode interpreter that maps the node tree straight to native
controls (or DOM elements, or canvas draws) and applies the *same* diff patches. Each implements `IBridge`
and resolves nodes with a positional `Find(id)`. See
[GTK](backends/linux-gtk.md), [Windows](backends/windows.md), [Web](backends/web.md), and
[Skia](backends/skia.md).

> **One Core, three families.** The DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol, and `SwiftApp`
> are shared verbatim. Only the leaf renderer differs: a native shim for the compiler-locked toolkits, a
> pure-C# widget interpreter for the bindable ones, and a self-drawing engine for Skia.

## Project layout

| Path | TFM | Role |
|------|-----|------|
| [`src/SwiftDotNet`](../src/SwiftDotNet) | **multi-target** | One library. `Core/` compiles for every TFM; `Platforms/{iOS,macOS,tvOS,Android,Windows}/` are opted in per TFM. |
| [`src/SwiftDotNet.Gtk`](../src/SwiftDotNet.Gtk) | `net10.0` | Separate pure-C# GTK4 backend (Linux shares `net10.0` with Core, so folding it in would force GTK on every consumer). |
| [`src/SwiftDotNet.Web`](../src/SwiftDotNet.Web) | `net10.0` (Razor) | Separate Blazor WebAssembly backend. |
| [`src/SwiftDotNet.Skia`](../src/SwiftDotNet.Skia) | `net10.0` | Separate self-drawing SkiaSharp engine. |
| [`src/SwiftDotNet.Skia.Maui`](../src/SwiftDotNet.Skia.Maui) | `net10.0-maccatalyst` (+more) | MAUI adapter hosting the Skia engine; composes with Shiny. |
| [`native/SwiftDotNetBridge`](../native/SwiftDotNetBridge) | Swift | SwiftUI interpreter → xcframework (5 slices). |
| [`native/SwiftDotNetComposeBridge`](../native/SwiftDotNetComposeBridge) | Kotlin | Compose interpreter → `.aar`. |
| [`sample/SharedUI`](../sample/SharedUI) | `net10.0` | The demo `ContentView` + composite `Rating`, shared by all apps. |
| [`sample/SampleApp`](../sample/SampleApp) | **multi-target** | One sample app, multi-targeted like the library. |

Why some backends are **separate** projects rather than TFMs of the combined library: GTK, Web, and Skia
all share the plain `net10.0` TFM with Core, so there's no TFM to distinguish them — folding them in would
force their dependency (Gir.Core, Blazor, SkiaSharp) onto every neutral consumer.

## Centralized hosting & registration

The per-OS bootstrap lives **in the library** as reusable abstract hosts, so an app's platform entry point
is a one-liner:

| Base host (in `SwiftDotNet`) | Platform | Subclass in the app |
|------------------------------|----------|---------------------|
| `SwiftDotNetAppDelegate : UIApplicationDelegate` | iOS / tvOS | `[Register("AppDelegate")] class AppDelegate : SwiftDotNetAppDelegate` |
| `SwiftDotNetAppDelegate : NSApplicationDelegate` | macOS | same (creates + sizes the `NSWindow`) |
| `SwiftDotNetActivity : ComponentActivity` | Android | `[Activity(MainLauncher=true)] class MainActivity : SwiftDotNetActivity` |
| `SwiftDotNetApplication : Application` | Windows | `class App : SwiftDotNetApplication` |

Each override is just `protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();`
— the MAUI `MauiProgram.cs` shape. `SwiftProgram` is the single place the app registers services, logging and
its root view; the host base takes the built app's provider and passes it to `SwiftApp.Run`, so views can
reach services via `[Inject]` / `Service<T>()`. See
[Hosting & Dependency Injection](hosting-and-di.md).

> The bases are **non-generic** abstract classes — a generic `NSObject`/`Java.Lang.Object` subclass can't be
> registered with the ObjC/Android runtimes.

## Design notes & constraints

- `@Observable` (SwiftUI) and `mutableStateOf` (Compose) require iOS 17+ / the observable model; Compose
  **strong-skipping** means an in-place VNode mutation is *skipped* unless props/children are observable.
- JSON is hand-rolled (`NodeJson`) precisely to stay trim/AOT-safe.
- `Date` crosses the bridge as Unix epoch seconds; `ColorPicker` as a hex string.

For where the architecture is still open (DI, per-view reconciliation, binary protocol), see the
**[Roadmap](roadmap.md)**.
