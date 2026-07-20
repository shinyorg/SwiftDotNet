# Proposal: Dependency Injection for SwiftDotNet

**Status:** **Phase 1 shipped** 2026-07-19 — decisions in §13 ratified. Reference docs:
[`docs/hosting-and-di.md`](../docs/hosting-and-di.md). Navigation split to
[`navigation-service-plan.md`](./navigation-service-plan.md) (paused); lifecycle continues in
[`page-lifecycle-plan.md`](./page-lifecycle-plan.md).
**Author:** (proposal)
**Date:** 2026-07-18 (revised 2026-07-19)
**Scope:** Let application services (repositories, HTTP clients, loggers, app state) reach `View`
subclasses — the "ContentViews" — on every backend (iOS/macOS/tvOS SwiftUI, Android Compose, Linux
GTK, Windows WinUI, Web/Blazor), without breaking the `new VStack(...)` authoring model or AOT.

**Shape (decided):** a **MAUI-style `SwiftProgram.CreateSwiftApp()`** composition root, built on
`SwiftDotNetApp.CreateBuilder()`, living **in the root `SwiftDotNet` project** (not an add-on).
Resolution goes through a Core-owned `SwiftHost`; `[Inject]` is filled by a **source generator** with
reflection-free assignments.

---

## 1. Goal

Today a screen is a plain object:

```csharp
public sealed class ContentView : View { /* ... */ }
// AppRoot.Create() => new ContentView();
```

We want a developer to be able to write:

```csharp
public sealed partial class WeatherView : View
{
    [Inject] public partial IWeatherService Weather { get; }   // ← injected, read-only
    readonly State<string> _summary = State("…");

    protected override async void OnAppearing()                // lifecycle hook (§4.4)
        => _summary.Value = await Weather.GetSummaryAsync();

    public override View Body => new Text(_summary.Value);
}
```

…and register `IWeatherService` once, in one place, and have it flow to the view — including views
nested deep in the tree — with correct lifetimes and thread-safety.

*(This snippet is the shape as built. The rest of the doc records how the design got there; where an
earlier section shows constructor injection or `OnAppear`, the ratified forms are `[Inject]` partial
properties (§4.3) and `OnAppearing`/`OnDisappearing` (§4.4).)*

## 2. Why this isn't automatic today

The current model has four properties that decide the design:

| Fact (in code) | Consequence for DI |
|----------------|--------------------|
| Views are POCOs built with `new` — the root in `AppRoot.Create()`, children inline inside `Body` (`new Rating(_rating)`, `new VStack(...)`). | Nothing constructs views *through* a container, so constructor injection isn't wired anywhere yet. |
| `SwiftApp` (`Core/SwiftApp.cs`) is a **static** runtime holding one `_root` and re-rendering it. | There's a single, app-global place to hang an `IServiceProvider`. That's good — one container per app — but it means the provider is effectively ambient, not passed as an argument. |
| The **root** view instance is kept alive across renders; **child** composite views created in `Body` are *transient* (rebuilt every render pass). This is the same limitation tracked by the "per-view local state ownership" milestone. | Constructor injection is clean for the **root** immediately. For **children**, until view-instance reconciliation lands, a child is a fresh object each render, so ctor injection there means "re-resolve every render" — fine for singletons, wrong for scoped/stateful. We need a path that works *now* and gets better when reconciliation lands. |
| ~~`State<T>.Value` setter calls `SwiftApp.RequestRender()` **synchronously**.~~ **Corrected 2026-07-19:** `SwiftApp` already captures `SynchronizationContext.Current` at `Run`, coalesces bursts, and `Post`s the render to it. | UI-thread marshaling is **already solved** for any backend whose `Run` happens on a thread with a `SynchronizationContext`. Async services can assign `State.Value` from a background thread today. See §7 for what's actually left. |

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

### 4.1 A host/builder (composition root) — MAUI's `MauiProgram` shape

