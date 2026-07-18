# Proposal: Page & View Lifecycle for SwiftDotNet

**Status:** Draft for review
**Author:** (proposal)
**Date:** 2026-07-18
**Scope:** Give `View` subclasses — the screens ("pages") *and* any composite — a predictable set of
lifecycle callbacks (appear / disappear / first-load / dispose / scene-phase), fired accurately on
every backend (iOS/tvOS/macOS SwiftUI, Android Compose, Windows WinUI, Linux GTK, Web/Blazor),
without breaking the `new VStack(...)` authoring model or AOT.

---

## 1. Goal

SwiftUI has a rich lifecycle vocabulary that developers expect. We want the C# analogs:

```csharp
public sealed class WeatherView : View
{
    readonly IWeatherService _weather;
    readonly State<string> _summary = State("…");
    IDisposable? _sub;

    public WeatherView(IWeatherService weather) => _weather = weather;

    protected override async Task OnAppearAsync(CancellationToken ct)   // ← SwiftUI .task
        => _summary.Value = await _weather.GetSummaryAsync(ct);         //   auto-cancelled on disappear

    protected override void OnAppear()  => _sub = _weather.Subscribe(…);// ← .onAppear
    protected override void OnDisappear() => _sub?.Dispose();           // ← .onDisappear

    protected override void OnResume()  { }                            // ← scenePhase == .active
    protected override void OnPause()   { }                            // ← scenePhase == .background

    public override View Body => new Text(_summary.Value);
}
```

The callbacks must fire **once per real transition**, in a deterministic order, on the UI thread —
not once per render pass (state changes re-render constantly; that must not re-fire `OnAppear`).

## 2. The mapping we're mirroring

| SwiftUI | Our hook | Fires when |
|---------|----------|-----------|
| `init` of a `@StateObject` / view first inserted | `OnLoad()` | View instance first enters the live tree (once) |
| `.onAppear` | `OnAppear()` | View becomes visible (push, tab-select, sheet present, first show) |
| `.task { }` | `OnAppearAsync(CancellationToken)` | With `OnAppear`; token cancels on disappear |
| `.onDisappear` | `OnDisappear()` | View leaves the screen (pop, tab-switch, dismiss) |
| view removed from tree / `deinit` | `OnUnload()` / `IDisposable.Dispose` | View instance leaves the tree for good (once) |
| `@Environment(\.scenePhase)` `.active` | `OnResume()` | App/scene returns to foreground *while this view is live* |
| `@Environment(\.scenePhase)` `.background` | `OnPause()` | App/scene goes to background *while this view is live* |
| `.onChange(of:)` | `.OnChange(state, handler)` modifier | A bound `State<T>` changed (already partly expressible; §8) |

`OnAppear`/`OnDisappear` can fire **many times** for one instance (appear → tab away → appear again).
`OnLoad`/`OnUnload` fire **exactly once** each and bracket the instance's whole life. This is the same
contract MAUI's `Page` (`Appearing`/`Disappearing` vs the ctor/`Unloaded`) and SwiftUI use.

## 3. Why this isn't automatic today — three structural facts

The current runtime decides the design (all grounded in `Core/`):

| Fact (in code) | Consequence for lifecycle |
|----------------|---------------------------|
| `SwiftApp` (`Core/SwiftApp.cs`) re-renders the **whole tree** every state change (`_root.ToNode(...)`), diffs against `_lastTree`, sends a patch. There is no per-view "did this appear?" signal — only node-tree structural diffs. | C# does not currently know when a view *appears*; it only knows the node tree changed shape. We need a source of truth for visibility. |
| Composite views recompute `Body` every render, and **inline child composites are transient** — `new WeatherView(...)` inside a parent's `Body` is a brand-new object each pass. Only the **root** instance is retained across renders. | An `OnAppear` on a transient child has nowhere stable to live: the instance that "appeared" is gone next render. Lifecycle for children is gated on **view-instance identity** — the same "per-view state ownership / reconciliation" milestone DI Phase 3, animations Phase 3, and keyed-`ForEach` all wait on. |
| `TreeDiffer` (`Core/TreeDiffer.cs`) emits `replace` / `updateProps` / `setChildren`. A view appearing or disappearing shows up as a **`setChildren` on the parent** (whole-subtree swap) — there is no per-node insert/remove op, so the diff can't by itself tell you *which* leaf appeared. | We can't derive precise appear/disappear purely from the C# diff. The reliable signal is the **native host**, which already knows real visibility. |

