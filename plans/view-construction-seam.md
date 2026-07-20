# Proposal: View construction seam — function-form authoring + a source generator

**Status:** Draft for review — decision 1 (adopt the function form?) still open
**Date:** 2026-07-18 (revised 2026-07-19)
**Companion to:** [`dependency-injection-proposal.md`](./dependency-injection-proposal.md) (esp. §4 DI, §8 reconciliation, §10 AOT)

**Scope:** Replace `new TextEntry()` authoring with a function form — `TextEntry()` — where the call
routes through a single **construction chokepoint**. Explores what that chokepoint unlocks
(DI, positional state retention), and what a **source generator** would emit so the form is uniform
for built-in *and* user-authored views without hand-writing a forwarder per type.

> **Scope change 2026-07-19.** The `[Inject]` source generator moved *out* of this doc and into DI
> proposal Phase 1, where it ships without needing the construction seam. What remains here is the
> function form itself and the **reconciliation** it unlocks — which is the actual architectural value
> (§2), and which no longer blocks DI.

---

## 1. The idea

Today every view is built with `new`, inline in `Body`:

```csharp
public override View Body =>
    new VStack(
        new Text(_summary.Value).Font(Font.Headline),
        new Rating(_rating)
    );
```

The proposal: author with a **function call** instead — `Text(...)`, `VStack(...)`, `Rating(...)` —
imported via `global using static SwiftDotNet.Views;`:

```csharp
public override View Body =>
    VStack(
        Text(_summary.Value).Font(Font.Headline),
        Rating(_rating)
    );
```

Closer to SwiftUI (`Text("hi")`, no `new`), and — the real point — **every construction now flows
through one seam the runtime owns**, instead of `new` scattered opaquely through every `Body`.

## 2. The governing fact (why this matters here)

From `Core/SwiftApp.cs` + `Core/State.cs`: **only the root view instance is retained across renders.**
Child composite views are `new`'d inside `Body` on every render pass and discarded; their `State<T>`
survives *only* on the root because the root instance survives. (`ContentView.cs` documents this
directly: "All state lives here on the root view … per-view child-instance state is a separate
milestone.")

The runtime **cannot intercept** `new Child(...)` inside a `Body` — it is opaque. If `Body` instead
calls `Child(...)`, the runtime owns that call and can do anything underneath it. That interception
seam is the thing we do not have and cannot add later without touching every call site. **This is the
architectural value — not DI.**

## 3. Two axes this separates

People conflate two independent things:

1. **Service injection** — a view needs `IWeatherService`, `INavigator`, `ILogger`. Solved by an
   ambient container + `[Inject]` / `Service<T>()` (DI proposal §4). Does *not* require the factory
   to own construction.
2. **Construction ownership** — who calls `new`. *This* is what the function form changes. It is
   fundamentally about **lifetimes / reconciliation**, not DI.

Blazor couples the two because its `Renderer` owns a retained component tree. SwiftUI — and this
`new`-based DSL — deliberately don't. The function form lets us reclaim construction ownership
*without* asking the author to give up literal composition.

## 4. What the chokepoint unlocks

### 4.1 Leaf primitives don't need DI
`Text`, `TextField`, `Button` have **no service dependencies** — nothing to inject. Routing them
through a `ServiceProvider` factory buys zero DI value, and the function form doesn't dissolve the
construct-vs-parameterize tension (`Text("hi")` still passes data positionally). So the function form
is **not** "the DI view factory." DI only ever mattered for composite screens, best served by the
container at reconciliation seams (nav pages, list rows — [`navigation-service-plan.md`](./navigation-service-plan.md)).

### 4.2 The real payoff — transparent state retention (the Compose model)
A construction chokepoint is exactly the primitive needed to fix root-only retention **without**
explicit keys at call sites:

```
Body calls Text(...) / Rating(...)
  → chokepoint derives a key from the ambient render scope + call position (RenderContext already
    threads structural paths like "0.2.1")
  → instance retained at this key? reuse it (State<T> fields survive), re-apply new parameters
  → else construct + cache; fire OnAppear
```

This is **Jetpack Compose's positional memoization**: `@Composable fun Text(...)` looks like a plain
call but the Composer tracks call position to retain state across recompositions. The function-call
syntax is the enabling primitive; our per-render structural paths are the memo keys. So this idea is
the seam that moves us from "root-only retention" to automatic per-view retention — the milestone the
DI proposal defers to "reconciliation" (§8).

## 5. The source generator

### 5.1 Why
Hand-writing `Foo()` forwarders for ~40 built-in primitives is bounded but tedious, and — worse — a
user's custom `MyControl : View` would have no forwarder, forcing an ugly split (`Foo()` for
built-ins, `new MyControl()` for user types) that defeats the no-`new` aesthetic. A generator makes
the form **uniform and free** for built-in and user types alike.