We deliberately mirror **.NET MAUI's `MauiProgram.cs`**, because the model it establishes — one
`CreateXApp()` method per app, a builder carrying `Services`/`Logging`, `UseX()` extension methods for
optional libraries, and per-platform heads that do nothing but call it — is already what this repo's
hosting *almost* is, and it's the shape every MAUI developer arrives knowing.

```csharp
public sealed class SwiftDotNetApp
{
    public static SwiftDotNetAppBuilder CreateBuilder();   // ← MAUI: MauiApp.CreateBuilder()

    public IServiceProvider Services { get; }
    public View CreateRoot();                              // resolves the type given to UseSwiftApp<T>()
}

public sealed class SwiftDotNetAppBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();
    public ILoggingBuilder Logging { get; }
    public SwiftDotNetApp Build();
}
```

Usage — a shared, platform-neutral `SwiftProgram.cs` that **replaces `AppRoot.cs`**:

```csharp
public static class SwiftProgram
{
    public static SwiftDotNetApp CreateSwiftApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();

        builder
            .UseSwiftApp<ContentView>()          // registers + marks the root view
            .UseAppleMaps();                     // src/SwiftDotNet.Maps.Apple (platform head only)

        builder.Services.AddSingleton<IWeatherService, WeatherService>();
        builder.Services.AddTransient<DetailView>();   // pushed pages get ctor injection (deferred: navigation plan)

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
```

**The `UseX()` seam is the main structural win.** SwiftDotNet's optional libraries are **separate
projects**, and the ones with renderers to register now extend the builder — mirroring
`UseMauiCommunityToolkit()`:

| Extension | Package |
|---|---|
| `UseAppleMaps()` | `SwiftDotNet.Maps.Apple` |
| `UseAppleCamera()` | `SwiftDotNet.Controls.Camera.Apple` |
| `UseMapLibreMaps()` | `SwiftDotNet.Maps.Web` |

**As built, there is deliberately no `UseSwiftDotNetControls()` / `UseMaps()`:** those libraries are pure
composition over the core with nothing to register, so a no-op extension would be decoration. Extensions
are added only where there is real registration to do.

**Deferred:** `builder.Configuration`. MAUI exposes it, but `ConfigurationManager`'s JSON binding is
reflection-based and is the usual trim casualty under iOS AOT (§10). `Services` + `Logging` ship in
Phase 1; `Configuration` waits until there's a concrete ask.

`AppRoot.Create()` is already the single registration point every platform host base funnels through
(`SwiftDotNetAppDelegate`, `SwiftDotNetActivity`, `SwiftDotNetApplication`, the Blazor `AppRoot`, GTK
and Skia `Program.Main`), so this is a rename plus a return-type change on ~6 files — see §6.

### 4.2 Reaching services from *any* view — `Service<T>()`

Because inline children aren't container-created, add a resolver to the `View` base so the DSL is
untouched:

```csharp
public abstract class View
{
    /// <summary>Resolve a required service from the running app's container.</summary>
    protected TService Service<TService>() where TService : notnull
        => SwiftHost.Require<TService>();

    /// <summary>Resolve an optional service (null if unregistered).</summary>
    protected TService? OptionalService<TService>()
        => SwiftHost.Optional<TService>();
}
```

backed by **`SwiftHost`** — the ambient provider plus the resolution helpers. This is the single type
the render path, `View.Service<T>()`, and all generated `[Inject]` code call:

```csharp
public static class SwiftHost
{
    /// <summary>The running app's provider. Set by SwiftApp.Run; settable in tests.</summary>
    public static IServiceProvider? Services { get; set; }

    // Explicit-provider overloads — what the source generator emits (§4.3).
    public static T Require<T>(IServiceProvider sp) where T : notnull
        => (T?)sp.GetService(typeof(T)) ?? throw new InvalidOperationException(
               $"No service registered for {typeof(T)}. Register it in SwiftProgram.CreateSwiftApp().");

    public static T? Optional<T>(IServiceProvider sp) => (T?)sp.GetService(typeof(T));

    // Ambient overloads — what View.Service<T>() and hand-written call sites use.
    public static T Require<T>() where T : notnull => Require<T>(Current);
    public static T? Optional<T>() => Services is { } sp ? Optional<T>(sp) : default;

    static IServiceProvider Current => Services
        ?? throw new InvalidOperationException("No SwiftDotNet service provider is running.");
}
```