**Conclusion:** the accurate, low-effort source of truth for appear/disappear is the *native side*,
reported back over the existing event channel. C# owns the *instance* the callback targets — trivial
for the root and for retained navigation pages, deferred (behind reconciliation) for inline children.

## 4. Design: native-authoritative visibility, C#-owned instances

### 4.1 The signal — every backend already has it

Each backend's real UI knows exactly when a node is shown/hidden. We attach lifecycle emitters when
interpreting a node and forward them over the **existing `(nodeId, value)` event channel**, with the
value carrying the phase:

| Backend | Appear signal | Disappear signal | Scene active/background |
|---------|---------------|------------------|-------------------------|
| iOS/tvOS/macOS (SwiftUI) | `.onAppear` | `.onDisappear` | `@Environment(\.scenePhase)` |
| Android (Compose) | `DisposableEffect` enter | `onDispose { }` | `LifecycleEventObserver` (ON_RESUME/ON_PAUSE) |
| Windows (WinUI) | `FrameworkElement.Loaded` | `Unloaded` | `Window.Activated` (Code/Deactivated) |
| Linux (GTK) | `map` signal | `unmap` signal | `GLib` app `activate` / focus |
| Web (Blazor) | first `OnAfterRenderAsync` for the element | component `Dispose` / node removed | `document visibilitychange` (JS interop) |

The shim change is small and local to each interpreter: when it builds the view for a node that has
lifecycle interest (§4.4), wrap it with the platform's appear/disappear emitter that calls the C
callback with `("lifecycle", nodeId, phase)`.

### 4.2 The routing — node id → View instance

`SwiftApp` keeps a map from node id → the `View` instance that produced it, populated during render,
plus a per-id record of the last-known lifecycle phase so we can enforce the once-vs-many contract and
de-dupe:

```csharp
// SwiftApp
static readonly Dictionary<string, View> _live = new();       // node id → producing view
static readonly Dictionary<string, Phase> _phase = new();      // node id → last phase seen

internal static void OnLifecycle(string nodeId, Phase incoming)
{
    if (!_live.TryGetValue(nodeId, out var view)) return;      // stale id (already unloaded)
    var prev = _phase.GetValueOrDefault(nodeId, Phase.None);
    if (prev == incoming) return;                              // de-dupe redundant native events
    _phase[nodeId] = incoming;
    Dispatcher.Post(() => Lifecycle.Dispatch(view, prev, incoming));  // UI thread, §7 of DI plan
}
```

`Lifecycle.Dispatch` translates a phase edge into the right callbacks and manages the async
`OnAppearAsync` `CancellationTokenSource` (created on appear, cancelled+disposed on disappear).

### 4.3 Where the instance is stable — the phasing falls out of the architecture

The callback needs a *retained* `View` to target. Stability differs by where the view sits:

- **Root** — retained today (`SwiftApp._root`). ✅ Lifecycle works **in Phase 1** with zero new
  infrastructure beyond the emitter + routing.
- **Navigation pages, tab items, sheet/alert content** — become retained instances the moment
  navigation moves to the `INavigator` stack model from the DI plan (§13 there): pushed pages live in
  `_stack` until popped. So their lifecycle is **Phase 2**, riding on the navigation-service work.
  This is the sweet spot — a "page" is exactly a retained stack/tab/modal entry, which is what most
  apps mean by "page lifecycle."
