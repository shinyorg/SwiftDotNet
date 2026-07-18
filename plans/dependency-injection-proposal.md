# Proposal: Dependency Injection for SwiftDotNet

**Status:** Draft for review
**Author:** (proposal)
**Date:** 2026-07-18
**Scope:** Let application services (repositories, HTTP clients, loggers, app state) reach `View`
subclasses — the "ContentViews" — on every backend (iOS/macOS/tvOS SwiftUI, Android Compose, Linux
GTK, Windows WinUI, Web/Blazor), without breaking the `new VStack(...)` authoring model or AOT.

---

## 1. Goal

Today a screen is a plain object:

```csharp
public sealed class ContentView : View { /* ... */ }
// AppRoot.Create() => new ContentView();
```

We want a developer to be able to write:

```csharp
public sealed class WeatherView : View
{
    readonly IWeatherService _weather;          // ← injected
    readonly State<string> _summary = State("…");

    public WeatherView(IWeatherService weather) => _weather = weather;

    protected override async void OnAppear()     // proposed lifecycle hook
        => _summary.Value = await _weather.GetSummaryAsync();

    public override View Body => new Text(_summary.Value);
}
```

…and register `IWeatherService` once, in one place, and have it flow to the view — including views
nested deep in the tree — with correct lifetimes and thread-safety.

## 2. Why this isn't automatic today

The current model has four properties that decide the design:

| Fact (in code) | Consequence for DI |
|----------------|--------------------|
| Views are POCOs built with `new` — the root in `AppRoot.Create()`, children inline inside `Body` (`new Rating(_rating)`, `new VStack(...)`). | Nothing constructs views *through* a container, so constructor injection isn't wired anywhere yet. |
| `SwiftApp` (`Core/SwiftApp.cs`) is a **static** runtime holding one `_root` and re-rendering it. | There's a single, app-global place to hang an `IServiceProvider`. That's good — one container per app — but it means the provider is effectively ambient, not passed as an argument. |
| The **root** view instance is kept alive across renders; **child** composite views created in `Body` are *transient* (rebuilt every render pass). This is the same limitation tracked by the "per-view local state ownership" milestone. | Constructor injection is clean for the **root** immediately. For **children**, until view-instance reconciliation lands, a child is a fresh object each render, so ctor injection there means "re-resolve every render" — fine for singletons, wrong for scoped/stateful. We need a path that works *now* and gets better when reconciliation lands. |
| `State<T>.Value` setter calls `SwiftApp.RequestRender()` **synchronously**, which calls `_bridge.Render(json)`. | A service that updates UI from a background thread would call the native bridge off the UI thread — unsafe on every backend. DI brings async services, so we must add UI-thread marshaling as part of this work. |

## 3. Design principles

