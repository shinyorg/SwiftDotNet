# .NET MAUI Interop

Two directions, one mechanism.

1. **SwiftDotNet inside a MAUI app** — drop a SwiftDotNet tree into a MAUI page. This has existed since the
   [Skia backend](backends/skia.md)'s MAUI host and is verified on the iOS simulator and Android emulator.
2. **MAUI content inside SwiftDotNet** — put a real `Microsoft.Maui.Controls.View` *in* a SwiftDotNet tree,
   with the [`MauiView`](#mauiview) node.

Direction 2 needed something the framework did not have: a way for a backend that owns every pixel to show
a control it cannot draw. That is the **[platform-view seam](#the-platform-view-seam)**, and it is
deliberately generic — `WebView`, which used to paint the string *"native WebView — not drawable on a
canvas"* on every self-drawing backend, is its second consumer.

> **There is no MAUI *backend*.** SwiftDotNet does not translate nodes into `Label`/`Button`/`Entry`. That
> would reach no platform the Apple, Compose and WinUI backends don't already reach at higher fidelity,
> while adding a column to every per-backend table in these docs. Interop is the supported story; a
> MAUI-controls backend is a rejected one. See [`plans/maui-interop-plan.md`](../plans/maui-interop-plan.md).

## Packages

| Package | Role | Status |
|---|---|---|
| [`src/SwiftDotNet.Maui`](../src/SwiftDotNet.Maui) | `MauiView`, `MauiViewRegistry`, `MauiPlatformViewLayer`, `MauiEmbedding`. iOS / Android / Mac Catalyst / Windows. | ✅ Builds on all three non-Windows TFMs |
| [`src/SwiftDotNet.Skia.Maui`](../src/SwiftDotNet.Skia.Maui) | The MAUI host (`SwiftDotNetSkiaView`), now also the platform-view host. | ✅ Builds; host verified on sim/emulator, **platform views not yet driven by hand** |
| [`SwiftDotNet.Graphics`](../src/SwiftDotNet.Graphics/PlatformViews.cs) | The seam itself — `IPlatformViewHost`, `PlatformViewPlacement`, `PlatformViews`. | ✅ 9 headless CI tests |

---

## Hosting SwiftDotNet in a MAUI app

`SwiftDotNetSkiaView` is an ordinary MAUI `ContentView`. Put it in a page:

```csharp
public class MainPage : ContentPage
{
    public MainPage()
    {
        var app = SwiftProgram.CreateSwiftApp();
        Content = new SwiftDotNetSkiaView(app.CreateRoot(), app.Services);
    }
}
```

Pass `IPlatformApplication.Current.Services` instead if you want the SwiftDotNet UI and the rest of the MAUI
app to resolve from one container — see [Hosting & DI](hosting-and-di.md).

**One host per app.** `SwiftApp.Run` binds a static root, so a second live `SwiftDotNetSkiaView` rebinds it.
Use one host and put your own navigation inside the tree (the sample uses a `TabView`).

The host is described in full in the [Skia backend](backends/skia.md#hosts) host table — soft keyboard,
pinch, safe-area, and the AndroidX pins.

---

## `MauiView`

Embeds a real MAUI control in a SwiftDotNet tree.

```csharp
new MauiView(() => new Microsoft.Maui.Controls.DatePicker())
    .Update(v => ((DatePicker)v).Date = _date.Value)
    .Size(320, 44)
```

| Member | What it does |
|---|---|
| `MauiView(Func<View> factory)` | Creates the control. Called **once per identity**, not per render. |
| `.Size(w, h)` | The control's size in DIPs. Effectively required — see [Measurement](#measurement). |
| `.Update(Action<View>)` | Pushes current values into the live control. Called on every frame it is placed. |
| `.Key(string)` | Stable identity across reorders. Defaults to the structural node id. |
| `.OnEvent(Action<string?>)` | Receives values the control raises via `MauiViewRegistry.Emit(key, value)`. |

Talking back to C# from the embedded control:

```csharp
new MauiView(() => new Switch())
    .Key("busy-switch")
    .Update(v => { var s = (Switch)v; s.Toggled -= OnToggled; s.Toggled += OnToggled; })
    .OnEvent(v => _busy.Value = v == "true")
    .Size(60, 32);

void OnToggled(object? s, ToggledEventArgs e)
    => MauiViewRegistry.Emit("busy-switch", e.Value ? "true" : "false");
```

Worked example: [`sample/SampleApp.Skia.Maui/MauiInteropView.cs`](../sample/SampleApp.Skia.Maui/MauiInteropView.cs)
— a native date wheel, a platform switch, an OS spinner and a `WebView`, all inside a scrolling SwiftDotNet
page, plus a `Sheet` that demonstrates the overlay rule below.

### Per-backend behaviour

| Backend | How a `MauiView` renders | Status |
|---|---|---|
| Skia (inside a MAUI app) | A real MAUI view floated over the canvas by `MauiPlatformViewLayer` | ✅ Builds; **not yet driven by hand** |
| iOS / SwiftUI | A `UIViewRepresentable` wrapping a `UIView` from `ApplePlatformViews` | 🧩 Swift type-checks (iOS/macOS/tvOS SDKs); **never run** |
| Android / Compose | A Compose `AndroidView` wrapping a view from `AndroidPlatformViews` | 🧩 `.aar` rebuilt, C# binding compiles; **never run** |
| Windows / WinUI | A `FrameworkElement` via `WindowsPlatformViews` → `WinRenderers` | 🧩 **Never compiled** (nor is the backend) |
| GTK, Web, TUI, Wayland, WebGPU, MonoGame, Godot, Unity, WPF, WinForms | The standard ⚠️ placeholder | ✅ By design — MAUI does not run there |

### Gotchas

- **Identity is the node id, not the object.** `MauiView` is reconstructed on every render pass, so the
  instance cannot be the identity. Inside a keyed `List`, pass `.Key(...)` — a row keeps its identity while
  its structural position moves.
- **The factory never crosses the wire.** Props are JSON scalars ([`NodeJson.cs`](../src/SwiftDotNet/Core/NodeJson.cs)
  is hand-rolled and reflection-free), so the delegate travels in `MauiViewRegistry` and only the `key` prop
  goes on the wire. This works because host and DSL share a process — which is exactly why the native-shim
  backends need a *different* mechanism (a view handle across the ABI) rather than a bigger version of this
  one.
- **Bind events once.** `.Update` runs every frame; `-=` before `+=` or handlers stack up.
- **Don't rebuild state-holding views.** The usual SwiftDotNet rule applies: hold your `MauiView`'s state in
  the enclosing view's fields, not in something `Body` news up each pass.

---

## The platform-view seam

A self-drawing backend owns every pixel, so a real OS control can never be *in* its tree — it has to be a
sibling floated over the canvas at the node's frame. The engine reports where; a host decides what that
means.

```csharp
public readonly record struct PlatformViewPlacement(
    string Id, string Type, Rect Frame, Rect? Clip, bool Visible,
    IReadOnlyDictionary<string, object?> Props);

public interface IPlatformViewHost
{
    void SyncPlatformViews(IReadOnlyList<PlatformViewPlacement> placements);
}

PlatformViews.Register("MauiView");   // this type is a control, not paint
```

Declaring a type through `PlatformViews.Register` is what punches the hole: a registered type stops being
painted and starts being reported. A host that cannot place native views (headless, Silk, MonoGame, Godot,
WebGPU standalone) never sets `VisualBridge.PlatformViewHost`, and those nodes keep painting exactly what
they paint today.

`SwiftDotNet.Skia.Maui` registers `MauiView` and `WebView` and implements the host. To serve a different
node type, or to place controls from another toolkit, implement `IPlatformViewHost` yourself.

### The whole set, every frame

`SyncPlatformViews` is handed the **complete** set, not a create/update/destroy delta. The scene is
recomputed every frame anyway, and a set-reconcile is the only shape that cannot leak a control when a
subtree vanishes through a `setChildren` patch. The corollary matters: a control scrolled off-screen is
still *present*, with `Visible = false`. Hide those; dispose only ids that leave the set.

### Gotchas

- **Z-order inversion — the one users will notice.** A native view always floats *above* the canvas, so
  anything the engine paints over it (a `Sheet`, `Alert`, `ActionSheet`, `Menu`, a pushed
  `NavigationStack` destination) would appear *behind* the real control. The engine resolves this by
  suppression: only placements recorded in the topmost painted layer stay visible, so a control under a
  presented sheet hides itself while one *inside* that sheet is shown. Flutter and MAUI make the same
  compromise.
- **Transforms are a no-op on platform views.** `.Offset`, `.ScaleEffect` and `.Rotation` are applied to the
  canvas matrix at paint time and are never folded into a node's frame, so a placement is the
  *untransformed* layout rect. A transformed platform view stays where it was laid out while its
  canvas-drawn siblings move — the same documented no-op as `.ScaleEffect` on [GTK](backends/linux-gtk.md).
- **Gestures that start on the control are consumed by it.** A `MauiView` inside a SwiftDotNet `ScrollView`
  repositions correctly while the page scrolls (a fresh frame is emitted on every paint), but you cannot
  *drag it* to scroll the page. Keep embedded controls out of long scrollers, or give the row a handle.
- **Clipping is applied per control**, by rebasing the viewport rect onto the control's own origin
  (`VisualElement.Clip`). The canvas's clip does nothing for a view that floats above it.
- **Input transparency is load-bearing.** The layer the controls live in is `InputTransparent = true` with
  `CascadeInputTransparent = false` — "the panel is a hole, its children are not". Get this pair wrong and
  either the canvas stops receiving touches or the embedded controls do.

### <a name="measurement"></a>Measurement

`.Size(w, h)` is required in practice. Layout has to settle before the host can place anything, so the
engine cannot ask a control that does not exist yet how big it wants to be. Without it a platform view
fills the available width and takes a 120pt height (the same default `WebView` has always had). Feeding a
real measured size back into layout is possible but costs a one-frame settle; it is deferred until the
static size proves annoying.

---

## `WebView` on the self-drawing backends

`WebView` has always been the node the canvas could not draw. Inside a MAUI app it is now a real
`Microsoft.Maui.Controls.WebView`, with no DSL change:

```csharp
new WebView("https://learn.microsoft.com/dotnet/maui/").Frame(height: 220)
```

Everywhere else — headless Skia, Silk, WebGPU, MonoGame, Godot, Unity — it still paints its placeholder,
because no host is attached to place one. The status is per **host**, not per backend.

---

## MAUI as the guest: the native-shim backends

When SwiftDotNet is the app and MAUI is the guest, MAUI has no application object, no service provider and
no `IMauiContext` — and a `Microsoft.Maui.Controls.View` is inert until all three exist. `MauiEmbedding`
creates them.

```csharp
// iOS AppDelegate, before the first render
MauiEmbedding.Initialize(this, window);
ApplePlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as UIView);

// Android Activity.OnCreate
MauiEmbedding.Initialize(Application, this);
AndroidPlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as Android.Views.View);

// WinUI startup
MauiEmbedding.Initialize(this, window);
WindowsPlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as FrameworkElement);
```

Each backend's registration lives in that backend's own assembly, and `SwiftDotNet.Maui` references no
backend at all. That is not tidiness: if it referenced one, an app using the Skia MAUI host would end up
with two copies of `SwiftDotNet.dll` in its graph — the neutral one the Skia chain resolves and a platform
one the reference would drag in. `CreatePlatformView` returns `object` for the same reason.

### Gotchas

- **`UseMauiEmbedding` is a trap.** `Microsoft.Maui.Embedding.EmbeddingExtensions` in `Microsoft.Maui.dll`
  is **internal** in MAUI 10.0.80 — code written against it compiles nowhere. The public entry points are
  `UseMauiEmbeddedApp<TApp>()` (in `Microsoft.Maui.Controls.Xaml`) and `CreateEmbeddedWindowContext` /
  `ToPlatformEmbedded` (in `Microsoft.Maui.Controls`). Both assemblies declare a
  `Microsoft.Maui.Controls.Embedding.EmbeddingExtensions`, so name the type and you get CS0433 — call the
  extension methods in extension syntax.
- **`Initialize` is platform-specific by necessity.** MAUI needs the real application object and the real
  window; there is no cross-platform spelling of either, and inventing one would only move the `#if` into
  every caller.
- **No placement set, so no automatic disposal.** The native-shim backends hand the view to SwiftUI/Compose
  and have no equivalent of the per-frame placement list, so nothing detects a removed node. Call
  `MauiEmbedding.Release(key)` when you know a view is gone.
- **Rebuild the native bridges after pulling this.** Core's iOS slice now P/Invokes
  `swiftdotnet_set_platform_view_provider`, and the Android slice binds a new Kotlin interface. `build/` is
  gitignored, so a stale `SwiftDotNetBridge.xcframework` fails at *app link* time with
  `Undefined symbols: _swiftdotnet_set_platform_view_provider` — not at compile time, and not in the project
  that introduced it. Run [`native/SwiftDotNetBridge/build-xcframework.sh`](../native/SwiftDotNetBridge/build-xcframework.sh)
  and the Compose bridge's `./gradlew assembleRelease` (copying the `.aar` to `build/`), as
  [Getting Started](getting-started.md) describes.
- **Status: never run.** Everything in this section compiles (and the Swift type-checks against the iOS,
  macOS and tvOS SDKs), but no MAUI control has been shown inside a SwiftUI or Compose tree. The direction
  that *is* exercised is the other one.

---

## Cross-links

- [Custom Controls](custom-controls.md) — the renderer registry `MauiView` is built on, and how the
  platform-view registry sits beside it.
- [Architecture → the platform-view seam](architecture.md#the-platform-view-seam) — where this fits among
  the engine's other seams.
- [Skia backend](backends/skia.md) — the MAUI host in full.
- [`plans/maui-interop-plan.md`](../plans/maui-interop-plan.md) — the design, the alternatives rejected
  (a MAUI-controls backend), and what is still unverified.
