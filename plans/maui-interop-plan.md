# Plan: MAUI interop — hosting SwiftDotNet in MAUI, and MAUI content in SwiftDotNet

**Status:** **Implemented** — engine seam CI-tested; the MAUI half builds but has not been driven by hand ·
**Date:** 2026-08-23
**Save to (repo convention):** `plans/maui-interop-plan.md`

User-facing reference: [`docs/maui-interop.md`](../docs/maui-interop.md).

## Built — 2026-08-23

All four phases landed in one pass rather than sequentially.

| Piece | Where | State |
|---|---|---|
| `IPlatformViewHost`, `PlatformViewPlacement`, `PlatformViews` registry | [`SwiftDotNet.Graphics/PlatformViews.cs`](../src/SwiftDotNet.Graphics/PlatformViews.cs) | ✅ 9 headless CI tests ([`PlatformViewSeamTests`](../tests/SwiftDotNet.Tests/PlatformViewSeamTests.cs)); suite 373 green |
| Placement collection, clip stack, layer-based overlay suppression | [`VisualBridge`](../src/SwiftDotNet.Graphics/VisualBridge.cs), `VisualNode`, `VisualNodePaint` | ✅ same tests |
| `MauiView`, `MauiViewRegistry`, `MauiPlatformViewLayer`, `MauiEmbedding` | [`src/SwiftDotNet.Maui`](../src/SwiftDotNet.Maui) | ✅ builds on ios / maccatalyst / android |
| Host wiring (the absolute layer, focus reconciliation) | [`SwiftDotNetSkiaView`](../src/SwiftDotNet.Skia.Maui/SwiftDotNetSkiaView.cs) | ✅ builds; **not yet driven by hand** |
| `WebView` as a platform view on the MAUI host | same | ✅ builds; ⚠️-placeholder path unchanged everywhere else |
| Swift: provider C ABI + `EmbeddedPlatformView` + `MauiView` dispatch | [`Bridge.swift`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift) | 🧩 type-checks against the iOS, macOS and tvOS SDKs; **never run** |
| Kotlin: `PlatformViewProvider` + `PlatformViewNode` | [`Bridge.kt`](../native/SwiftDotNetComposeBridge/src/main/kotlin/com/swiftdotnet/bridge/Bridge.kt) | 🧩 `.aar` rebuilt via Gradle; **never run** |
| `ApplePlatformViews` / `AndroidPlatformViews` / `WindowsPlatformViews` | `src/SwiftDotNet/Platforms/{iOS,Android,Windows}` | 🧩 iOS + Android compile; Windows **never compiled** (nor is that backend) |
| Sample MAUI-interop tab | [`sample/SampleApp.Skia.Maui/MauiInteropView.cs`](../sample/SampleApp.Skia.Maui/MauiInteropView.cs) | ✅ builds on all three TFMs |

### Where the build diverged from the design below

1. **`UseMauiEmbedding` does not exist for us.** `Microsoft.Maui.Embedding.EmbeddingExtensions` in
   `Microsoft.Maui.dll` is **`internal`** in MAUI 10.0.80 — the API every embedding sample names compiles
   nowhere. The public path is `UseMauiEmbeddedApp<TApp>()` (in `Microsoft.Maui.Controls.Xaml`) plus
   `CreateEmbeddedWindowContext` / `ToPlatformEmbedded` (in `Microsoft.Maui.Controls`), and because both
   assemblies declare a `Microsoft.Maui.Controls.Embedding.EmbeddingExtensions`, naming the type is CS0433
   — they must be called in extension syntax. This was the single biggest surprise.
2. **The reconciler moved into `SwiftDotNet.Maui`**, not `SwiftDotNet.Skia.Maui` as §3.2 assumed. It is not
   Skia-specific, and `SwiftDotNet.Graphics` is platform-neutral, so a pinned reference costs nothing and
   any future MAUI-based host reuses it.
3. **A placement carries the node's props.** The §2.1 sketch didn't, and then `WebView` needed its `url` to
   build the control. Cheap, and it removes the host's only reason to reach back into the tree.
4. **`SwiftDotNet.Maui` references no backend at all**, and `CreatePlatformView` returns `object`. §5's
   WinUI row assumed the package would call `WinRenderers` directly; that would drag Core's *Windows* slice
   into the graph of every app using the Skia MAUI host — two `SwiftDotNet.dll`s, the TPV-collision trap the
   WPF work already hit. Each backend's registration lives in that backend's own assembly instead, which
   also made the Apple and Android halves cleaner than planned.
