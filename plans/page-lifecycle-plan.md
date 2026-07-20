# Proposal: Page & View Lifecycle for SwiftDotNet

**Status:** **Partially shipped** — the hook surface, dispatch, and observer seam landed with
[DI Phase 1](./dependency-injection-proposal.md) on 2026-07-19. What remains is **real visibility
signalling** (native appear/disappear emitters), app/scene lifecycle, and `OnAppearAsync`.
**Author:** (proposal)
**Date:** 2026-07-18 (reconciled with shipped code 2026-07-19)
**Scope:** Give `View` subclasses — the screens ("pages") *and* any composite — a predictable set of
lifecycle callbacks, fired accurately on every backend (iOS/tvOS/macOS SwiftUI, Android Compose,
Windows WinUI, Linux GTK, Web/Blazor), without breaking the `new VStack(...)` authoring model or AOT.

---

## 0. Reconciliation — what shipped vs. what this doc proposed

DI Phase 1 delivered the lifecycle *surface* and *dispatch*, so several sections below are now history
rather than plan. Read this section first; it is authoritative where it disagrees with the rest.

### 0.1 Naming — the hooks were renamed

| This doc originally proposed | **Shipped** | Note |
|---|---|---|
| `OnLoad()` | `OnCreated()` | Fires after construction **and `[Inject]` fill**, before the first render. Not quite "entered the live tree" — see §0.3. |
| `OnAppear()` | `OnAppearing()` | |
| `OnDisappear()` | `OnDisappearing()` | |
| `OnUnload()` | `OnDestroyed()` | |
| `OnResume()` / `OnPause()` — *scene phase* | **not shipped** | ⚠️ These names were briefly used for *visibility* during implementation and then renamed to `OnAppearing`/`OnDisappearing`. If scene-phase hooks are added later they must **not** reuse them — see §6 and decision 7. |
| `OnAppearAsync(CancellationToken)` | **not shipped** | Still wanted; see §5.1. |

### 0.2 A second surface exists that this doc never anticipated

Lifecycle shipped with **two** surfaces, not one:

1. **View hooks** — `protected virtual` methods on `View`, for a view that cares about itself.
2. **`IViewLifecycle`** — a cross-cutting *observer*, registered in the container, called for **every**
   retained view:

```csharp
public interface IViewLifecycle
{
    void OnCreated(View view);
    void OnAppearing(View view);
    void OnDisappearing(View view);
    void OnDestroyed(View view);
}
```

`ViewLifecycleDispatcher` raises both. **Ordering:** setup (`OnCreated`, `OnAppearing`) runs
**observers → view**; teardown runs **view → observers**. Dependency injection is itself just a
registered observer (`InjectionViewLifecycle`), which is what guarantees `[Inject]` members are filled
before a view's own `OnCreated` runs.

This matters to the rest of the plan: **analytics/telemetry lifecycle consumers no longer need per-view
hooks at all**, which removes most of the pressure behind §4.4's opt-in detection machinery.

### 0.3 The honest gap — visibility is *not* yet real

**The shipped `OnAppearing` is raised by the host code path, not by actual visibility.** Today:

- `SwiftDotNetApp.CreateRoot()` raises `OnCreated` then `OnAppearing` at startup, unconditionally.
- `ViewScope.Dispose()` raises `OnDisappearing` then `OnDestroyed`.
- **Nothing** raises `OnDisappearing` when the app backgrounds, a page is covered, or a tab switches —
  because no backend emits visibility yet.

So the *contract* in §2 is defined and dispatched correctly, but the *signal* described in §4.1 is
entirely unimplemented. Treat every "fires when the view becomes visible" statement below as the target
semantic, not current behaviour. This is the single biggest remaining piece of work in this plan.

### 0.4 Dependencies that changed

- **`ISwiftDispatcher` is mostly moot.** `SwiftApp` already captures `SynchronizationContext.Current`
  at `Run`, coalesces bursts, and posts renders to it. §7 is rewritten accordingly.
