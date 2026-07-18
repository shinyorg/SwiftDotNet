# Plan: Windows / Scenes for SwiftDotNet (multi-window)

**Status:** Draft for review
**Date:** 2026-07-18
**Scope:** A declarative **`Scene` / `Window` / `WindowGroup`** layer that lets an app declare and open more
than one top-level window from C#, mapped onto real windows on macOS, Windows (WinUI), and Linux (GTK);
onto scenes on iPadOS; and onto sensible degradations everywhere else (iPhone, tvOS, Android, Web).

> **Interpretation note.** "Windows" here means the *UI concept* of a top-level window (SwiftUI's
> `WindowGroup` / `Window` / `Settings` scenes), **not** the Windows OS backend (which already exists —
> see [[swiftdotnet-windows]]). If you meant something else, stop me here.

---

## 1. Goal

Today every backend hosts exactly one root view in one implicit window. The whole app is
`AppRoot.Create() => new ContentView()` — a single tree, a single window, created for you by the reusable
host bases (`SwiftDotNetAppDelegate`, `SwiftDotNetApplication`, `SwiftDotNetActivity`). There is no way to
open a second window, a document window, an inspector, or a preferences panel.

The target is a scene declaration plus a programmatic open/close, mirroring SwiftUI:

```csharp
public sealed class MyApp : App
{
    public override Scene Body =>
        new SceneSet(
            // The primary window (auto-created at launch). Data-driven: one window per open document.
            new WindowGroup<Document>(doc => new DocumentView(doc)),

            // A single, unique auxiliary window opened on demand.
            new Window("Inspector", id: "inspector", () => new InspectorView())
                .DefaultSize(320, 640)
                .Resizable(false),

            // macOS/Windows/GTK preferences.
            new Settings(() => new SettingsView())
        );
}
```

```csharp
// Opening / closing from any view — ambient actions, no plumbing:
public override View Body =>
    new VStack(
        new Button("Inspect",  () => this.OpenWindow("inspector")),
        new Button("New Doc",  () => this.OpenWindow(new Document())),   // WindowGroup<Document>
        new Button("Close",    () => this.DismissWindow())
    );
```

Same C# → real `NSWindow`s on macOS, `Microsoft.UI.Xaml.Window`s on WinUI, `Gtk.Window`s on Linux,
`UIWindowScene`s on iPad, and graceful fallbacks (sheet / new tab / single-window) where the platform can't
give you a real second window.

## 2. Why windows are the interesting case

Windowing is the first feature that lives **above** the render tree instead of inside it, and it stresses
the framework in three ways nothing so far has:

1. **The app is no longer a single root.** Every backend hard-assumes *one* root view → *one* window →
   *one* bridge/store. `AppRoot.Create()`, `SwiftDotNetHost.CreateRoot*`, and each `IBridge` are all
   singular. Multi-window means **N independent render trees** alive at once, each with its own diff state
   and its own native window — a structural change to hosting, not a new control.

2. **Windows are created outside the diff.** A window's title, size, position, and style aren't node
   props you patch — they're set when the OS window is born. We need a **window-creation channel** parallel
   to the existing node-patch channel, plus a **window registry** keyed by window id.

3. **Opening a window is an imperative side effect from declarative code.** `OpenWindow`/`DismissWindow`
   are *actions*, like SwiftUI's `@Environment(\.openWindow)`. They need an **ambient window service**
   reachable from any view without threading it through — exactly the ambient-locator seam the DI proposal
   introduces ([[dependency-injection-proposal]]).

These are real framework changes (§5) and they generalize: the per-window store work also unlocks
independent state scopes, and the ambient action seam is reused by DI and by the animation transaction API.

## 3. Where it plugs in

Windows sit at the **app-hosting** layer (`Platforms/*/SwiftDotNet*AppDelegate|Application|Activity`,
`SwiftDotNetHost`, `AppRoot`), not the interpreter. The plan is **additive and back-compatible**:

- A bare root view keeps working — `AppRoot.Create() => new ContentView()` is treated as sugar for
  `new WindowGroup(() => new ContentView())` with one instance. Existing samples don't change.