> **Naming note.** `SwiftHost` **replaces** the earlier `SwiftServices` — one concept, one name. It is
> deliberately **resolution-only**; `SwiftDotNetApp.CreateBuilder()` is the composition entry point.
> Note also that `SwiftHost` binds to **BCL `IServiceProvider.GetService` only** — no
> `Microsoft.Extensions.DependencyInjection` types appear on the render path or in generated code, even
> though the package now ships in the root project (§10).

`SwiftApp.Run` gains an overload that stashes the provider:

```csharp
public static void Run(View root, IBridge bridge, IServiceProvider? services = null)
{
    SwiftHost.Services = services;
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

### 4.3 `[Inject]` property injection — filled by a source generator

For views that the container *does* create (roots and pushed pages today; any view once §8 lands),
support attribute injection. **The documented form is a `partial` property** (C# 13+):

```csharp
public sealed partial class ContentView : View        // ← the type must be partial too
{
    [Inject] public partial IWeatherService Weather { get; }   // required
    [Inject] public partial IImageCache? Cache { get; }        // nullable ⇒ optional resolve
}
```

No `= default!` initializer, no public setter — the service is read-only to everything except the
generated code.

**Filled by a source generator, not reflection.** For each view with `[Inject]` members, emit one
partial implementing `IInjectable`, plus the implementing half of each partial property over its own
backing field:

```csharp
// <auto-generated/>
partial class ContentView : IInjectable
{
    private IWeatherService? __inject_Weather;
    public partial IWeatherService Weather
        => __inject_Weather ?? throw new InvalidOperationException(
            "'ContentView.Weather' was read before it was injected. Only container-created views are "
            + "injected; inline children should use Service<T>() instead.");

    private IImageCache? __inject_Cache;
    public partial IImageCache? Cache => __inject_Cache;