- **Inline `Body` children** (e.g. `new Rating(...)` mid-tree) — transient today. Their `OnAppear`
  needs view-instance reconciliation (keyed identity across renders). **Phase 3**, sequenced behind
  the same reconciliation milestone DI/animations already defer to.

This is why the plan is naturally three-phased and why **"page" lifecycle (roots + nav/tab/modal
entries) is fully deliverable before general per-view lifecycle** — pages are the retained instances.

### 4.4 Which nodes get emitters

Emitting appear/disappear for *every* node is wasteful. A view opts in when it overrides any hook. We
detect that cheaply once per type and flag the node:

```csharp
// View.cs — computed once per concrete type, cached
bool WantsLifecycle => LifecycleReflection.HasOverrides(GetType());   // OnAppear/OnDisappear/…

// during BuildNode wrapping (ToNode)
if (WantsLifecycle) node.Props["__lifecycle"] = true;                 // shim adds the emitter
```

The shim only wraps nodes carrying `__lifecycle`, so the native cost is paid only where a developer
actually asked for a callback. (Container "page" nodes — `NavigationStack` entries, `TabView` tabs,
`Sheet` content — always carry it, so page-level hooks work even on a plain-`VStack` body.)

## 5. The `View` surface

```csharp
public abstract class View
{
    // Once-per-instance bracketing
    protected virtual void OnLoad()   { }   // entered the live tree
    protected virtual void OnUnload() { }   // left the tree for good (also: Dispose if IDisposable)

    // Visibility (may fire many times)
    protected virtual void OnAppear()    { }
    protected virtual void OnDisappear() { }
    protected virtual Task OnAppearAsync(CancellationToken ct) => Task.CompletedTask;  // ~ .task

    // Scene phase (only while this view is live)
    protected virtual void OnResume() { }   // foreground/active
    protected virtual void OnPause()  { }   // background/inactive
}
```

**Ordering guarantees** (enforced in `Lifecycle.Dispatch`, matching SwiftUI/MAUI):

```
OnLoad → OnAppear → OnAppearAsync(started)                     first show
       … OnPause / OnResume may interleave while visible …
OnDisappear (cancels OnAppearAsync token)                      hidden
OnAppear → OnAppearAsync(restarted)                            shown again (tab back)
       …
OnUnload → Dispose                                             removed for good
```

- `OnAppearAsync` is fire-and-forget from the framework's view; its `ct` is cancelled on the matching
  `OnDisappear`, so `await`ing services stop cleanly — the single most common lifecycle bug (a network
  call completing after the screen is gone) is handled by construction.
- Exceptions thrown from a hook are caught, routed to an `ILifecycleErrorHandler` (default: log), and
  never crash the render loop.

## 6. App / scene lifecycle (the other axis)

View lifecycle answers "is *this screen* visible." App lifecycle answers "is the *app* foregrounded."
They're distinct and both wanted. We surface app lifecycle as an injectable service **and** as the
per-view `OnResume`/`OnPause` (which is just the app signal filtered to currently-live views).

```csharp
public interface IAppLifecycle
{
    AppState State { get; }                     // Foreground | Background | Inactive
    event Action<AppState>? StateChanged;
    event Action? LowMemory;                    // iOS didReceiveMemoryWarning / Android onTrimMemory
    event Action? WillTerminate;                // last-chance save
}
public enum AppState { Foreground, Inactive, Background }
```

Each host base already owns the process entry point, so wiring is a few lines per backend with **no
change to the host-base shape** — same pattern the DI dispatcher uses:

| Backend | Foreground/Background source | Terminate | Low-memory |
|---------|------------------------------|-----------|------------|
| iOS/tvOS | `UIApplicationDelegate` `DidBecomeActive`/`DidEnterBackground` | `WillTerminate` | `DidReceiveMemoryWarning` |
| macOS | `NSApplicationDelegate` active/resign | `WillTerminate` | — |
| Android | `Activity` `OnResume`/`OnPause`/`OnStop`; `ProcessLifecycleOwner` | `OnDestroy` | `OnTrimMemory` |
| Windows | `Window.Activated`; `AppInstance` | `Closed` | `MemoryManager.AppMemoryUsage*` |
| GTK | `Gtk.Application` focus/`activate` | `shutdown` | — |
| Web | `visibilitychange` / `pagehide` (JS interop) | `beforeunload` | — |

