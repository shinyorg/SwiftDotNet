# Proposal: Navigation service (DI-driven, imperative)

**Status:** Paused — split out of [`dependency-injection-proposal.md`](./dependency-injection-proposal.md)
on 2026-07-19 so DI Phase 1 could land without it. Not scheduled.
**Depends on:** DI Phase 1 (`SwiftHost`, the container, `[Inject]`) — already built.

> **Why it was paused.** Navigation is the feature that *consumes* DI rather than defines it, and it is
> the single largest piece of host glue in the DI proposal (every backend must forward its back gesture).
> Landing DI without it keeps that surface out of Phase 1. The scoped-per-page `IServiceScope` work
> described below is the motivating use case for `ViewScope`, which shipped with DI Phase 1 and is ready
> for this plan to pick up.

---

## 1 The gap today

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

## 2 The contract

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

## 3 How it renders — `NavigationStack` reads the navigator

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
        var nav = (Navigator)SwiftHost.Require<INavigator>();
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

## 4 DI angle — typed routes and scoped-per-page lifetimes

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

## 5 Per-backend mapping

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

## 6 Testing

The navigator is an ordinary injectable, so navigation is asserted headlessly with no host:

```csharp
var nav = new Navigator(dispatcher: Immediate);
var sp = new ServiceCollection()
    .AddSingleton<INavigator>(nav)
    .AddTransient<DetailView>()
    .BuildServiceProvider();
SwiftHost.Services = sp;

await nav.PushAsync<DetailView>();
Assert.Equal(2, nav.Depth);
Assert.IsType<DetailView>(nav.Stack[^1]);
await nav.PopAsync();
Assert.Equal(1, nav.Depth);
```

## 7 Surface summary

| Component | Change |
|-----------|--------|
| **New** `Core/Navigation/INavigator.cs`, `Navigator.cs`, `PresentationStyle`. | The contract + a dispatcher-backed stack/modal implementation. |
| `Core/Views/Navigation.cs` | `NavigationStack` renders `INavigator.Stack`; `NavigationLink` re-expressed over `INavigator.PushAsync`; reports host back → `pop`. |
| `SwiftDotNetAppBuilder` | Auto-register `INavigator` (singleton) in `Build()` — like MAUI's implicit `Shell`/navigation services; optional `AddRoute<T>(key)`. |
| `PushAsync<T>()` | Resolve from the container **and** run `if (v is IInjectable i) i.Inject(sp);` — pushed pages are retained, so `[Inject]` is correct there today (§4.3). |
| Per-platform hosts | Map stack diff → native push/pop; report hardware/gesture back as a `pop` event. |

## 8 Where it sits in the phases

| Phase | Navigation deliverable |
|-------|------------------------|
| **1** | `INavigator` registered; `PushAsync(View)` / `PopAsync` / `PushAsync<T>()`; `NavigationStack` renders the stack. Synchronous render (no dispatcher yet — push must originate on the UI thread for now). |
| **2** | Dispatcher-safe mutation (push/pop from any thread); `PresentAsync` + `ConfirmAsync`/`AlertAsync` result dialogs; host back-gesture sync; `OnAppear`/`OnDisappear` fire on push/pop. |
| **3** | Per-page `IServiceScope` (scoped view-models disposed on pop); string/deep-link route registry; URL/intent restore. |

Navigation intentionally tracks the DI phases 1:1 — it's the feature that *consumes* each phase's new
capability (ambient provider → threading → scoped lifetimes), which is a good forcing function for
getting each phase's shape right.

---