    void IInjectable.Inject(IServiceProvider sp)
    {
        __inject_Weather = SwiftHost.Require<IWeatherService>(sp);
        __inject_Cache   = SwiftHost.Optional<IImageCache>(sp);
    }
}
```

Because the generated getter owns the read, a **required** reference-typed service that was never
injected throws an explanatory error at the point of use rather than NRE-ing somewhere downstream —
the failure mode `SDN1003` warns about at compile time, caught again at runtime. Optional properties
return null.

**Legacy form.** `[Inject] public IFoo Foo { get; set; } = default!;` is still supported and assigned
directly, for anyone who cannot use C# 13. It has no uninjected-read diagnostic — it simply hands back
null — which is why the partial form is the default.

Either way this is the **only** approach that is trim/AOT-clean on iOS without annotating every view
with `DynamicallyAccessedMembers`, and it honours the repo's no-reflection rule (`NodeJson.cs` is
hand-rolled for exactly this reason). It resolves the concern in §10.

#### Deliberately decoupled from the construction seam

[`view-construction-seam.md`](./view-construction-seam.md) bundles the `[Inject]` generator into
**Tier 1** of the function-form seam (`Text()` instead of `new Text()`), which drags in
`RenderScope`/`KeyFor` positional identity — a problem that doc itself calls "the hard, unresolved
part" and which *is* the reconciliation milestone. **None of that is needed for reflection-free
`[Inject]`.**

So the generator ships here, standalone, with exactly one call site:

```csharp
if (view is IInjectable i) i.Inject(sp);
```

invoked from `SwiftDotNetApp.CreateRoot()` and `INavigator.PushAsync<T>()` ([navigation plan](./navigation-service-plan.md), paused). Both hand back
**retained** instances, so injection runs exactly once per instance — no render-loop cost, no memo
keys, no `new`-vs-function decision. The seam doc's Tier 1 then *reuses* this generator rather than
introducing it.

**Costs to plan for:** views with `[Inject]` must be `partial` (and the partial-property form needs C# 13+, which `LangVersion latest` on `net10.0` already gives us); a new `src/SwiftDotNet.SourceGenerators`
project (`netstandard2.0`, `IncludeBuildOutput=false`, packed under `analyzers/dotnet/cs`); and
generator diagnostics for the failure modes —

| Diagnostic | Case |
|---|---|
| `SDN1001` | `[Inject]` on a non-`partial` type |
| `SDN1002` | `[Inject]` on a property that can't be assigned — non-partial get-only, or `init`-only |
| `SDN1003` | `[Inject]` on a view only ever built inline in a `Body` — nothing calls `Inject`, so it would silently stay null |

`SDN1003` is the important one: it's what stops `[Inject]` quietly no-op'ing on transient children
until reconciliation lands (§8).

### 4.4 Scoped views, initializers, and lifecycle (added 2026-07-19, implemented in Phase 1)

**`ViewScope`** pairs a retained view with its own `IServiceScope`, which is what makes *scoped*
`[Inject]` meaningful. The scope boundary is the **retained view** — the root today, each pushed page
once `INavigator` lands — because that is the only place a scope has a real lifetime. `SwiftHost` gained
an `ActiveScope` + `EnterScope(...)`, so `Service<T>()` and generated `[Inject]` resolve from the page's
scope while that page renders or handles an event, falling back to the app provider outside it.

> **Gotcha:** `ActiveScope` is a plain static, valid across *synchronous* UI-thread work. An
> `async void` handler that awaits resumes after the scope has been exited — capture services before
> awaiting.

**`ISwiftInitializer`** is the analog of MAUI's `IMauiInitializeService` / `IMauiInitializeScopedService`,
deliberately collapsed into **one** interface:

```csharp
public interface ISwiftInitializer { void Initialize(IServiceProvider services, bool scoped); }
```

Invoked with `scoped: false` once from `Build()` against the root provider, and with `scoped: true` on
every `ViewScope.Create` — before anything else resolves from the new scope. One interface avoids MAUI's
situation where a service wanting both has to implement two near-identical ones.

**Lifecycle has two surfaces**, both raised by the same dispatch and superseding the `OnAppear`/
`OnDisappear` sketch used elsewhere in this doc:

1. **`IViewLifecycle`** — a cross-cutting *observer*, registered in the container and resolved as an
   enumerable, so every registered implementation sees every retained view:

   ```csharp
   public interface IViewLifecycle
   {
       void OnCreated(View view);
       void OnAppearing(View view);
       void OnDisappearing(View view);
       void OnDestroyed(View view);
   }
   ```

2. **`View` virtuals** — `protected virtual OnCreated()` / `OnAppearing()` / `OnDisappearing()` /
   `OnDestroyed()`, for a view that only cares about itself. (Views don't implement `IViewLifecycle`:
   passing a view itself as an argument reads badly.)

**DI is delivered as an observer, not a special case.** The framework registers an internal
`InjectionViewLifecycle` whose `OnCreated` performs the `[Inject]` fill. It is registered **scoped**, so
resolving it from the app provider injects app-wide services and resolving it from a `ViewScope` injects
that scope's — the entire scoped-vs-non-scoped distinction, with no second code path.

**Ordering** follows construction/disposal order and is what makes the above work: setup (`OnCreated`,
`OnAppearing`) runs **observers → view**, so `[Inject]` members are filled before the view's own
`OnCreated`; teardown (`OnDisappearing`, `OnDestroyed`) runs **view → observers**. `OnDestroyed` runs
before the scope is disposed, so scoped services are still resolvable inside it.

Inline `Body` children get **no** lifecycle callbacks — they are rebuilt every render, so
"created"/"destroyed" would fire continuously and mean nothing.

**The root is not scoped.** It lives for the app's lifetime, so a dedicated scope would too and buy
nothing; `CreateRoot()` injects from the app provider, raises `OnCreated` then `OnAppearing`, and caches
the instance. `ViewScope` is the scoped path, waiting on the paused navigation plan to give it a
create/destroy boundary.

### 4.5 Push direction — services that update the UI safely

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
| `Core/SwiftApp.cs` | `Run` overload taking `IServiceProvider?`; store it in `SwiftHost`; route `RequestRender` through a dispatcher. |
| `Core/View.cs` | Add `Service<T>()` / `OptionalService<T>()` and the `OnCreated`/`OnAppearing`/`OnDisappearing`/`OnDestroyed` hooks (§4.4). |
| **New** `Core/SwiftHost.cs` | Ambient provider + `Require`/`Optional`. BCL `IServiceProvider` only. |
| **New** `Core/Hosting/SwiftDotNetApp.cs`, `SwiftDotNetAppBuilder.cs`, `SwiftDotNetAppBuilderExtensions.cs` | `CreateBuilder()` / `Build()` / `CreateRoot()` and `UseSwiftApp<T>()`. |
| **New** `Core/Injection.cs`, `Core/IViewLifecycle.cs`, `Core/Hosting/ViewLifecycleDispatcher.cs`, `ViewScope.cs`, `ISwiftInitializer.cs` | Generator contract, lifecycle surface + dispatch, scoped views, initializers. |
| **New project** `src/SwiftDotNet.SourceGenerators` | The `[Inject]` generator (§4.3) + `SDN1001`–`SDN1003`. |
| `SwiftDotNet.csproj` | Add `Microsoft.Extensions.DependencyInjection` + `.Logging`; ship the generator as an analyzer. |
| Sibling libs (`Controls`, `Maps`, `Camera`, `Skia`) | Each adds a `UseX(this SwiftDotNetAppBuilder)` extension registering its renderers. |
| Per-platform `Platforms/*` | `CreateRoot()` → `CreateSwiftApp()` (§6); provide an `ISwiftDispatcher` (§7). |
| `AppRoot.cs` (sample) → **`SwiftProgram.cs`** | Build the container, register services, `UseSwiftApp<ContentView>()`. |

The public authoring model for existing apps is **unchanged** — DI is additive. An app that registers
nothing behaves exactly as today.

## 6. Composition root per platform

Exactly as in MAUI, each platform head does nothing but return the shared app. The host bases change
their abstract member from `View CreateRoot()` to `SwiftDotNetApp CreateSwiftApp()`, then internally
set `SwiftHost.Services` and build the root:

```csharp
public abstract class SwiftDotNetAppDelegate : UIApplicationDelegate
{
    protected abstract SwiftDotNetApp CreateSwiftApp();       // ← MAUI: MauiApp CreateMauiApp()

    public override bool FinishedLaunching(UIApplication app, NSDictionary opts)
    {
        var swiftApp = CreateSwiftApp();
        SwiftHost.Services = swiftApp.Services;
        Window = new UIWindow(UIScreen.MainScreen.Bounds)
        {
            RootViewController = SwiftDotNetHost.CreateRootController(swiftApp.CreateRoot()),
        };
        // …
    }
}
```

And every app head collapses to one line — e.g. `sample/SampleApp/Platforms/iOS/AppDelegate.cs`:

```csharp
protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
```

Per backend:

- **iOS/tvOS/macOS** — `SwiftDotNetAppDelegate` (3 files under `Platforms/`), via `CreateRootController`.
- **Android** — `SwiftDotNetActivity`, via `CreateRootView`.
- **Windows** — `SwiftDotNetApplication`, via `CreateRootElement`.
- **Linux/GTK & Skia** — `Program.Main` calls `SwiftProgram.CreateSwiftApp()` before `SwiftApp.Run`.
- **Web/Blazor** — Blazor already *has* a container. **Decided: reuse it** (§13.2) — assign the
  component's injected `IServiceProvider` to `SwiftHost.Services`, so one registration list serves both
  Blazor and SwiftDotNet. `SwiftProgram` then contributes registrations into Blazor's
  `builder.Services` rather than building a second container.

Because the app object carries the provider, the earlier `ConfigureServices` hook on the host bases is
**dropped** — registration belongs in `SwiftProgram`, one place, MAUI-style.

## 7. Threading — mostly already done (corrected 2026-07-19)

**This section originally over-scoped the work.** `Core/SwiftApp.cs` as it stands already:

- captures `SynchronizationContext.Current` at `Run` (`_uiContext`),
- coalesces a burst of `State<T>` mutations into a single render (`_renderQueued`),
- `Post`s the render to that context, falling back to an inline render only when `_uiContext is null`,
- and offers `SwiftApp.Transaction(...)` as an explicit synchronous batch boundary.

So "a service updates `State.Value` from a background thread" is **safe today** on every backend whose
`Run` is called on a thread carrying a `SynchronizationContext` — which is the normal case on iOS/tvOS/
macOS, Android, WinUI and Blazor.

**Audit (done 2026-07-19):**

| Backend | Captures a `SynchronizationContext` at `Run`? |
|---|---|
| Linux/GTK | ✅ `app.RunWithSynchronizationContext(null)` — [`SwiftDotNet.Gtk/SwiftDotNetHost.cs`](../src/SwiftDotNet.Gtk/SwiftDotNetHost.cs) |
| iOS / tvOS / macOS | ✅ launch runs on the UI thread |
| Android | ✅ `OnCreate` on the main looper |
| Windows / WinUI | ✅ `OnLaunched` on the UI thread |
| **Skia hosts** (Mac, Silk, headless) | ❌ **none** — renders inline on the calling thread |
| Web / Blazor | ⚠️ unverified |

So GTK — the original suspect — is fine, and the remaining hazard is narrower than assumed:

1. **The Skia hosts**, plus a Blazor check.
2. **Add `ISwiftDispatcher` only as the fallback** for those backends, injected via the container so
   services can also `Post` explicitly (**not yet built** — see §11 item 1):

```csharp
public interface ISwiftDispatcher { void Post(Action work); }

// SwiftApp.RequestRender, after the existing _batching check:
if (_uiContext is null && _dispatcher is { } d) { d.Post(Render); return; }
```

Per-backend dispatcher, **where a `SynchronizationContext` is absent** (each a handful of lines):

| Backend | Marshal via |
|---------|-------------|
| iOS/tvOS/macOS | `CoreFoundation.DispatchQueue.MainQueue.DispatchAsync` |
| Android | `Handler(Looper.MainLooper!).Post(...)` |
| WinUI | `DispatcherQueue.TryEnqueue(...)` |
| GTK | `GLib.Functions.IdleAdd(...)` |
| Web/Blazor | `ComponentBase.InvokeAsync(...)` |

The remaining latent bug is therefore **backend-specific, not global**: off-thread `State` mutation is
unsafe only where no `SynchronizationContext` was captured at `Run`. Phase 2 should start by measuring
that, not by building six dispatchers.

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
SwiftHost.Services = sp;

var view = new WeatherView();
((IInjectable)view).Inject(sp);                       // same call site the host uses
var node = view.ToNode(new RenderContext(), "0");
// assert on the node tree — no device, no bridge
```

This composes with the existing Core test harness that already diffs node trees.

## 10. AOT / trimming

**The core library now references `Microsoft.Extensions.DependencyInjection` (decided, §13.3).** This
reverses the original recommendation of a thin `SwiftDotNet.Extensions.DependencyInjection` add-on.
The trade, stated plainly:

- **Cost.** `SwiftDotNet` is no longer dependency-free. `Microsoft.Extensions.DependencyInjection`
  (+ `.Abstractions`, `.Logging`) flow to *every* consumer on every TFM, including ones that register
  nothing. The `IsTrimmable=true` promise now partly rides on packages we don't own.
- **Why it's worth it.** Hosting *is* the framework's front door — `SwiftProgram.CreateSwiftApp()` is
  the first line of every app. Behind an opt-in package, the documented golden path needs an extra
  reference, the `UseX()` seam can't live where the sibling libraries need it, and MAUI parity (where
  `MauiApp` is core) is lost. These packages are effectively part of the platform — MAUI, ASP.NET Core
  and Aspire all take them unconditionally.
- **Containment.** The dependency stays at the *composition* layer. `SwiftHost`, `View.Service<T>()`,
  generated `[Inject]` code, and everything on the render path bind only to **BCL `IServiceProvider`**,
  so MS.DI types never reach the hot path and the container could be swapped later.

Reflection specifics:

- **`[Inject]` is reflection-free** — the generator emits static assignments (§4.3), so property
  injection carries no trim risk at all. This is the main reason to prefer it over `ActivatorUtilities`.
- **Explicit factory registration** is the trim-clean default for constructor injection —
  `services.AddSingleton<ContentView>(sp => new ContentView(sp.GetRequiredService<IWeatherService>()))`.
- `ActivatorUtilities.CreateInstance` (used by `AddSingleton<TRoot>()` without a factory) works under
  iOS AOT today (MAUI relies on it) but can surface trim warnings; we'll annotate the resolve entry
  points with `[RequiresUnreferencedCode]`/`DynamicallyAccessedMembers` and document the factory form
  as the default.
- No change to `NodeJson` (still hand-rolled, zero-reflection). DI reflection is confined to
  construction, never the render/serialize hot path.

## 11. Phased delivery — **revised 2026-07-19 (post-ship)**

Phase 1 shipped. Re-auditing what was called Phases 2–3 against the code, **most of it is not this
plan's work** — it belongs to plans that own those milestones. Handing it over rather than tracking it
twice:

| Original item | Where it actually lives now |
|---|---|
| `OnAppear`/`OnDisappear` firing on real visibility | [`page-lifecycle-plan.md`](./page-lifecycle-plan.md) §1b — native emitters per backend + node-id→view routing |
| Container-created **child** views, scoped-per-view lifetimes | The **view-instance reconciliation** milestone (also gates [`view-construction-seam.md`](./view-construction-seam.md) Tier 1 and animation transitions) |
| Per-**page** `IServiceScope` | [`navigation-service-plan.md`](./navigation-service-plan.md) (paused). `ViewScope` is built and tested for exactly this and currently has **no production caller**. |

### What is genuinely left inside this plan

| # | Item | Detail | Size |
|---|---|---|---|
| 1 | **`ISwiftDispatcher` for backends with no `SynchronizationContext`** | Does not exist (zero references outside this doc). The §7 audit is now partly done — see the table there: GTK, Apple, Android and WinUI all capture one; the **Skia hosts (Mac / Silk / headless) do not** and render inline on the calling thread. Blazor unverified. | Small — one dispatcher + a check, not six backends |
| 2 | **§4.5 pattern 2** — services injecting a dispatcher to post UI updates | Same item as (1). Pattern 1 (a service mutating `State<T>`) already works wherever a sync context exists. | — |
| 3 | **Verify the Windows head** | `SwiftDotNetApplication.CreateSwiftApp()` is written but has never been compiled — needs a Windows host. | Unknown until run |
| 4 | **`SDN1003` false positives** | The "never container-created" check is a whole-compilation heuristic; a view registered from another assembly trips it. Needs either a suppression story or a `[ContainerCreated]`-style opt-out. | Small |
| 5 | **`builder.Configuration`** | Deferred by choice (§4.1) — `ConfigurationManager`'s reflective binding is a trim casualty under iOS AOT. Revisit on demand. | Deferred |

**Bottom line: DI is done as a feature.** Item 1 is the only real code left; 3–4 are follow-ups.

## 12. Navigation — split out

The navigation service (`INavigator`, imperative push/pop, per-page scopes, host back-gesture sync) was
**paused and moved to [`navigation-service-plan.md`](./navigation-service-plan.md)** on 2026-07-19, so DI
Phase 1 could land without taking on host glue in all six backends. References to `INavigator` elsewhere
in this doc describe that deferred plan.

## 12.1 Worked example (end state, Phase 1 + 2)

```csharp
// App setup — sample/SharedUI/SwiftProgram.cs
public static class SwiftProgram
{
    public static SwiftDotNetApp CreateSwiftApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp<ContentView>()
               .UseSwiftDotNetControls();
        builder.Services.AddSingleton<IGreetingService, GreetingService>();
        builder.Services.AddSingleton<IAudit, ConsoleAudit>();
        return builder.Build();
    }
}

// Each platform head — sample/SampleApp/Platforms/*/…
protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();

