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
Android (Kotlin), and an **in-process interpreter** on the pure-C# backends (GTK / WinUI / Web / TUI /
Skia / WebGPU) — but
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
| Layout math | `GridLayout.cs`, `GridEngine.cs` | `GridTrack`/`LayoutFlags` and the shared grid-placement + proportional-bounds math — see below |

The Core is **dependency-free**. Each backend pulls in only its own toolkit.

**Core normally describes; `GridEngine` decides.** Almost everything above is declarative — the Core lowers
a view to a node and each backend interprets it however its toolkit prefers. [`Grid`](views-and-controls.md#grid)
and [`AbsoluteLayout`](views-and-controls.md#absolutelayout) are the exception: which cell a child lands in,
and what a proportional bound resolves to, are answered *once* in Core
([`GridEngine`](../src/SwiftDotNet/Core/GridEngine.cs), [`AbsoluteLayoutBounds`](../src/SwiftDotNet/Core/GridLayout.cs))
and consumed by every C# backend, because seven independent implementations of "where does a pinned child go"
would silently disagree. Track *sizing* stays per-backend, since GTK/WinUI/TUI/Web all hand it to a native
grid that already does it; only Skia computes it from scratch, and the Swift and Kotlin shims — which can't
call into Core — port both halves line for line.

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

GTK4, WinUI 3, Blazor/DOM, XenoAtom.Terminal.UI, and the self-drawing canvases are all fully C#-bindable (or
self-drawn), so those backends are **pure C# with no native code** — a retained-mode interpreter that maps
the node tree straight to native controls (or DOM elements, terminal cells, or canvas draws) and applies the
*same* diff patches. Each implements `IBridge` and resolves nodes with a positional `Find(id)`. See
[GTK](backends/linux-gtk.md), [Windows](backends/windows.md), [Web](backends/web.md),
[Terminal/TUI](backends/tui.md), [Skia](backends/skia.md), [WebGPU](backends/webgpu.md) and
[Unity](backends/unity.md).

> **One Core, three families.** The DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol, and `SwiftApp`
> are shared verbatim. Only the leaf renderer differs: a native shim for the compiler-locked toolkits, a
> pure-C# widget interpreter for the bindable ones, and a self-drawing engine for Skia/WebGPU/Unity.

## The renderer seam

A self-drawing backend is a whole UI toolkit — measure, arrange, hit-test, gesture recognition, scrolling,
animation, *and* painting. Only the last of those is actually about the rasterizer. So the self-drawing
engine lives once, in [`SwiftDotNet.Graphics`](../src/SwiftDotNet.Graphics), and a rasterizer supplies three
small interfaces:

| Interface | Supplies | Why it exists |
|---|---|---|
| [`ICanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs) | The paint primitives | The whole drawing vocabulary, deliberately closed |
| [`IFontProvider`](../src/SwiftDotNet.Graphics/Text.cs) | Fonts + measurement | The **layout** pass needs text metrics long before anything is drawn |
| [`IImageDecoder`](../src/SwiftDotNet.Graphics/Image.cs) | Bytes → a drawable image | The decode target is rasterizer-specific (bitmap vs. GPU texture) |

```
                     SwiftDotNet.Graphics
   VisualBridge ─► VisualNode: Measure / Arrange / HitTest / Draw
                             │
                    ICanvas · IFontProvider · IImageDecoder
        ┌────────────────────┼────────────────────┐
   SkiaCanvas          WebGpuCanvas          GodotCanvas
        │                                    (Godot's own 2D
   (MonoGame and Unity                        draw commands)
    reuse SkiaCanvas)
```

The split is roughly **3,600 lines of engine to 530 lines of Skia adapter** — a good measure of how little
of a self-drawing toolkit is really about the rasterizer.

Two design choices in `ICanvas` are worth knowing:

- **The vocabulary is closed and small**: rounded rects, ovals, circles, lines, images and text, under a
  save/restore transform stack with *rectangular* clipping. That is the complete set the DSL's node types
  draw. Notably absent is an arbitrary path primitive — adding one would make every future backend owe a
  full vector rasterizer, so it belongs behind a capability check rather than in the interface.
- **Shadows and gradients are descriptions, not objects.** The engine used to hang an
  `SKImageFilter.CreateDropShadow(...)` off its paint, which forces every backend to own an image-filter
  graph. Carrying a shadow as four numbers instead lets the Skia adapter rebuild exactly that filter while
  the WebGPU backend renders it as an SDF falloff in the same draw call.

### What the seam has since had to absorb

Two things the original Skia-shaped seam did not anticipate, both added because a host needed them and
neither specific to one backend:

- **[`VisualBridge.ClearColor`](../src/SwiftDotNet.Graphics/VisualBridge.cs)** — the paint pass clears to the
  theme's window background, which is right for a UI that owns the window and wrong for a HUD drawn over a
  game scene. A host that composites sets it to a transparent colour. It lives on the bridge because `Draw`
  owns the clear.
- **[`FrameLoopSyncContext`](../src/SwiftDotNet.Graphics/FrameLoopSyncContext.cs)** — game loops have no
  synchronization context, so an off-thread `State<T>` mutation would rebuild the tree while the paint pass
  reads it. Every loop-driven host installs one before `SwiftApp.Run` and drains it once per frame.

**[Godot](backends/godot.md) is the seam's real test**, and it passed: it is the first `ICanvas`
implementation whose target is a *retained* renderer, where clipping and group opacity are properties of a
scene object rather than of a draw call. Everything above the seam was reused unchanged. The one thing the
port needed was care about draw ordering — see that page for the specific trap.

See [Skia](backends/skia.md), [WebGPU](backends/webgpu.md), [MonoGame](backends/monogame.md),
[Godot](backends/godot.md) and [Unity](backends/unity.md).

## Project layout

| Path | TFM | Role |
|------|-----|------|
| [`src/SwiftDotNet`](../src/SwiftDotNet) | **multi-target** | One library. `Core/` compiles for every TFM; `Platforms/{iOS,macOS,tvOS,Android,Windows}/` are opted in per TFM. |
| [`src/SwiftDotNet.Gtk`](../src/SwiftDotNet.Gtk) | `net10.0` | Separate pure-C# GTK4 backend (Linux shares `net10.0` with Core, so folding it in would force GTK on every consumer). |
| [`src/SwiftDotNet.Web`](../src/SwiftDotNet.Web) | `net10.0` (Razor) | Separate Blazor WebAssembly backend. |
| [`src/SwiftDotNet.Graphics`](../src/SwiftDotNet.Graphics) | `net10.0`, `net8.0`, `netstandard2.1` | The self-drawing **engine** — layout, hit-testing, gestures, paint pass — minus any rasterizer. Dependency-free, like Core. |
| [`src/SwiftDotNet.Skia`](../src/SwiftDotNet.Skia) | `net10.0`, `net8.0`, `netstandard2.1` | The SkiaSharp binding of that engine's seam (canvas, fonts, image decode) plus hosts. |
| [`src/SwiftDotNet.WebGpu`](../src/SwiftDotNet.WebGpu) | `net10.0` | A from-scratch GPU rasterizer for the same seam: SDF shapes, a glyph atlas, wgpu-native. No Skia. |
| [`src/SwiftDotNet.MonoGame`](../src/SwiftDotNet.MonoGame) | `net10.0`, `net8.0` | MonoGame host: a `DrawableGameComponent` that draws the Skia engine into a `Texture2D`. |
| [`src/SwiftDotNet.Godot`](../src/SwiftDotNet.Godot) | `net8.0` (Godot.NET.Sdk) | Godot host: a `Control` node **and** an `ICanvas` on Godot's own 2D renderer. No Skia, no native library. |
| [`src/SwiftDotNet.Godot.Skia`](../src/SwiftDotNet.Godot.Skia) | `net8.0` (Godot.NET.Sdk) | The Skia-into-a-texture variant of that control; separate so the native route stays dependency-free. |
| [`unity/com.swiftdotnet.unity`](../unity/com.swiftdotnet.unity) | Unity package | The Unity host: draws the Skia engine into a `Texture2D` and pumps input. |
| [`src/SwiftDotNet.Tui`](../src/SwiftDotNet.Tui) | `net10.0` | Separate pure-C# terminal backend over XenoAtom.Terminal.UI; includes its own PNG decoder and image→character-art renderer. |
| [`src/SwiftDotNet.Tui.Graphics`](../src/SwiftDotNet.Tui.Graphics) | `net10.0` | Optional add-on: Sixel/Kitty/iTerm2 pixel images (pulls SkiaSharp, hence separate from the backend). |
| [`src/SwiftDotNet.Skia.Maui`](../src/SwiftDotNet.Skia.Maui) | `net10.0-maccatalyst` (+more) | MAUI adapter hosting the Skia engine; composes with Shiny. |
| [`native/SwiftDotNetBridge`](../native/SwiftDotNetBridge) | Swift | SwiftUI interpreter → xcframework (5 slices). |
| [`native/SwiftDotNetComposeBridge`](../native/SwiftDotNetComposeBridge) | Kotlin | Compose interpreter → `.aar`. |
| [`sample/SharedUI`](../sample/SharedUI) | `net10.0` | The demo `ContentView` + composite `Rating`, shared by all apps. |
| [`sample/SampleApp`](../sample/SampleApp) | **multi-target** | One sample app, multi-targeted like the library. |

Why some backends are **separate** projects rather than TFMs of the combined library: GTK, Web, Skia, WebGPU
and the terminal backend all share the plain `net10.0` TFM with Core, so there's no TFM to distinguish them —
folding them in would force their dependency (Gir.Core, Blazor, SkiaSharp, wgpu-native,
XenoAtom.Terminal.UI) onto every neutral consumer. `SwiftDotNet.Graphics` is the exception that proves the
rule: it is dependency-free, which is exactly why it can sit between Core and every self-drawing backend.

Core, `SwiftDotNet.Graphics` and `SwiftDotNet.Skia` also target **netstandard2.1** (Unity's scripting
runtime) and **net8.0** (Godot's) — see
[Unity → why Core multi-targets netstandard2.1](backends/unity.md#why-core-multi-targets-netstandard21).
The `net8.0` target is not redundant with netstandard2.1: `init`-only setters carry a `modreq` on
`IsExternalInit`, which is a polyfilled *internal* type on netstandard2.1 and a BCL type from net5.0 on, so
an assembly compiled against the netstandard2.1 build throws `MissingMethodException` the first time it
evaluates a `with` expression against the net10.0 one. Mixed-TFM consumers need a real net8.0 build.

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