1. **One container per app, created at the composition root** (each platform's entry point). Use
   `Microsoft.Extensions.DependencyInjection` — it's the .NET standard, ships for every TFM we target,
   and is what consumers already know.
2. **Don't break the DSL.** `new VStack(new Text(...))` must keep working. We will *not* force every
   child view through a factory.
3. **Support both directions.** Views *pull* services (injection); services *push* updates back (a
   background result must be able to trigger a safe re-render).
4. **Minimal, familiar surface.** Prefer `builder.Services.AddSingleton<…>()` and constructor
   injection over bespoke concepts.
5. **AOT/trim-safe by default.** The library is `IsTrimmable=true` and iOS runs AOT; the happy path
   must not depend on reflection that the trimmer can't see.

## 4. Proposed API

### 4.1 A host/builder (composition root)

Mirror `Host.CreateApplicationBuilder`, scaled down:

```csharp
public sealed class SwiftDotNetAppBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();
    public SwiftDotNetApp Build();     // builds the provider, returns the app handle
}

public sealed class SwiftDotNetApp
{
    public IServiceProvider Services { get; }
    public TRoot CreateRoot<TRoot>() where TRoot : View;   // resolves the root from the container
}

public static class SwiftDotNetHostApp
{
    public static SwiftDotNetAppBuilder CreateBuilder();
}
```

Usage (shared, platform-neutral — lives next to `AppRoot`):

```csharp
public static class AppRoot
{
    static readonly SwiftDotNetApp _app = Build();

    static SwiftDotNetApp Build()
    {
        var b = SwiftDotNetHostApp.CreateBuilder();
        b.Services.AddSingleton<IWeatherService, WeatherService>();
        b.Services.AddSingleton<ContentView>();          // the root screen
        return b.Build();
    }

    public static View Create() => _app.CreateRoot<ContentView>();
}
```

`AppRoot.Create()` is already the single registration point (from the sample consolidation), so this
is a natural home. Every platform host base (`SwiftDotNetAppDelegate`, `SwiftDotNetActivity`,
`SwiftDotNetApplication`, the Blazor `AppRoot`, GTK `Program`) already funnels through `CreateRoot()` —
**no per-platform change is required** beyond what's in §6.

### 4.2 Reaching services from *any* view — `Service<T>()`

Because inline children aren't container-created, add a resolver to the `View` base so the DSL is
untouched:

```csharp
public abstract class View
{
    /// <summary>Resolve a required service from the running app's container.</summary>
    protected TService Service<TService>() where TService : notnull
        => SwiftServices.Require<TService>();

    /// <summary>Resolve an optional service (null if unregistered).</summary>
    protected TService? OptionalService<TService>()
        => SwiftServices.Current is { } sp ? (TService?)sp.GetService(typeof(TService)) : default;
}
```

backed by an ambient provider set when the app runs:

```csharp
public static class SwiftServices
{
    public static IServiceProvider? Current { get; internal set; }
    public static T Require<T>() where T : notnull =>
        (Current ?? throw new InvalidOperationException("No SwiftDotNet service provider is running."))
            .GetRequiredService<T>();
}
```

`SwiftApp.Run` gains an overload that stashes the provider:

```csharp
public static void Run(View root, IBridge bridge, IServiceProvider? services = null)
{
    SwiftServices.Current = services;
    // …existing body…
}
```

This gives us **both** styles from day one:

- **Constructor injection** for the root (and any subview you choose to resolve explicitly) — the
  "clean" path.
- **`Service<T>()`** for inline children where writing a constructor would fight the DSL — e.g. a
  small custom control that needs an `ILogger`.

> `Service<T>()` is a scoped service *locator*. That's a deliberate, pragmatic trade — it's how many
> retained-mode UI frameworks expose DI to leaf widgets — and it stays fully testable because the
> provider is swappable (§9). Teams that dislike locators can simply resolve the root and pass
> services down through constructors; the framework supports both.

### 4.3 Optional sugar — `[Inject]` property injection

For views that the container *does* create (roots today; any view once §8 lands), support attribute
injection so you don't hand-write assignments:

```csharp
public sealed class ContentView : View
{
    [Inject] public IWeatherService Weather { get; set; } = default!;
}
```

Filled by `ActivatorUtilities` + a small post-construct pass during `CreateRoot`/resolution. This is
opt-in; constructor injection remains the recommended default.

### 4.4 Push direction — services that update the UI safely

Two supported patterns, both requiring **UI-thread marshaling** (§7):

1. **Shared observable state.** A service exposes/mutates a `State<T>` (or a small observable store)
   that views bind to. Setting `.Value` schedules a re-render. We route that through the dispatcher so
   it's safe from any thread.
2. **Injected dispatcher.** Services take `ISwiftDispatcher` and call `Post(() => state.Value = …)`
   when async work completes.

```csharp
public interface ISwiftDispatcher { void Post(Action work); }   // marshals to the UI thread
```

## 5. What actually changes (surface summary)

| Component | Change |
|-----------|--------|
| `Core/SwiftApp.cs` | `Run` overload taking `IServiceProvider?`; store it; route `RequestRender` through a dispatcher. |
| `Core/View.cs` | Add `Service<T>()` / `OptionalService<T>()`; (Phase 2) `[Inject]` support; (Phase 2) `OnAppear`/`OnDisappear` lifecycle hooks. |
| **New** `Core/SwiftServices.cs`, `SwiftDotNetAppBuilder`, `SwiftDotNetApp`, `ISwiftDispatcher`, `InjectAttribute`. | The builder + ambient accessor + dispatcher contract. |
| `SwiftDotNet.csproj` | Add `Microsoft.Extensions.DependencyInjection` (+`.Abstractions`). netstandard2.0 packages, load on every TFM. |
| Per-platform `Platforms/*` | Provide an `ISwiftDispatcher` implementation (a few lines each — see §7). No change to the host-base *shape*. |
| `AppRoot.cs` (sample) | Build the container, register services, resolve the root. |

The public authoring model for existing apps is **unchanged** — DI is additive. An app that registers
nothing behaves exactly as today.

## 6. Composition root per platform

The container is built once, at the same place each platform creates the root. Because we centralized
hosting into the library bases, this is just "call `AppRoot.Create()` (which resolves from the
container) and pass the provider to `Run`". The host bases forward the provider:

- **iOS/tvOS/macOS** (`SwiftDotNetAppDelegate`): `CreateRoot()` → provider available via
  `SwiftServices`; pass it into `SwiftDotNetHost.CreateRootController` → `SwiftApp.Run(root, bridge, sp)`.
- **Android** (`SwiftDotNetActivity`): same, through `CreateRootView`.
- **Windows** (`SwiftDotNetApplication`): same, through `CreateRootElement`.
- **Web/Blazor**: Blazor already *has* a container. We can either (a) reuse it — pass
  `builder.Services.BuildServiceProvider()` / the component's injected `IServiceProvider` straight into
  `SwiftServices`, so a single registration list serves both Blazor and SwiftDotNet — or (b) keep a
  dedicated SwiftDotNet container. Reuse is preferred on Web.
- **Linux/GTK**: build the container in `Program.Main` before `SwiftApp.Run`.

Concretely, the host bases gain one protected seam so an app can register services without rewriting
`AppRoot`:

```csharp
public abstract class SwiftDotNetAppDelegate : UIApplicationDelegate
{
    protected abstract View CreateRoot();
    protected virtual void ConfigureServices(IServiceCollection services) { }  // ← optional hook
}
```

## 7. Threading (required, not optional)

`RequestRender` must marshal to the UI thread so async services can update state from anywhere:

```csharp
// SwiftApp
static ISwiftDispatcher? _dispatcher;
internal static void RequestRender()
{
    if (_dispatcher is { } d) d.Post(RenderNow);
    else RenderNow();          // already on UI thread / no dispatcher registered
}
```

Per-backend dispatcher (each a handful of lines, registered during host bootstrap):

| Backend | Marshal via |
|---------|-------------|
| iOS/tvOS/macOS | `CoreFoundation.DispatchQueue.MainQueue.DispatchAsync` |
| Android | `Handler(Looper.MainLooper!).Post(...)` |
| WinUI | `DispatcherQueue.TryEnqueue(...)` |
| GTK | `GLib.Functions.IdleAdd(...)` |
| Web/Blazor | `ComponentBase.InvokeAsync(...)` |

This also fixes a latent bug independent of DI: today any off-thread `State` mutation is unsafe.

## 8. Interplay with the per-view state / reconciliation milestone

Full **constructor injection for child views** wants child view *instances* to be stable across
renders — which is exactly the "per-view local state ownership" milestone (keyed reconciliation of
child `View` instances). Sequencing:

- **Now** (Phase 1): root via container; everything else via `Service<T>()`. Correct for singletons.
- **After reconciliation**: children resolved from the container too, so `[Inject]` and scoped
  lifetimes (e.g. a per-navigation-page scope) become clean. DI and reconciliation are separable but
  DI's Phase 3 depends on reconciliation for scoped-per-view lifetimes.

## 9. Testing

The ambient provider is swappable, so screens can be rendered headlessly with fakes:

```csharp
var sp = new ServiceCollection().AddSingleton<IWeatherService, FakeWeather>().BuildServiceProvider();
SwiftServices.Current = sp;
var node = new WeatherView(sp.GetRequiredService<IWeatherService>()).ToNode(new RenderContext(), "0");
// assert on the node tree — no device, no bridge
```

This composes with the existing Core test harness that already diffs node trees.

## 10. AOT / trimming

- **Recommended:** explicit factory registration avoids reflection and is fully AOT-safe —
  `services.AddSingleton<ContentView>(sp => new ContentView(sp.GetRequiredService<IWeatherService>()))`.
- `ActivatorUtilities.CreateInstance` (used by `AddSingleton<TRoot>()` without a factory and by
  `[Inject]`) works under iOS AOT today (MAUI relies on it) but can surface trim warnings; we'll
  annotate the resolve entry points with `[RequiresUnreferencedCode]`/`DynamicallyAccessedMembers` and
  document the factory form as the trim-clean default.
- No change to `NodeJson` (still hand-rolled, zero-reflection). DI reflection is confined to
  construction, never the render/serialize hot path.