`IAppLifecycle` fires `StateChanged`; `SwiftApp` fans that out to every currently-live view's
`OnResume`/`OnPause` (walk `_live`, on the UI thread).

## 7. Threading

Every hook fires on the UI thread via the `ISwiftDispatcher` introduced in the DI proposal (§7 there).
Native emitters call the C callback from the platform's UI thread already, but async continuations in
`OnAppearAsync` and background service callbacks driving `IAppLifecycle` may not be — so all dispatch
goes through `Dispatcher.Post`. This makes lifecycle safe to use with the async services DI enables,
and shares one mechanism with `RequestRender` marshaling. **Lifecycle depends on the DI plan's Phase 2
dispatcher**; until then hooks fire synchronously on the reporting thread (fine for the root, which is
always driven from the UI thread).

## 8. `.OnChange` and derived reactions

`onChange(of:)` is lifecycle-adjacent and cheap to add independently, since `State<T>` already knows
when it mutates. Add a subscription channel to `State<T>` and a modifier:

```csharp
public sealed class State<T>
{
    event Action<T>? _changed;                         // opt-in; null-cost when unused
    // in the setter, after RequestRender():
    //   _changed?.Invoke(_value);
    internal IDisposable Observe(Action<T> h) { _changed += h; return new Unsub(() => _changed -= h); }
}

// usage
new Text(_name.Value).OnChange(_name, v => _validated.Value = Validate(v));
```

The subscription is owned by the producing view and disposed on `OnUnload`, so it participates in the
same lifecycle bookkeeping. This ships in **Phase 1** (root/any view that holds the `State` field) —
it does not need reconciliation because it binds to a `State<T>` object identity, not a view instance.

## 9. Interplay with the other milestones

Lifecycle sits at the confluence of the pending milestones and is a good forcing function for them:

| Milestone | Relationship |
|-----------|--------------|
| **DI proposal** | Shares the `ISwiftDispatcher` (§7) and the retained-instance model. `OnAppearAsync` is where injected async services actually run; DI already lists `OnAppear`/`OnDisappear` as its Phase 2. Lifecycle should land **with** DI Phase 2, not separately. |
| **Navigation service** (DI §13) | Provides the retained page instances that make page-level `OnAppear`/`OnDisappear` correct. Push → target page `OnAppear`; pop → `OnDisappear` + `OnUnload`. The navigator is the C# authority; native appear/disappear events reconcile against it. |
| **View reconciliation / per-view state** | Gates Phase 3 (inline-child lifecycle). Same dependency as animations' transitions and DI's child ctor injection. |
| **Animations plan** | `OnDisappear` and remove-transitions are the same event; a page that animates out must not `OnUnload` until the exit animation completes. Coordinate: `OnUnload` fires on animation-complete, not on diff. |

## 10. Testing

Because visibility is delivered as ordinary events and instances are C#-owned, lifecycle is asserted
headlessly with no device:

```csharp
var view = new WeatherView(new FakeWeather());
var app = TestHost.Mount(view);                 // renders, registers node ids

app.Emit(view, Phase.Appear);
Assert.Equal("sunny", view.Summary);            // OnAppearAsync ran
app.Emit(view, Phase.Disappear);
Assert.True(view.LastToken.IsCancellationRequested);   // token cancelled on disappear

app.SetAppState(AppState.Background);
Assert.True(view.Paused);                        // OnPause fanned out
```

This composes with the existing Core node-diff test harness.

## 11. AOT / trimming