5. **Every Core project reference from `SwiftDotNet.Maui` is pinned** with
   `SetTargetFramework="TargetFramework=net10.0"` for the same reason — without it a `net10.0-ios` build
   resolves Core's SwiftUI slice.
6. **Open question 1 answered, not deferred** (see §7).
7. **`Map` was not wired up.** §4 paired it with `WebView`; it needs a MAUI map control and a props
   translation of the whole `MapTypes` wire, which is [Maps](../docs/maps.md) work, not seam work.

Two directions, one piece of new machinery.

1. **SwiftDotNet inside a MAUI app** — already exists as
   [`SwiftDotNetSkiaView`](../src/SwiftDotNet.Skia.Maui/SwiftDotNetSkiaView.cs) (canvas + hidden `Entry` for
   the IME + pinch), verified on the iOS simulator and Android emulator per
   [`skia-maui-host-plan.md`](skia-maui-host-plan.md). This plan **keeps that as the host** and does not add
   a second one.
2. **MAUI content inside SwiftDotNet** — a `MauiView` node that renders a *real* `Microsoft.Maui.Controls.View`
   inside a SwiftDotNet tree.

Direction 2 is blocked on something that does not exist today: a **platform-view seam**. A self-drawing
backend owns every pixel, so a real OS control cannot be "in" the tree — it has to be a sibling floated over
the canvas at the node's frame. `WebView` on the self-drawing backends is currently a painted apology
(`VisualNodePaint.cs:425` — *"native WebView — not drawable on a canvas"*), which is exactly the same gap.
Build the seam once, generically; `MauiView` is its first consumer and `WebView`/`Map` are the second.

> **Explicitly not in scope: a native-control MAUI backend** (mapping nodes to `Label`/`Button`/`Entry`/…).
> It reaches no platform the Apple, Compose and WinUI backends don't already reach with higher fidelity, it
> costs ~1.5–1.8k LOC (GTK is 1,727; WinUI 1,606), and — the real price — it adds a column to every
> per-backend behaviour table in `docs/` forever, on a matrix that already carries three backends that have
> never been run. Revisit only if someone needs the embedded island to inherit the host app's MAUI styles
> and accessibility, and record that decision here rather than drifting into it.

---

## 1. Guiding decisions

- **Inside the MAUI host, a "platform view" *is* a MAUI view.** The host is already a
  `Microsoft.Maui.Controls.ContentView` wrapping a `MauiGrid`. So embedding needs **no `IMauiContext`, no
  `UseMauiEmbedding`, no `ToPlatformEmbedded`, no per-OS handler code** — add the `View` as a child of an
  absolutely-positioned overlay panel in that same grid and let MAUI lay it out. This collapses the hard
  part of direction 2 to bookkeeping. It is also *why* direction 1 has to come first: MAUI content is only
  cheap when MAUI is already running the app.
- **The seam is generic, not MAUI-shaped.** The engine emits *placements* (`id`, rect, visibility, clip); a
  host decides what a placement means. The MAUI host makes a MAUI view; a future WPF/WinForms/Apple host
  makes its own. Hosts that cannot float a native view (headless, Silk, MonoGame, Godot, WebGPU standalone)
  simply don't implement the interface and keep today's ⚠️ placeholder.
- **Identity is the node id, not the object.** `MauiView` is reconstructed on every render pass — this is
  the exact trap that caused the "repaint defect" (`skia-maui-host-plan.md`). The factory therefore cannot
  be the identity; the **structural node id** is (or an explicit `.Key` for keyed lists).
- **The factory travels in-process, beside the wire, not on it.** Props are JSON scalars
  (`NodeJson.cs` is hand-rolled and reflection-free — a delegate will never fit). A static registry keyed by
  node id carries it. This works only because host and DSL share a process, which is true of every pure-C#
  backend and false across the Swift/Kotlin ABI — hence Phase 3 is a different problem, not a bigger one.
- **Graceful fallback is already the contract.** An unregistered `CustomView` type paints ⚠️
  ([`custom-controls.md`](../docs/custom-controls.md)), so `MauiView` on GTK/Web/TUI/Godot degrades without
  a single `#if`.

---

## 2. Phase 0 — the platform-view seam (`SwiftDotNet.Graphics`)