// A screen pulls a service via ctor injection and updates on appear
public sealed class ContentView : View
{
    readonly IGreetingService _greeter;
    readonly State<string> _greeting = State("…");

    public ContentView(IGreetingService greeter) => _greeter = greeter;

    protected override async void OnAppearing() =>
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

## 13. Decisions — ratified 2026-07-19

| # | Decision | Outcome |
|---|----------|---------|
| 1 | Locator vs strict ctor injection | **Ship `Service<T>()`** alongside ctor injection and `[Inject]`. Opt-in, testable, DSL-friendly. |
| 2 | Web container | **Reuse Blazor's `IServiceProvider`** — one registration list serves both (§6). |
| 3 | DI package baseline | **Take MS.DI in the root `SwiftDotNet` project** — *reverses* the original add-on recommendation. Hosting is the front door and the `UseX()` seam must live where the sibling libraries can reach it. Trade written up in §10. |
| 4 | Navigation source of truth | **C# stack authoritative**; hosts mirror it and forward hardware/gesture back as `pop`. |
| 5 | Navigator lifetime & scope timing | **Singleton-only in Phase 1**; per-page `IServiceScope` in Phase 3. |
| 6 | Declarative `NavigationLink` | **Re-express** over `INavigator.PushAsync` — one model, destinations become lazy-on-tap. |
| 7 | Hosting shape *(new)* | **Mirror .NET MAUI's `MauiProgram.cs`**: `SwiftProgram.CreateSwiftApp()`, `SwiftDotNetApp.CreateBuilder()`, `builder.Services`/`builder.Logging`, `UseX()` extensions, heads that only forward (§4.1, §6). `builder.Configuration` deferred. |
| 8 | Resolution entry point *(new)* | **`SwiftHost`** replaces `SwiftServices`, and is resolution-only — `SwiftDotNetApp.CreateBuilder()` is composition. Binds to BCL `IServiceProvider` only (§4.2). |
| 9 | `[Inject]` mechanism *(new)* | **Source generator**, reflection-free, shipped **standalone in Phase 1** — explicitly *not* gated behind the construction seam's Tier 1 (§4.3). |

---

## 14. Related: view construction seam (`Text()` vs `new Text()`)

A separate but reinforcing idea — replacing `new Text()` authoring with a function form `Text()`
routed through a **construction chokepoint** — is written up in
[`view-construction-seam.md`](./view-construction-seam.md). It matters to this proposal in three
places:

- **§4.3 `[Inject]`** — the generator that emits the reflection-free fill now **ships here, in Phase 1**,
  decoupled from that doc's Tier 1. The seam reuses it rather than introducing it.
- **§8 reconciliation** — the function form is the enabling primitive for Compose-style *positional
  state retention*, i.e. the "per-view child-instance state" milestone this doc keeps deferring. It is
  the same milestone approached from the construction side.
- **Service delivery rule — ratified.** Both docs now say: **constructor = parent data,
  `[Inject]` = services**, with `Service<T>()` as the escape hatch for leaves that would otherwise need
  a constructor. Constructor injection of services stays *supported* (and is how the root and pushed
  pages work today, since the container builds them), but `[Inject]` is the documented default because
  it is the generator-friendly, reflection-free form.