## 11. Phased delivery

| Phase | Deliverable | Risk |
|-------|-------------|------|
| **1** | Builder + `SwiftServices` ambient provider + `SwiftApp.Run(…, sp)` + `View.Service<T>()` + root constructor injection. `Microsoft.Extensions.DependencyInjection` referenced. | Low. Additive; no DSL change. |
| **2** | `ISwiftDispatcher` + per-backend dispatchers + thread-safe `RequestRender`; `[Inject]` sugar; `OnAppear`/`OnDisappear`. | Medium (touches every backend, but small each). |
| **3** | Container-created child views + scoped lifetimes (per-navigation scope), riding on view-instance reconciliation. | Higher; sequenced after reconciliation. |

Phase 1 alone satisfies "my services can get to the ContentViews." Phases 2–3 make it idiomatic and safe under async.

## 12. Worked example (end state, Phase 1 + 2)

```csharp
// App setup (shared AppRoot)
var b = SwiftDotNetHostApp.CreateBuilder();
b.Services.AddSingleton<IGreetingService, GreetingService>();
b.Services.AddSingleton<ContentView>();
var app = b.Build();

// A screen pulls a service via ctor injection and updates on appear
public sealed class ContentView : View
{
    readonly IGreetingService _greeter;
    readonly State<string> _greeting = State("…");

    public ContentView(IGreetingService greeter) => _greeter = greeter;

    protected override async void OnAppear() =>
        _greeting.Value = await _greeter.GreetAsync();   // async result marshals to UI thread

    public override View Body =>
        new VStack(
            new Text(_greeting.Value).Font(Font.LargeTitle),
            // a nested custom control reaches a service without a constructor:
            new AuditButton("Refresh", () => _greeting.Value = "…")
        );
}

public sealed class AuditButton : View
{
    readonly string _title; readonly Action _tap;
    public AuditButton(string title, Action tap) { _title = title; _tap = tap; }
    public override View Body =>
        new Button(_title, () => { Service<IAudit>().Log(_title); _tap(); });
}
```

## 13. Navigation service (DI-driven, imperative)

### 13.1 The gap today

Navigation is currently **declarative-only** (`Core/Views/Navigation.cs`):

- `NavigationStack(root)` renders a single root; there is no mutable page stack.
- `NavigationLink(label, destination)` hard-codes its destination **inline**, in `Body`. The
  destination is constructed on every render pass whether or not it's visible.
- `Sheet` / `Alert` are driven by a `State<bool>` the view owns.

So a **service or view-model cannot navigate.** There is no `Push`, no `Pop`, no "go to the login
screen after this async sign-in completes", no result-returning dialog. Once DI lands and services
start reaching views (§4), the very next thing an app wants is for those services to *move the user
around* — which today is impossible without threading a `State<bool>` through the view tree by hand.

A navigation **service** is the natural companion to DI: it's an injectable singleton that owns the
navigation stack + modal state, mutates it imperatively, and triggers a safe re-render through the
same dispatcher §7 introduces.

### 13.2 The contract

```csharp
public interface INavigator
{
    // Stack navigation
    Task PushAsync(View destination);
    Task<T?> PushAsync<T>() where T : View;             // resolve destination from the container
    Task PopAsync();
    Task PopToRootAsync();

    // Modal presentation
    Task PresentAsync(View content, PresentationStyle style = PresentationStyle.Sheet);
    Task DismissAsync();

    // Result-returning dialogs (the async escape hatch services actually want)
    Task<bool> ConfirmAsync(string title, string message, string ok = "OK", string cancel = "Cancel");
    Task AlertAsync(string title, string message);

    // Introspection (for tests, back-button enablement, deep-link restore)
    IReadOnlyList<View> Stack { get; }
    int Depth { get; }
    event Action? Changed;
}

public enum PresentationStyle { Sheet, FullScreen, Popover }
```

Registered by the framework, resolved anywhere:

```csharp
public sealed class SignInService(INavigator nav, IAuth auth)
{
    public async Task SignInAsync(string user, string pw)
    {
        if (await auth.LoginAsync(user, pw))
            await nav.PushToRootThenAsync<HomeView>();   // moves the UI, from a service, off the UI thread-safe
        else
            await nav.AlertAsync("Sign-in failed", "Check your credentials.");
    }
}
```

### 13.3 How it renders — `NavigationStack` reads the navigator