Backend-agnostic, pure C#, headlessly testable. This is the only genuinely new architecture.

### 2.1 The contract

```csharp
namespace SwiftDotNet.Graphics;

/// <summary>Where a real OS control must be floated over the canvas this frame.</summary>
public readonly record struct PlatformViewPlacement(
    string Id,            // node id — the identity key
    string Type,          // node type: "MauiView", "WebView", "Map", …
    Rect Frame,           // window-relative DIPs, post-scroll, post-transform-translation
    Rect? Clip,           // nearest scrolling/clipping ancestor, in the same space
    bool Visible);        // false when off-screen, fully clipped, or under an active overlay

/// <summary>Implemented by a host that can place real OS controls above the canvas.</summary>
public interface IPlatformViewHost
{
    /// <summary>Full set for this frame. The host creates, moves, hides and disposes to match.</summary>
    void SyncPlatformViews(IReadOnlyList<PlatformViewPlacement> placements);
}
```

One call per frame with the complete set — not create/update/destroy deltas. The engine already recomputes
every frame, and a set-reconcile is the only shape that cannot leak a view when a subtree vanishes via
`setChildren`.

### 2.2 Engine changes

- `VisualNode` gains `bool IsPlatformView` (true for `MauiView`, `WebView`, and `Map` once registered) and
  contributes a placement during the paint walk, where `Frame` is already final (`VisualNode.cs:30`).
- `VisualBridge` collects placements into a list during paint and hands them to
  `IPlatformViewHost?` after the paint pass, before the overlay pass.
- A platform-view node **paints nothing** where the control will land (the hole), but still paints its
  `.Background`/`.Border` modifiers underneath — a native control with a transparent background should still
  show the SwiftDotNet chrome around it.
- **Overlay suppression.** `VisualNode.HasActiveOverlay` (`VisualNodeOverlay.cs:26`) already knows when a
  Sheet/Alert/Menu/nav-push is up. If any ancestor-or-root overlay is active, every placement is emitted
  with `Visible = false`. See Crux 1.
- `Clip` walks up to the nearest `ScrollOffset`-owning ancestor and intersects.

### 2.3 Tests (headless, macOS — the ones that actually run in CI)

A `RecordingPlatformViewHost` capturing `(id, frame, visible)` per frame makes all of this testable without
MAUI:

| Test | Asserts |
|---|---|
| Placement emitted for a `MauiView` at the laid-out rect | frame == node `Frame` |
| Node scrolled in a `ScrollView` | frame tracks `ScrollOffset`; `Clip` == viewport |
| Node scrolled out of the viewport | `Visible == false` |
| Subtree removed by `setChildren` | id absent from the next frame's set (host can dispose) |
| `Sheet`/`Alert` presented | every placement `Visible == false` |
| Two `MauiView`s reordered in a keyed `List` | ids follow their keys, no churn |

---

## 3. Phase 1 — `MauiView` and the MAUI host implementation

### 3.1 New package `SwiftDotNet.Maui`

TFMs `net10.0-ios;net10.0-android;net10.0-maccatalyst` (+ `net10.0-windows…` on Windows), `<UseMaui>true`.
Holds the DSL surface and the registry only — no rendering — so Phase 3 can reuse it unchanged.

```csharp
public sealed class MauiView : CustomView
{
    readonly Func<Microsoft.Maui.Controls.View> _factory;
    Action<Microsoft.Maui.Controls.View>? _update;
    string? _key;
    double _w = -1, _h = -1;

    public MauiView(Func<Microsoft.Maui.Controls.View> factory) => _factory = factory;

    /// <summary>Stable identity across reorders — defaults to the structural node id.</summary>
    public MauiView Key(string key) { _key = key; return this; }

    /// <summary>Called on every render pass with the live control, to push new values into it.</summary>
    public MauiView Update(Action<Microsoft.Maui.Controls.View> update) { _update = update; return this; }

    /// <summary>Required in v1 — see Crux 3 (measurement).</summary>
    public MauiView Size(double w, double h) { _w = w; _h = h; return this; }

    protected override string TypeName => "MauiView";

    protected override void Configure(CustomNode n)
    {
        MauiViewRegistry.Bind(_key ?? n.Id, _factory, _update);
        n.Prop("key", _key ?? n.Id).Prop("w", _w).Prop("h", _h);
        n.OnEvent(v => _onEvent?.Invoke(v));   // lets an embedded control talk back
    }
}
```

