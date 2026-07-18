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

## 13. Decisions I need from you

1. **Locator vs strict ctor injection.** Ship `Service<T>()` (pragmatic, DSL-friendly) as recommended,
   or hold the line on constructor-injection-only and wait for reconciliation (Phase 3) before children
   can touch services? *Recommendation: ship `Service<T>()` now; it's opt-in and testable.*
2. **Web container:** reuse Blazor's `IServiceProvider` (one registration list) or keep a separate
   SwiftDotNet container? *Recommendation: reuse.*
3. **DI package baseline:** take the `Microsoft.Extensions.DependencyInjection` dependency in the core
   `SwiftDotNet` library, or isolate it in a `SwiftDotNet.Extensions.DependencyInjection` add-on so the
   core stays dependency-free? *Recommendation: a thin add-on package keeps Core clean; the `View.Service<T>()`
   hook stays in Core but binds to `IServiceProvider` (a BCL type), not the MS.DI package.*
```