The key change: `NavigationStack` stops rendering only its static root and instead renders the
**navigator's current stack** as ordered children. The host animates the diff — a child appended =
push; a child removed = pop — which `TreeDiffer` already expresses as child add/remove patches, so
no new patch primitive is needed.

```csharp
public sealed class NavigationStack : View
{
    readonly View _root;
    public NavigationStack(View root) => _root = root;

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var nav = (Navigator)ctx.Services.GetRequiredService<INavigator>();
        nav.BindRoot(_root);                       // root is stack[0]; idempotent

        var node = ctx.NewNode("NavigationStack", path);
        var pages = nav.Stack;                      // [root, pushed1, pushed2, …]
        for (var i = 0; i < pages.Count; i++)
            node.Children.Add(pages[i].ToNode(ctx, $"{path}.{i}"));
        node.Props["depth"] = pages.Count;
        ctx.RegisterAction(node.Id, v =>            // hardware/gesture back from the host
        {
            if (v == "pop") _ = nav.PopAsync();
        });
        return node;
    }
}
```

- The `Navigator` implementation holds `List<View> _stack` + modal state, and on every mutation calls
  `SwiftApp.RequestRender()` **through the dispatcher** (§7) so a service can push from a background
  continuation safely.
- The host-side back gesture (iOS swipe, Android back button, GTK/WinUI back) reports `"pop"` up the
  existing event channel so the C# stack and the native stack stay in sync — the navigator is the
  single source of truth; the host mirrors it.
- `NavigationLink` stays as declarative sugar and is re-expressed as `Button(label, () =>
  Service<INavigator>().PushAsync(destination))`, so the two models can't drift. Destinations passed
  to a link are now built **lazily on tap**, not every render — a latent efficiency win.

### 13.4 DI angle — typed routes and scoped-per-page lifetimes

This is where navigation and the container reinforce each other:

- **`PushAsync<T>()`** resolves the destination view from the container (§4), so a pushed screen gets
  constructor injection for free — the reconciliation-independent win, because a pushed page *is* a
  stable retained instance (it lives in `_stack` until popped), unlike inline `Body` children.
- **Per-page scope (Phase 3).** Each push can open an `IServiceScope`; the page and its
  scoped services (a per-screen view-model, an `HttpClient`, an editing `DbContext`) live exactly as
  long as the page is on the stack and are disposed on pop. This is the concrete, motivating use case
  for the scoped lifetimes §8 defers to reconciliation — navigation gives them a natural boundary
  *without* needing full child-view reconciliation, because the stack already retains instances.
- **Optional route registry** for string/deep-link navigation:
  `services.AddRoute<DetailView>("detail")` → `nav.PushAsync("detail")`, enabling URL restore on Web
  and Android intent deep-links. AOT-safe when registered with an explicit factory (§10).

### 13.5 Per-backend mapping

Each backend already has a native navigation primitive; the navigator drives it via the stack diff:

| Backend | Native container | Push / pop |
|---------|------------------|------------|
| iOS/tvOS/macOS | `UINavigationController` / SwiftUI `NavigationStack` path | push/pop view controller |
| Android | Fragment back-stack / Compose `NavHost` | add/remove destination |
| WinUI | `Frame` | `Navigate` / `GoBack` |
| GTK | `Gtk.Stack` + header back | add/remove named child |
| Web/Blazor | history API + component swap | push state / `NavigateTo` |

Modal presentation (`PresentAsync`/dialogs) reuses the existing `Sheet`/`Alert` node types — the
navigator just owns the `bool`/content instead of the view, so no new host-side node is required for
Phase 2.

### 13.6 Testing

The navigator is an ordinary injectable, so navigation is asserted headlessly with no host:

```csharp
var nav = new Navigator(dispatcher: Immediate);
var sp = new ServiceCollection()
    .AddSingleton<INavigator>(nav)
    .AddTransient<DetailView>()
    .BuildServiceProvider();
SwiftServices.Current = sp;

await nav.PushAsync<DetailView>();
Assert.Equal(2, nav.Depth);
Assert.IsType<DetailView>(nav.Stack[^1]);
await nav.PopAsync();
Assert.Equal(1, nav.Depth);
```

### 13.7 Surface summary