### 5.2 What it emits
For every public non-abstract `View` subclass, emit one static forwarder per public constructor into
a partial static class `Views` (consumed via `global using static SwiftDotNet.Views;`):

- **Tier 0 — pure sugar.** The forwarder is `=> new T(args)`. Zero behavior change. Ship first; it is
  mechanical and safe, and establishes the seam at every call site.
- **Tier 1 — the chokepoint.** The forwarder routes through `ViewFactory.Create(...)`, which consults
  the ambient render scope for positional retention + DI, then fills `[Inject]` members **with
  statically-emitted assignments (no reflection)**.

> **Update (2026-07-19): the `[Inject]` generator is no longer part of this doc's scope.** It ships
> standalone in **DI proposal Phase 1** — see its §4.3. The reason: filling `[Inject]` reflection-free
> needs *nothing* from the construction seam. It needs one interface (`IInjectable`) and one call site
> (`if (v is IInjectable i) i.Inject(sp);`) inside `CreateRoot()` / `PushAsync<T>()`, both of which hand
> back **retained** instances. Bundling it here would have gated AOT-clean DI behind `RenderScope`/
> `KeyFor` positional identity — the unresolved crux in §7.2. Tier 1 now **reuses** that generator,
> extending it to inline children once retention exists.

### 5.3 The construct-vs-parameterize rule the generator needs
The generator must know which values are **parent data** vs **container services**. Recommended
convention (Blazor-aligned, and it makes forwarder signatures pure data):

> **Constructor parameters = parent data (parameters). `[Inject]` properties = services from the
> container.**

**Ratified 2026-07-19** (DI proposal §13, decision 9) — both docs now state this rule. Constructor
injection of services stays *supported*, since the container builds roots and pushed pages, but
`[Inject]` is the documented default because it is the generator-friendly, reflection-free form and it
keeps generated forwarder signatures pure data. (Alternative, documented but rejected: forward *all*
ctor params and let `ActivatorUtilities.CreateInstance` fill service-typed params at runtime — more
magic, harder for the generator to reason about, reflection-tinged.)

## 6. Worked example — DI + service + state

A service:

```csharp
public interface IWeatherService
{
    Task<string> GetSummaryAsync(string city);
}
```

A composite screen — **`[Inject]` services**, a **ctor parameter** (parent data), and **local
`State`**:

```csharp
using SwiftDotNet;
using Microsoft.Extensions.Logging;

public sealed partial class WeatherView : View
{
    [Inject] public partial IWeatherService Weather { get; }    // service (container)
    [Inject] public partial ILogger<WeatherView> Log { get; }   // service (container)

    readonly string _city;                          // parameter (from parent, via ctor)
    readonly State<string> _summary = State("…");   // local state (survives once retained)

    public WeatherView(string city) => _city = city;

    protected override async void OnAppear()        // lifecycle hook (DI proposal Phase 2)
    {
        Log.LogInformation("Loading weather for {City}", _city);
        _summary.Value = await Weather.GetSummaryAsync(_city);  // async result marshals to UI thread
    }

    public override View Body =>
        VStack(
            Text($"Weather in {_city}").Font(Font.LargeTitle),
            Text(_summary.Value).Font(Font.Headline).ForegroundColor(Color.Blue),
            RefreshButton("Refresh", () => _summary.Value = "…")
        ).Spacing(8).Padding(16);
}
```