- **The navigation service is paused.** Per-page lifecycle (this doc's Phase 2) now depends on
  [`navigation-service-plan.md`](./navigation-service-plan.md), which is not scheduled. `ViewScope` —
  the retained-view + `IServiceScope` pairing that per-page lifecycle needs — is built and tested but
  has no production caller yet.

---

## 1. Goal

SwiftUI has a rich lifecycle vocabulary that developers expect. The C# analogs, **as shipped**:

```csharp
public sealed partial class WeatherView : View
{
    [Inject] public partial IWeatherService Weather { get; }   // filled before OnCreated
    readonly State<string> _summary = State("…");
    IDisposable? _sub;

    protected override void OnCreated()       { }                        // ← once, after injection
    protected override void OnAppearing()     => _sub = Weather.Subscribe(…);   // ← .onAppear
    protected override void OnDisappearing()  => _sub?.Dispose();               // ← .onDisappear
    protected override void OnDestroyed()     { }                        // ← once, at teardown

    public override View Body => new Text(_summary.Value);
}
```

Still missing (§5.1): `OnAppearAsync(CancellationToken)` — the `.task` analog, auto-cancelled on
disappear.

The callbacks must fire **once per real transition**, in a deterministic order, on the UI thread —
not once per render pass (state changes re-render constantly; that must not re-fire `OnAppearing`).

## 2. The mapping we're mirroring

| SwiftUI | Our hook | Fires when | Status |
|---------|----------|-----------|--------|
| `init` of a `@StateObject` / view first inserted | `OnCreated()` | View constructed + injected, before first render | ✅ shipped |
| `.onAppear` | `OnAppearing()` | View becomes visible | 🧩 dispatched, but only from the host code path (§0.3) |
| `.task { }` | `OnAppearAsync(CancellationToken)` | With `OnAppearing`; token cancels on disappear | ❌ not shipped |
| `.onDisappear` | `OnDisappearing()` | View leaves the screen | 🧩 same caveat |
| view removed from tree / `deinit` | `OnDestroyed()` | View leaves the tree for good (once) | ✅ shipped (via `ViewScope.Dispose`) |
| `@Environment(\.scenePhase)` `.active` / `.background` | scene-phase hooks | App foregrounds/backgrounds while this view is live | ❌ not shipped (§6) |
| `.onChange(of:)` | `.OnChange(state, handler)` modifier | A bound `State<T>` changed | ❌ not shipped (§8) |

`OnAppearing`/`OnDisappearing` can fire **many times** for one instance (appear → tab away → appear
again). `OnCreated`/`OnDestroyed` fire **exactly once** each and bracket the instance's whole life. Same
contract as MAUI's `Page` and SwiftUI.

## 3. Why this isn't automatic — three structural facts

*(Unchanged and still accurate — this analysis is what drove the design.)*

| Fact (in code) | Consequence for lifecycle |
|----------------|---------------------------|
| `SwiftApp` re-renders the **whole tree** every state change, diffs against `_lastTree`, sends a patch. There is no per-view "did this appear?" signal — only node-tree structural diffs. | C# does not know when a view *appears*; only that the node tree changed shape. We need a source of truth for visibility. |
| Composite views recompute `Body` every render, and **inline child composites are transient**. Only *retained* instances (the root; pushed pages once navigation lands) survive. | An `OnAppearing` on a transient child has nowhere stable to live. Lifecycle for inline children is gated on **view-instance reconciliation**. |
| `TreeDiffer` emits `replace` / `updateProps` / `setChildren`. A view appearing shows up as a **`setChildren` on the parent** — no per-node insert/remove op. | We can't derive precise appear/disappear from the C# diff. The reliable signal is the **native host**. |

**Conclusion:** the accurate source of truth for appear/disappear is the *native side*, reported over
the existing event channel. C# owns the *instance* the callback targets — already true for the root,
and for retained pages once navigation lands.

## 4. Design: native-authoritative visibility, C#-owned instances

### 4.1 The signal — every backend already has it *(still to build — §0.3)*

| Backend | Appear signal | Disappear signal | Scene active/background |
|---------|---------------|------------------|-------------------------|
| iOS/tvOS/macOS (SwiftUI) | `.onAppear` | `.onDisappear` | `@Environment(\.scenePhase)` |
| Android (Compose) | `DisposableEffect` enter | `onDispose { }` | `LifecycleEventObserver` |
| Windows (WinUI) | `FrameworkElement.Loaded` | `Unloaded` | `Window.Activated` |
| Linux (GTK) | `map` signal | `unmap` signal | `GLib` app `activate` / focus |
| Web (Blazor) | first `OnAfterRenderAsync` for the element | component `Dispose` / node removed | `document visibilitychange` |

The shim change is local to each interpreter: when building the view for a node with lifecycle interest,
wrap it with the platform's appear/disappear emitter, which calls back with `(nodeId, phase)`.

### 4.2 The routing — node id → View instance

`SwiftApp` keeps node id → producing `View`, plus the last-known phase per id to enforce the
once-vs-many contract and de-dupe redundant native events. On a phase edge it calls
`ViewLifecycleDispatcher.Appearing(view, services)` / `.Disappearing(...)` — **the dispatcher already
exists and is tested**; only the map and the native emitters are missing.

```csharp
// SwiftApp — to build
static readonly Dictionary<string, View> _live = new();    // node id → producing view
static readonly Dictionary<string, Phase> _phase = new();  // node id → last phase seen
```

### 4.3 Where the instance is stable

- **Root** — retained today. ✅ Dispatch works now; needs only the visibility signal.
- **Navigation pages / tab items / sheet content** — become retained the moment the
  [navigation service](./navigation-service-plan.md) lands and pages live in its stack. `ViewScope`
  already provides the retained-view + scope + lifecycle pairing they need. ⏸ blocked on that paused plan.
- **Inline `Body` children** — transient. Needs view-instance reconciliation. Sequenced last.

### 4.4 Which nodes get emitters — **mostly unnecessary now**

The original design flagged nodes with `__lifecycle` and detected hook overrides (by reflection or a
marker interface) so the native cost was paid only where asked.

**Revisit before building this.** Only *retained* views receive lifecycle, and there are very few of
them (one root, plus one per navigation page). Flagging and override-detection is machinery for a
problem that does not exist at that scale — and `IViewLifecycle` observers (§0.2) already want events for
*every* retained view regardless of whether it overrides anything, so per-type detection would be wrong
for them. **Recommendation: always emit for retained/page nodes; drop the detection scheme entirely.**
It can come back if and when inline-child lifecycle (Phase 3) makes the node count large.

## 5. The `View` surface

**Shipped:**

```csharp
public abstract class View
{
    protected internal virtual void OnCreated()      { }   // once, after [Inject] fill
    protected internal virtual void OnAppearing()    { }   // may fire many times
    protected internal virtual void OnDisappearing() { }
    protected internal virtual void OnDestroyed()    { }   // once
}
```

(Declared `protected internal` so the dispatcher can raise them from within the library; an app
overrides them as `protected`.)

### 5.1 Still to add

```csharp
protected virtual Task OnAppearAsync(CancellationToken ct) => Task.CompletedTask;   // ~ .task
```

Fire-and-forget from the framework's view, with `ct` cancelled on the matching `OnDisappearing`, so an
`await`ing service stops cleanly — this handles the single most common lifecycle bug (a network call
completing after the screen is gone) by construction.

**Ordering guarantee** (as implemented, extended with the async hook):

```
OnCreated → OnAppearing → OnAppearAsync(started)          first show
OnDisappearing (cancels OnAppearAsync token)              hidden
OnAppearing → OnAppearAsync(restarted)                    shown again
OnDestroyed                                               removed for good; scope disposed after
```

Exceptions thrown from a hook should be caught, routed to an error handler (default: log), and never
crash the render loop. **Not yet implemented** — a throwing hook currently propagates.

## 6. App / scene lifecycle (the other axis) — not shipped

View lifecycle answers "is *this screen* visible." App lifecycle answers "is the *app* foregrounded."
Both are wanted, and they are distinct.

```csharp
public interface IAppLifecycle
{
    AppState State { get; }                     // Foreground | Inactive | Background
    event Action<AppState>? StateChanged;
    event Action? LowMemory;
    event Action? WillTerminate;
}
public enum AppState { Foreground, Inactive, Background }
```

| Backend | Foreground/Background source | Terminate | Low-memory |
|---------|------------------------------|-----------|------------|
| iOS/tvOS | `UIApplicationDelegate` `DidBecomeActive`/`DidEnterBackground` | `WillTerminate` | `DidReceiveMemoryWarning` |
| macOS | `NSApplicationDelegate` active/resign | `WillTerminate` | — |
| Android | `Activity` `OnResume`/`OnPause`; `ProcessLifecycleOwner` | `OnDestroy` | `OnTrimMemory` |
| Windows | `Window.Activated`; `AppInstance` | `Closed` | `MemoryManager.AppMemoryUsage*` |
| GTK | `Gtk.Application` focus/`activate` | `shutdown` | — |
| Web | `visibilitychange` / `pagehide` | `beforeunload` | — |

Each host base owns the process entry point, so wiring is a few lines per backend — and it now has an
obvious home: `SwiftDotNetApp`/the builder, registered like any other service.

> **Naming warning (§0.1).** The original draft called the per-view scene hooks `OnResume`/`OnPause`.
> Those names were used for *visibility* mid-implementation before being renamed, so reusing them for
> scene phase risks real confusion. **Recommendation: no per-view scene hooks at all — expose
> `IAppLifecycle` as an injectable service only** (views that care inject it and subscribe). It avoids
> the naming collision, avoids fanning out to every live view, and is a smaller surface. See decision 7.

## 7. Threading — mostly already handled *(corrected)*

The original text made lifecycle depend on the DI plan's `ISwiftDispatcher`. That dependency is
**largely obsolete**: `SwiftApp` already captures `SynchronizationContext.Current` at `Run`, coalesces
mutations, and posts renders to it, so state set from a background thread is already safe on any backend
whose `Run` happens on a thread with a synchronization context.

What remains:

- Lifecycle hooks fire on whatever thread raises them. Today that is the UI thread in every shipped path
  (`CreateRoot` at startup, `ViewScope` under host control).
- When native emitters land (§4.1) they call from the platform UI thread already.
- The genuine gap is **backends with no `SynchronizationContext` at `Run`** (audit needed — GTK, Skia,
  console hosts), plus async continuations in `OnAppearAsync`. Those need an explicit post; see the DI
  plan's §7 for the narrowed scope.

## 8. `.OnChange` and derived reactions — not shipped

`onChange(of:)` is lifecycle-adjacent and independent of everything above, since `State<T>` already knows
when it mutates:

```csharp
new Text(_name.Value).OnChange(_name, v => _validated.Value = Validate(v));
```

The subscription is owned by the producing view and disposed on `OnDestroyed`. It does **not** need
reconciliation — it binds to `State<T>` object identity, not a view instance — so it can ship at any time.

## 9. Interplay with the other milestones

| Milestone | Relationship |
|-----------|--------------|
| **DI proposal** | ✅ **Done.** Shipped the hook surface, `IViewLifecycle`, `ViewLifecycleDispatcher`, `ViewScope`, and the guarantee that `[Inject]` precedes `OnCreated`. |
| **Navigation service** | ⏸ **Paused.** Provides the retained page instances that make page-level lifecycle real. Push → `OnAppearing`; pop → `OnDisappearing` + `OnDestroyed` + scope disposal. |
| **View reconciliation** | Gates inline-child lifecycle. Same dependency as animations' transitions and DI's child injection. |
| **Animations plan** | `OnDisappearing` and remove-transitions are the same event; a page that animates out must not `OnDestroyed` until the exit animation completes. |

## 10. Testing

Instances are C#-owned and dispatch is an ordinary call, so lifecycle is asserted headlessly. This is
**already how the shipped tests work** ([`HostingTests.cs`](../tests/SwiftDotNet.Tests/HostingTests.cs)):

```csharp
var page = ViewScope.Create(app.Services, _ => new ScopedPageView());
page.Appearing();
page.Dispose();
// asserts observers saw created → appearing → disappearing → destroyed, in order
```

When native emitters land, add a fake that pushes phase events through the same routing.

## 11. AOT / trimming

- The override-detection reflection (§4.4) is **no longer needed** under the current recommendation.
- No change to `NodeJson` (still hand-rolled, zero-reflection). Visibility adds at most one bool prop and
  reuses the existing event channel — no new serialization surface.

## 12. Phased delivery *(revised)*

| Phase | Deliverable | Depends on | Status |
|-------|-------------|------------|--------|
| **1** | Hook surface on `View`; `IViewLifecycle` observers; `ViewLifecycleDispatcher` ordering; root `OnCreated`/`OnAppearing`; `ViewScope` teardown. | — | ✅ **Shipped** with DI Phase 1 |
| **1b** | **Native visibility emitters** per backend + `SwiftApp` node-id→view routing, so appear/disappear reflect reality (§0.3). `IAppLifecycle`. `.OnChange`. | — | ❌ **Next** — the real remaining work |
| **2** | Per-**page** lifecycle (nav stack entries, tab items, sheet/alert) via retained pages + per-page `IServiceScope`; `OnAppearAsync` + cancellation. | [Navigation service](./navigation-service-plan.md) (paused) | ⏸ Blocked |
| **3** | **Inline child** lifecycle via keyed view-instance reconciliation; animation-coordinated `OnDestroyed`. | Reconciliation, Animations Phase 3 | Deferred |

## 13. Decisions

| # | Decision | Outcome |
|---|----------|---------|
| 1 | Visibility source of truth: native-authoritative vs C#-derived | **Native-authoritative** — the diff genuinely can't tell which leaf appeared (§3). Still to build. |
| 2 | Opt-in detection: marker interface vs reflection | **Neither — drop it** (§4.4). Only retained views get lifecycle, so the node count doesn't justify it, and observers want every view anyway. |
| 3 | `OnAppearAsync` cancellation granularity | **Cancel on `OnDisappearing`** — the SwiftUI `.task` semantic. |
| 4 | Should teardown imply `Dispose`? | **Partly settled:** `ViewScope` disposes the view's *service scope* after `OnDestroyed`. Auto-calling `IDisposable.Dispose()` on the **view itself** is still open. *Rec: yes, for symmetry.* |
| 5 | Ship independently or fold into DI? | **Folded** — Phase 1 shipped with DI Phase 1, as recommended. |
| 6 | Scene-phase fan-out to every live view? | **Superseded by 7.** |
| 7 | Scene-phase surface *(new)* | **Open.** `IAppLifecycle` as an injectable service only (recommended), or also per-view hooks? Per-view hooks cannot reuse `OnResume`/`OnPause` — those names were transiently used for visibility (§0.1). |

---

## 14. Where the code is

| Piece | Location |
|---|---|
| `View` hooks | [`Core/View.cs`](../src/SwiftDotNet/Core/View.cs) |
| `IViewLifecycle` | [`Core/IViewLifecycle.cs`](../src/SwiftDotNet/Core/IViewLifecycle.cs) |
| Dispatch + ordering | [`Core/Hosting/ViewLifecycleDispatcher.cs`](../src/SwiftDotNet/Core/Hosting/ViewLifecycleDispatcher.cs) |
| Retained view + scope + teardown | [`Core/Hosting/ViewScope.cs`](../src/SwiftDotNet/Core/Hosting/ViewScope.cs) |
| Root raise path | [`Core/Hosting/SwiftDotNetApp.cs`](../src/SwiftDotNet/Core/Hosting/SwiftDotNetApp.cs) |
| Docs | [`docs/hosting-and-di.md`](../docs/hosting-and-di.md) |