Usage reads like any other view:

```csharp
new VStack(
    new Text("Rendered by SwiftDotNet"),
    new MauiView(() => new Microsoft.Maui.Controls.DatePicker())
        .Update(v => ((DatePicker)v).Date = _date.Value)
        .Size(320, 44),
    new Button("Done", Save))
```

`MauiViewRegistry` is a `static Dictionary<string, (factory, update)>`, written during render and read
during paint — both on the UI thread in the MAUI host, so no locking, but assert the thread in DEBUG.
Entries are dropped when the host reports an id gone (Crux 4).

### 3.2 `SwiftDotNet.Skia.Maui` implements `IPlatformViewHost`

`SwiftDotNetSkiaView` gains a third child in `_layout`, above the canvas and the 1×1 `Entry`:

```csharp
_overlay = new AbsoluteLayout { InputTransparent = true, CascadeInputTransparent = false };
_layout.Children.Add(_overlay);
_bridge.PlatformViewHost = this;
```

`InputTransparent = true` with `CascadeInputTransparent = false` is load-bearing: the panel itself must not
swallow the touches the canvas needs, while its children must still receive their own.

`SyncPlatformViews` reconciles by id: create via the registry factory on first sight, `SetLayoutBounds` to
the placement frame on every frame, `IsVisible = placement.Visible`, `Clip` to the placement clip, call the
updater, and remove + `Handler?.DisconnectHandler()` for any id no longer present.

### 3.3 Focus / IME reconciliation

The host focuses a hidden `Entry` whenever the engine focuses a text node (`AttachSoftKeyboard`). An
embedded MAUI `Entry` is a second claimant. Rules:

- Embedded view `Focused` → `_bridge.ClearFocus()` so the hidden entry lets go and the caret stops blinking.
- Engine focus change to a canvas-drawn field → `Unfocus()` the currently-focused embedded view.

### 3.4 Sample + verification

Add a **"MAUI" tab** to `sample/SampleApp.Skia.Maui` embedding a `DatePicker`, a `WebView` and a MAUI
`ActivityIndicator` — three controls the canvas genuinely cannot draw — inside a scrolling SwiftDotNet page,
with one of them inside a `Sheet` to exercise Crux 1. Verify by hand on the iOS simulator and the Android
emulator, and say so in the docs status table (this half cannot be CI-verified on macOS).

---

## 4. Phase 2 — retarget `WebView` (and `Map`) onto the seam

Once Phase 1 lands this is nearly free and is the strongest argument for the seam: on the MAUI host,
`WebView` stops painting *"not drawable on a canvas"* and becomes a real `Microsoft.Maui.Controls.WebView`;
`Map` can host the MAUI/community map control the same way. On every other self-drawing host the placeholder
stays, unchanged, because no `IPlatformViewHost` is attached — which keeps
[`docs/backends/skia.md`](../docs/backends/skia.md) honest per-host rather than per-backend.

---

## 5. Phase 3 (gated — do not start without a concrete request)

`MauiView` on the backends where SwiftDotNet is the host and MAUI is the guest. Different problem, listed so
the shape is on record:

| Backend | What it needs | Blocker |
|---|---|---|
| WinUI | `UseMauiEmbedding()`, then `WinRenderers.Register("MauiView", ctx => (FrameworkElement)view.ToPlatformEmbedded(ctx))` | The WinUI backend has **never compiled** ([`windows.md`](../docs/backends/windows.md)) — fix that first |
| Apple (SwiftUI shim) | `view.ToPlatform(mauiContext)` → `UIView`, exposed across a **new C ABI entry point** (`swiftdotnet_platform_view(token) -> UnsafeRawPointer`), consumed by a Swift `UIViewRepresentable` registered through `swiftDotNetRegisterRenderer` (`Bridge.swift:1899`) | New ABI surface + MAUI embedding init in a non-MAUI app |
| Android (Compose shim) | Same via JNI + `AndroidView { lookup(token) }` | Same, plus the AndroidX pin reconciliation the Skia MAUI csproj already documents |

The registry trick of §3.1 does **not** cross the ABI — a delegate can't. Phase 3 needs a real handle table
returning a native pointer, which is the one piece of genuinely new interop.