A small custom control that reaches a service via the locator (a leaf composite — no ctor service):

```csharp
public sealed class RefreshButton : View
{
    readonly string _title;
    readonly Action _tap;

    public RefreshButton(string title, Action tap) { _title = title; _tap = tap; }

    public override View Body =>
        Button(_title, () => { Service<IAudit>().Log(_title); _tap(); });
}
```

Registration (shared `SwiftProgram.cs`, per DI proposal §4.1):

```csharp
var builder = SwiftDotNetApp.CreateBuilder();
builder.UseSwiftApp<ContentView>();
builder.Services.AddSingleton<IWeatherService, WeatherService>();
builder.Services.AddSingleton<IAudit, Audit>();
builder.Logging.AddDebug();
// WeatherView is authored via WeatherView("London") in a Body — the factory constructs it, so no
// container registration of the view type itself is required unless it is a resolved root/route.
```

## 7. What the generator emits for §6

### 7.1 Tier 0 — pure sugar (ship first)

```csharp
// <auto-generated/>
#nullable enable
namespace SwiftDotNet;

public static partial class Views
{
    // Framework primitives (emitted once, in the SwiftDotNet assembly)
    public static Text   Text(string text)                 => new Text(text);
    public static VStack VStack(params View[] children)    => new VStack(children);
    public static Button Button(string title, System.Action tap) => new Button(title, tap);

    // User types (emitted into the app assembly's generated partial)
    public static WeatherView   WeatherView(string city)                       => new WeatherView(city);
    public static RefreshButton RefreshButton(string title, System.Action tap) => new RefreshButton(title, tap);
}
```

At this tier, `VStack(Text(...), RefreshButton(...))` is *identical* in behavior to today's `new`
form — it is a syntactic seam only. Existing modifiers (`.Font(...)`, `.Padding(...)`) chain
unchanged because the forwarder returns the concrete `View`.

### 7.2 Tier 1 — the chokepoint (DI + positional retention)

The forwarder routes through `ViewFactory.Create`, which (a) derives a memo key from the ambient
render scope + call position, (b) returns the retained instance at that key if present — preserving
its `State<T>` — else constructs, (c) fills `[Inject]` members with **statically emitted, reflection-
free** assignments, (d) drives `OnAppear`/`OnDisappear` on mount/unmount.

```csharp
// <auto-generated/>
#nullable enable
namespace SwiftDotNet;

public static partial class Views
{
    public static WeatherView WeatherView(string city)
        => ViewFactory.Create(
               // constructor from parent data + reflection-free [Inject] fill from the container:
               factory: static (sp, city) =>
               {
                   var v = new WeatherView(city);
                   ((IInjectable)v).Inject(sp);   // generated in DI Phase 1 — see that doc's §4.3
                   return v;
               },
               arg: city,
               callSite: 0x8F2A_0001 /* generated stable id for this forwarder */);

    public static RefreshButton RefreshButton(string title, System.Action tap)
        => ViewFactory.Create(
               factory: static (sp, args) => new RefreshButton(args.title, args.tap), // no [Inject] members
               arg: (title, tap),
               callSite: 0x8F2A_0002);
}
```

Sketch of the runtime seam the forwarders call:

```csharp
public static class ViewFactory
{
    // Consults RenderScope.Current for the memo table; positional index + callSite form the key.
    internal static T Create<TArg, T>(Func<IServiceProvider, TArg, T> factory, TArg arg, int callSite)
        where T : View
    {
        var scope = RenderScope.Current;                 // ambient, set while a Body is being evaluated
        if (scope is null)                                // Tier-0 fallback path / tests
            return factory(SwiftHost.Services!, arg);

        var key = scope.KeyFor(callSite);                 // positional identity within the current group
        if (scope.TryReuse<T>(key, out var existing))
        {
            scope.ApplyParameters(existing, arg);         // re-apply parent data; State<T> is untouched
            return existing;
        }

        var created = factory(scope.Services, arg);       // construct + [Inject] fill
        scope.Retain(key, created);                       // cache; schedule OnAppear
        return created;
    }
}
```