- Apps that want more override `App.Body` to return a `Scene`. The host bases learn to bootstrap a
  `Scene` (create the primary window(s)) instead of a single root controller.
- Each **open window is a fresh, independent instance of the existing single-window pipeline** — its own
  root `View`, its own `TreeDiffer`, its own `IBridge` target, its own native window. We are multiplying
  the thing we already have, not redesigning it.

## 4. Public API

```csharp
// The app declares scenes instead of (or in addition to) a single root.
public abstract class App { public abstract Scene Body { get; } }

public abstract class Scene { }

// A window type that may have many live instances, optionally one-per-data-value.
public sealed class WindowGroup : Scene
{
    public WindowGroup(Func<View> content);
    public WindowGroup Title(string title);
    public WindowGroup DefaultSize(double w, double h);
}
public sealed class WindowGroup<TData> : Scene           // one window instance per opened value
{
    public WindowGroup(Func<TData, View> content);
}

// A single, unique window (opening it again just focuses the existing one).
public sealed class Window : Scene
{
    public Window(string title, string id, Func<View> content);
    public Window DefaultSize(double w, double h);
    public Window Resizable(bool resizable = true);
    public Window Style(WindowStyle style);              // Titled | Plain | Utility
}

// macOS/Windows/GTK preferences window; degrades to a pushed screen elsewhere.
public sealed class Settings : Scene { public Settings(Func<View> content); }

// Group several scenes.
public sealed class SceneSet : Scene { public SceneSet(params Scene[] scenes); }

// Ambient actions (extension methods on View — resolved via the ambient window service).
public static class WindowActions
{
    public static void OpenWindow(this View view, string id);
    public static void OpenWindow<TData>(this View view, TData value);   // WindowGroup<TData>
    public static void DismissWindow(this View view);                    // closes the caller's window
    public static void DismissWindow(this View view, string id);
}
```

Window configuration (`Title`/`DefaultSize`/`Resizable`/`Style`) is applied **at creation**; changing it
later is an explicit imperative call, not a node patch — consistent with how windows behave on every OS.

## 5. Framework prerequisites (the real work)

### 5.1 An App/Scene layer above the single-root host

- New `App` + `Scene` types in Core (pure, no platform deps — just a declaration the host walks).
- `AppRoot` gains an optional `App CreateApp()` alongside today's `View Create()`; if only a view is
  supplied, wrap it in a default `WindowGroup`. Zero-change migration for existing samples.
- Each host base (`SwiftDotNetAppDelegate` macOS/iOS/tvOS, `SwiftDotNetApplication` Windows,
  `SwiftDotNetActivity` Android) learns to **enumerate the primary scene(s)** at launch and create the
  first window, instead of hard-coding one `CreateRoot()`.

### 5.2 A window registry + N independent pipelines

The core structural change. Introduce an `IWindowHost` per backend and a `WindowManager` that:

- keys **live windows by id** (or by `(groupId, dataValue)` for `WindowGroup<T>`);
- for each window owns an **independent `(rootView, TreeDiffer, IBridge, nativeWindow)` quad** — the exact
  pipeline that exists today, instantiated once per window;
- routes `OpenWindow`/`DismissWindow` to create/tear down a quad.

The single-window assumption lives in three places that each need a per-window instance instead of a
singleton:
- **Pure-C# backends (GTK / WinUI / Web):** cheap — `IBridge` is already an instance; just create one per
  window and give each its own native `Gtk.Window` / `Microsoft.UI.Xaml.Window` / render root.
- **Native-shim backends (iOS/macOS/tvOS Swift, Android Compose):** the shim currently holds **one global
  `@Observable` store**. Each window needs its **own store**. `swiftdotnet_make_host_controller` already
  returns a fresh controller per call — extend it so each controller **owns an independent store**, and
  make `swiftdotnet_render`/patch entry points take a **window/host handle** (or become instance methods on
  the returned host) instead of writing the global store. This is the largest single piece of work and the
  main risk (§8).