| Component | Change |
|-----------|--------|
| **New** `Core/Navigation/INavigator.cs`, `Navigator.cs`, `PresentationStyle`. | The contract + a dispatcher-backed stack/modal implementation. |
| `Core/Views/Navigation.cs` | `NavigationStack` renders `INavigator.Stack`; `NavigationLink` re-expressed over `INavigator.PushAsync`; reports host back → `pop`. |
| `SwiftDotNetAppBuilder` | Auto-register `INavigator` (singleton) in `Build()`; optional `AddRoute<T>(key)`. |
| Per-platform hosts | Map stack diff → native push/pop; report hardware/gesture back as a `pop` event. |

### 13.8 Where it sits in the phases

| Phase | Navigation deliverable |
|-------|------------------------|
| **1** | `INavigator` registered; `PushAsync(View)` / `PopAsync` / `PushAsync<T>()`; `NavigationStack` renders the stack. Synchronous render (no dispatcher yet — push must originate on the UI thread for now). |
| **2** | Dispatcher-safe mutation (push/pop from any thread); `PresentAsync` + `ConfirmAsync`/`AlertAsync` result dialogs; host back-gesture sync; `OnAppear`/`OnDisappear` fire on push/pop. |
| **3** | Per-page `IServiceScope` (scoped view-models disposed on pop); string/deep-link route registry; URL/intent restore. |

Navigation intentionally tracks the DI phases 1:1 — it's the feature that *consumes* each phase's new
capability (ambient provider → threading → scoped lifetimes), which is a good forcing function for
getting each phase's shape right.

---

## 14. Decisions I need from you

1. **Locator vs strict ctor injection.** Ship `Service<T>()` (pragmatic, DSL-friendly) as recommended,
   or hold the line on constructor-injection-only and wait for reconciliation (Phase 3) before children
   can touch services? *Recommendation: ship `Service<T>()` now; it's opt-in and testable.*
2. **Web container:** reuse Blazor's `IServiceProvider` (one registration list) or keep a separate
   SwiftDotNet container? *Recommendation: reuse.*
3. **DI package baseline:** take the `Microsoft.Extensions.DependencyInjection` dependency in the core
   `SwiftDotNet` library, or isolate it in a `SwiftDotNet.Extensions.DependencyInjection` add-on so the
   core stays dependency-free? *Recommendation: a thin add-on package keeps Core clean; the `View.Service<T>()`
   hook stays in Core but binds to `IServiceProvider` (a BCL type), not the MS.DI package.*
4. **Navigation source of truth.** Make `INavigator`'s C# stack authoritative and have hosts mirror it
   (portable, testable, but every backend must forward hardware/gesture back as a `pop` event), or let
   each native container own its own stack and treat `INavigator` as a thin command forwarder (less
   host glue, but the C# `Stack`/`Depth` can drift and headless testing gets weaker)?
   *Recommendation: C# stack authoritative — it's the only design that stays testable and consistent
   across six backends.*
5. **Navigator lifetime & scope timing.** Ship per-page `IServiceScope` in Phase 3 (needs disposal
   plumbing on pop), or land imperative push/pop in Phase 1 with singleton-only resolution and defer
   scopes? *Recommendation: singleton-only in Phase 1; per-page scopes in Phase 3 where the stack gives
   them a clean boundary without waiting on full child-view reconciliation.*
6. **Keep declarative `NavigationLink`?** Re-express it over `INavigator.PushAsync` (one model, links
   become lazy-on-tap) or keep both models fully independent? *Recommendation: re-express — one source
   of truth, and destinations stop being rebuilt every render.*
```

---

## 15. Related: view construction seam (`Text()` vs `new Text()`)

A separate but reinforcing idea — replacing `new Text()` authoring with a function form `Text()`
routed through a **construction chokepoint** — is written up in
[`view-construction-seam.md`](./view-construction-seam.md). It matters to this proposal in three
places:

- **§4.3 `[Inject]`** — a source generator can emit the `[Inject]` fill as *static, reflection-free*
  assignments, which makes property injection trim/AOT-clean (addresses the §10 reflection concern).
- **§8 reconciliation** — the function form is the enabling primitive for Compose-style *positional
  state retention*, i.e. the "per-view child-instance state" milestone this doc keeps deferring. It is
  the same milestone approached from the construction side.
- **Service delivery rule** — that doc recommends *constructor = parent data, `[Inject]` = services*,
  a deliberate shift from §4's constructor-injection-of-services default. **The two docs must ratify
  one rule** (see its §8, decision 3).