> **The hard, unresolved part is `KeyFor` / positional identity.** Compose forms keys from call
> position within the parent group and needs explicit `key(...)` markers around conditionals and loops
> so a moved/removed call doesn't shift every sibling's identity. We inherit that problem: `Body` today
> is a plain property with no ambient composer, so Tier 1 requires setting `RenderScope.Current` around
> `Body` evaluation and defining the group/keying rules for `if`/`foreach`. This is the real design
> cost and should not be under-sold — it is the same "reconciliation" milestone (DI proposal §8),
> approached from the construction side.

## 8. Costs, gotchas, decisions

**Costs / gotchas**
- **Global-namespace pollution & collisions.** `List()`, `Text()`, `Section()`, `Menu()`, `Path()`,
  `Group()` as free functions collide with BCL types and each other; `List` the view vs `List<T>` the
  collection is already a known SwiftUI-in-C# pain. Mitigation: a dedicated static host + selective
  `using static`, and generator diagnostics on collisions.
- **Tooling regresses slightly.** `new WeatherView(` gives clean per-type ctor IntelliSense; a flat
  sea of global functions is a weaker "what can I type here" story.
- **Positional identity needs stable call order** (Tier 1) — `if`/`foreach` in `Body` require group
  keys, or state teleports between siblings. This is the crux design problem.
- ~~**Two-doc consistency.**~~ **Resolved 2026-07-19** — both docs state *constructor = parent data,
  `[Inject]` = services* (§5.3).

**Decisions to ratify**
1. **Adopt the function form at all?** Or keep `new` and treat the seam as internal-only. *Rec: adopt,
   Tier 0 first — it is cheap, reversible, and unlocks everything else.* **← still open; this is now
   the only load-bearing question in this doc, since DI no longer depends on it.**
2. **Generator vs hand-written forwarders.** *Rec: generator — it is the only way custom controls stay
   uniform with built-ins.*
3. ~~**Service delivery: `[Inject]` properties or ctor injection via `ActivatorUtilities`?**~~
   **Ratified: `[Inject]` properties**, generator-emitted, reflection-free — and shipped in DI Phase 1,
   not here (§5.2).
4. **Tier 1 memo-key strategy** — pure positional (Compose-style, needs group keys) vs
   explicit-key-required for retained children. *Rec: positional with an opt-in `Key(...)` escape hatch;
   sequence behind the reconciliation milestone.*
5. **Naming-collision policy** — reserved host class, analyzer warnings, or per-type `using static`.

## 9. Sequencing (tracks the DI proposal phases)

| Phase | Deliverable | Depends on |
|-------|-------------|------------|
| **0** | Source generator emits **Tier 0** forwarders for all `View` subclasses; adopt `Views` static import in the sample. Pure sugar, no behavior change. | Nothing. Additive; `new` keeps working. |
| **1** | `ViewFactory.Create` with a **no-retention** path (constructs each render, calls the existing `IInjectable.Inject`). Extends `[Inject]` to inline children. | DI proposal Phase 1 — which already ships the `[Inject]` generator, `SwiftHost`, and the container. |
| **2** | `RenderScope.Current` around `Body`; **positional retention** + `OnAppear`/`OnDisappear` on mount/unmount; group/`Key(...)` rules for `if`/`foreach`. This is the reconciliation unlock. | DI proposal Phase 2 (dispatcher, lifecycle). |
| **3** | Scoped-per-retained-view lifetimes (per-page / per-row `IServiceScope`), riding on the retained instances from Phase 2. | DI proposal Phase 3. |

**Bottom line:** the function form is worth adopting — but as the **construction seam that unlocks
reconciliation**, delivered via a source generator, with DI riding along at the composite layer. It is
not, and should not be sold as, a DI mechanism for leaves.