### 5.3 Ambient window actions

`OpenWindow`/`DismissWindow` need to reach a `IWindowService` from any view without constructor plumbing.
Reuse the DI proposal's ambient locator: `view.Service<IWindowService>().Open(id)`, surfaced as the tidy
`view.OpenWindow(id)` extension. On backends where actions must marshal to the UI thread, dispatch through
`ISwiftDispatcher` (also from the DI plan). No new cross-cutting mechanism — windows are the first real
consumer of both seams. [[dependency-injection-proposal]]

### 5.4 Window lifecycle events (Phase 2+)

`OnWindowClosed`, `OnWindowFocused` surfaced as scene-level callbacks so an app can react (save on close,
etc.). These ride the existing event channel (window id as the source id).

## 6. Per-backend implementation

| Backend | Real multi-window? | Window primitive | Approach & degradation |
|---------|-------------------|------------------|------------------------|
| **macOS** (SwiftUI/AppKit) | ✅ Full | `NSWindow` + `NSHostingController` per window | Best fit. `WindowManager` creates an `NSWindow` per quad (reuses the existing collapse-to-intrinsic sizing fix). `Settings` → real Preferences window; `Window(id)` → unique window. |
| **Windows** (WinUI 3) | ✅ Full | `Microsoft.UI.Xaml.Window` per window | Strong fit, like macOS. One `Window` object per quad; `Settings` → a normal secondary window. First-build API checks needed (see [[swiftdotnet-windows]]). |
| **Linux** (GTK4) | ✅ Full | `Gtk.Window` / `Gtk.ApplicationWindow` | Strong fit. `WindowManager` adds windows to the `Gtk.Application`; each hosts an independent `GtkBridge` widget tree. `Settings` → a normal window. |
| **iPadOS** (SwiftUI/UIKit) | ⚠️ Scenes | `UIWindowScene` via `requestSceneSessionActivation` | Real multi-window on iPad only. Needs a `UISceneDelegate` that builds a host controller per scene. `WindowGroup<T>` → one scene per document. |
| **iPhone** (SwiftUI/UIKit) | ❌ Single | one `UIWindow` | No user windows. `OpenWindow` degrades to a **full-screen cover / pushed screen**; primary `WindowGroup` only. |
| **tvOS** (SwiftUI/UIKit) | ❌ Single | one `UIWindow` | Full-screen only. Secondary windows degrade to `fullScreenCover`/sheet; `Settings` → a pushed screen. Same degrade pattern as its control fallbacks ([[swiftdotnet-tvos]]). |
| **Android** (Compose) | ⚠️ Limited | secondary `Activity` **or** Compose `Dialog` | No desktop-style windows. `OpenWindow(id)` → launch a secondary `SwiftDotNetActivity` hosting that scene's root (real "window" in recents / split-screen on Android 12+), **or** a full-screen `Dialog` for lighter cases. `Settings` → a pushed screen or secondary activity. |
| **Web** (Blazor) | ⚠️ Simulated | in-page floating window **or** `window.open` | No OS windows. Reference = an **in-page window manager**: each open window is a draggable/resizable absolutely-positioned host `<div>` with its own `SwiftDotNetView` render root (independent bridge). `window.open` popups are a harder alt (needs a second Blazor root) — offer as opt-in. `Settings` → a modal window. |

**Default posture: degrade, never crash.** A backend that can't give a real second window renders the
scene's content in the best local substitute (cover / dialog / pushed screen / floating div) and
`DismissWindow` maps to the matching dismissal. `Window(id)` "open again" always **focuses the existing
instance** rather than duplicating.

## 7. Interplay with other milestones

- **DI proposal** ([[dependency-injection-proposal]]): windows are the first real consumer of the ambient
  `View.Service<T>()` locator and `ISwiftDispatcher`. A per-window **scoped DI container** is a natural
  extension (each window = a DI scope).
- **Per-view state ownership** (Core next step): the per-window store work in §5.2 is a stepping stone —
  independent stores per window generalize toward independent state scopes per subtree.