**Non-targets, permanently:** GTK, Web, TUI, Wayland, MonoGame, Godot, Unity, WebGPU-standalone. MAUI does
not run there; the ⚠️ placeholder is the right answer.

---

## 6. Cruxes

### Crux 1 — z-order inversion (the one that will generate bug reports)

A native view always floats **above** the canvas. Anything the engine paints over the node — `Sheet`,
`Alert`, `ActionSheet`, `Menu`, a pushed `NavigationStack` destination, `.Overlay` — would appear *behind*
the real control. Flutter and MAUI both live with this.

**Resolution:** suppress. When any overlay is active, all placements go `Visible = false`. Cost: an embedded
control inside a presented `Sheet` disappears when a second overlay opens above it. Accepted, documented as a
gotcha, revisited only if it bites.

### Crux 2 — input arbitration inside a scroller

A gesture starting on the platform view is consumed by it and never reaches `SkiaPointerRouter`, so a
`MauiView` inside a SwiftDotNet `ScrollView` cannot be dragged to scroll the page. Repositioning during a
scroll works fine (a new frame is emitted each paint); it is only gesture *capture* that's lost.
**v1:** document it and keep embedded controls out of long scrollers, or give the row a drag handle.

### Crux 3 — measurement

`IVisualRenderer.Measure` has to answer before any MAUI view exists, and the MAUI view is the only thing that
knows its intrinsic size. **v1:** `.Size(w, h)` is required and `Measure` returns it — honest and one-way.
**Later (opt-in):** the host calls `view.Measure(...)` and feeds the result back through the registry, which
costs a one-frame settle. Don't build the second until the first is proven annoying.

### Crux 4 — lifetime

Two leaks to avoid: the MAUI view (handler + native peer) and the registry entry. Both are keyed by node id
and both are released on the first frame the id is absent from the placement set. A node that is merely
scrolled off-screen is still *present* (`Visible = false`), so it must not be disposed — this distinction is
the whole reason `SyncPlatformViews` takes the full set rather than deltas.

### Crux 5 — DPI

Engine `Frame` is in DIPs (layout space; `OnPaintSurface` applies `_scale` only to the canvas and
`OnTouch` divides it back out). MAUI's `AbsoluteLayout` bounds are also DIPs. **No conversion** — but confirm
on a 3× device before believing it.

---

## 7. Open questions

1. ~~**Does `VisualNode.Frame` include transform translation?**~~ **Answered — it does not.** `.Offset`,
   `.ScaleEffect` and rotation are applied to the *canvas matrix* at paint time
   (`VisualNodePaint.cs:37–44`), never folded into `Frame`. So a placement taken from `Frame` is the
   untransformed layout rect, and a transformed platform view would sit where it was laid out while its
   canvas-drawn siblings move. **Decision:** don't compose the matrix in v1 — declare transforms a
   **no-op on platform views** and document it, the same way `.ScaleEffect` is a documented no-op on GTK.
2. **`SwiftDotNet.Maui` vs. folding `MauiView` into `SwiftDotNet.Skia.Maui`.** Separate is right only if
   Phase 3 ever happens. Cheap to start separate; note the extra package in the docs index either way.
3. **Should `WebView` keep its own node type** once it's a platform view, or lower to `MauiView` on that
   host? Keep the node type — it is cross-backend vocabulary and only its *rendering* is host-specific.

---

## 8. Documentation impact (part of "done", per `CLAUDE.md`)

| Change | Page |
|---|---|
| The platform-view seam (cross-cutting) | [`docs/architecture.md`](../docs/architecture.md) — new section beside "The renderer seam" |
| `IPlatformViewHost` + `MauiView` registration | [`docs/custom-controls.md`](../docs/custom-controls.md) |
| MAUI host gains platform views; `WebView` status changes **on that host only** | [`docs/backends/skia.md`](../docs/backends/skia.md) |
| "Rendering inside an existing MAUI app" as a first-class story | [`docs/backends/README.md`](../docs/backends/README.md) → *Choosing a backend*; **no new row** in the platform matrix — this is not a backend |
| `MauiView` in the view reference | [`docs/views-and-controls.md`](../docs/views-and-controls.md) |
| Phase 3 as deferred | [`docs/roadmap.md`](../docs/roadmap.md) + [`plans/README.md`](README.md) status table |

Status honesty: Phase 0 can be ✅ with CI tests; Phase 1's MAUI half is hand-verified on simulator/emulator
at best, and must say exactly that.