- `LifecycleReflection.HasOverrides` (§4.4) uses `MethodInfo.DeclaringType` comparison, run **once per
  concrete type** and cached — off the render hot path. Annotate with `DynamicallyAccessedMembers` for
  the specific methods, or (trim-clean alternative) let views advertise interest via an
  `ILifecycleAware` marker interface instead of reflection. *Recommendation: ship the marker-interface
  path as the AOT-safe default, reflection as convenience.*
- No change to `NodeJson` (still hand-rolled, zero-reflection). Lifecycle adds one bool prop
  (`__lifecycle`) and reuses the existing event channel — no new serialization surface.

## 12. Phased delivery

| Phase | Deliverable | Depends on | Risk |
|-------|-------------|------------|------|
| **1** | `View` hooks defined; **root-level** `OnLoad`/`OnAppear`/`OnDisappear`/`OnUnload` via native emitters + `SwiftApp` routing; `.OnChange` + `State<T>.Observe`; `IAppLifecycle` + `OnResume`/`OnPause` wired in host bases. Synchronous dispatch. | — | Low. Additive; root is already retained. |
| **2** | `OnAppearAsync` + `CancellationToken` tied to disappear; per-**page** lifecycle (nav stack entries, tab items, sheet/alert) via the `INavigator` retained stack; UI-thread dispatch via `ISwiftDispatcher`. | DI Phase 2 (dispatcher), Navigation service | Medium (touches every backend, small each). |
| **3** | **Inline child** lifecycle via keyed view-instance reconciliation; animation-coordinated `OnUnload` (fires on exit-animation complete). | Reconciliation milestone, Animations Phase 3 | Higher; sequenced last. |

Phase 1 already delivers "my screen gets `OnAppear`/`OnDisappear`/`OnResume`/`OnPause`" for the root
and app-scene axis. Phase 2 delivers real **page** lifecycle across navigation. Phase 3 generalizes to
every view.

## 13. Decisions I need from you

1. **Visibility source of truth: native-authoritative (recommended) vs C#-derived.** Emit
   appear/disappear from each backend's real UI over the event channel (accurate for nav/tab/sheet,
   small per-backend shim), or try to infer visibility from the C# `TreeDiffer`? *Recommendation:
   native-authoritative — the diff genuinely can't distinguish which leaf appeared (§3), and the native
   signal is free and exact.*
2. **Opt-in detection: `ILifecycleAware` marker interface vs reflection.** Marker is AOT-clean and
   explicit; reflection (`HasOverrides`) is zero-ceremony. *Recommendation: ship both — marker as the
   trim-safe default, reflection for convenience — mirroring the DI plan's factory-vs-`ActivatorUtilities`
   stance.*
3. **`OnAppearAsync` cancellation granularity.** One token cancelled on `OnDisappear` (matches SwiftUI
   `.task`), or a token that survives tab-away and only cancels on `OnUnload`? *Recommendation: cancel
   on `OnDisappear` — it's the SwiftUI semantic and prevents the "response arrives after the screen is
   gone" bug.*
4. **Should `OnUnload` imply `Dispose`?** Auto-call `IDisposable.Dispose()` on unload for views that
   implement it, or keep disposal explicit? *Recommendation: auto-dispose on `OnUnload` — it's the
   least-surprise behavior and pairs with per-page `IServiceScope` disposal from DI §13.4.*
5. **Ship independently or fold into DI Phase 2?** Lifecycle shares the dispatcher, retained-instance
   model, and navigation service with DI. *Recommendation: land Phase 1 lifecycle alongside DI Phase 1
   (root hooks need nothing new), and Phase 2 lifecycle *with* DI Phase 2 so they share the dispatcher
   and navigator rather than duplicating them.*
6. **Scene-phase fan-out cost.** Fan `IAppLifecycle` state changes to every live view's
   `OnResume`/`OnPause` (walk `_live` each transition), or require views to subscribe to `IAppLifecycle`
   explicitly? *Recommendation: fan out — it's a rare event (foreground/background), the walk is cheap,
   and it matches SwiftUI's ambient `scenePhase`.*