- **Animations** ([[animations-plan.md]] via the plans folder): window present/dismiss transitions compose
  with the animation transaction work; the render-batching that plan needs also benefits multi-window
  (batch per window).
- **Navigation**: `NavigationStack` is *within* a window; windows are *above* nav. Data-driven
  `WindowGroup<T>` is the multi-window analog of `NavigationLink(value:)` — worth aligning the value-routing
  APIs so they read the same.

## 8. Phased delivery

| Phase | Deliverable | Backends | Risk |
|-------|-------------|----------|------|
| **1** | `App`/`Scene`/`WindowGroup`/`Window(id)` model; window registry + per-window pipeline; `OpenWindow(id)`/`DismissWindow`; back-compat single-window wrap | **macOS + GTK** first (real windows, pure/near-pure C# hosting), then **WinUI** | **High** — the per-window store refactor of the native shim (§5.2) is the core lift; do macOS to prove it, GTK to prove the pure-C# path. |
| **2** | Data-driven `WindowGroup<TData>`; window config (`DefaultSize`/`Resizable`/`Style`/`Title`); window lifecycle events; **iPad** multi-scene | macOS, WinUI, GTK, iPad | Med–high |
| **3** | `Settings` scene; `MenuBarExtra` (macOS); **Web** in-page window manager; **Android** secondary-activity windows; **iPhone/tvOS** degradations formalized | all | Med (Web WM + Android activities are the fresh work) |

Phase 1 puts a real second `NSWindow`/`Gtk.Window` on screen from C# and proves the N-pipelines
architecture. Phase 2 makes document windows and window sizing real. Phase 3 fills in the platforms that
only *simulate* windows.

## 9. Worked example — a document app with an inspector

```csharp
public sealed class DocApp : App
{
    public override Scene Body =>
        new SceneSet(
            new WindowGroup<Document>(doc => new DocumentView(doc)).Title("Document"),
            new Window("Inspector", id: "inspector", () => new InspectorView())
                .DefaultSize(300, 600).Resizable(false),
            new Settings(() => new PreferencesView())
        );
}

public sealed class DocumentView : View
{
    readonly Document _doc;
    public DocumentView(Document doc) => _doc = doc;

    public override View Body =>
        new VStack(
            new Text(_doc.Title).Font(Font.Title),
            new Button("Open Inspector", () => this.OpenWindow("inspector")),
            new Button("New Document",   () => this.OpenWindow(new Document())),
            new Button("Close",          () => this.DismissWindow())
        ).Spacing(12).Padding();
}
```

On macOS this is three real windows (document windows, a unique inspector, a Preferences panel); on iPad
each document is a separate scene; on iPhone/tvOS the inspector is a cover and there's one document at a
time; on the Web each is a draggable in-page window.

## 10. Decisions needed

1. **App model migration:** introduce `App.Body => Scene` as **additive** (keep `AppRoot.Create()` as a
   single-window shortcut), or fully replace the entry point? *Rec: additive — a bare view auto-wraps in a
   `WindowGroup`, so every existing sample keeps working untouched.*
2. **Native-shim per-window store (§5.2):** make `swiftdotnet_render`/patch take a **host handle** so each
   host controller owns its store, vs. keeping a global store and disallowing native multi-window in v1.
   *Rec: do the host-handle refactor — it's the whole point, and macOS is where multi-window matters most.*
3. **Web strategy:** ship the **in-page floating window manager** as the reference, or invest in real
   `window.open` popups (second Blazor root)? *Rec: in-page WM first; `window.open` as a later opt-in.*
4. **Android strategy:** map `OpenWindow` to a **secondary Activity** (real task/split-screen window) or a
   **Compose Dialog** (lighter, in-process)? *Rec: Dialog for simple auxiliaries, secondary Activity behind
   an opt-in flag for true multi-window.*
5. **Scope of v1:** limit Phase 1 to **`Window(id)` (unique auxiliary windows)** and defer data-driven
   `WindowGroup<T>` to Phase 2? *Rec: yes — unique windows exercise the full architecture with the simplest
   API surface.*
```