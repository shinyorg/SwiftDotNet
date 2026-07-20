# Hot Reload

Edit a `Body`, save, and watch the running app update in place — without losing the page you pushed, the
text you typed, or the row you scrolled to.

SwiftDotNet gets this from stock .NET hot reload (`dotnet watch`), so it works on **every backend** rather
than being a per-platform feature. There is no custom file watcher, no Roslyn re-compile step, and no
designer process.

## What it is

The .NET runtime can push edited method bodies into a live process. It notifies interested libraries
through a [`MetadataUpdateHandler`](https://learn.microsoft.com/dotnet/api/system.reflection.metadata.metadataupdatehandlerattribute).
SwiftDotNet registers one — [`HotReload`](../src/SwiftDotNet/Core/HotReload.cs) — whose entire job is to
call [`SwiftApp.Invalidate()`](../src/SwiftDotNet/Core/SwiftApp.cs).

The reason that is enough is the shape the runtime already had:

| Property | Where it comes from | Why it matters here |
|---|---|---|
| The root `View` instance is retained for the app's life | [`SwiftDotNetApp.CreateRoot()`](../src/SwiftDotNet/Core/Hosting/SwiftDotNetApp.cs) caches it | An edit to `Body` is picked up with no re-instantiation |
| `Body` is re-evaluated on *every* render | [`View.ToNode`](../src/SwiftDotNet/Core/View.cs) | The new code runs on the very next pass |
| `State<T>` cells are fields on that retained instance | [`State<T>`](../src/SwiftDotNet/Core/State.cs) | **State survives the reload** — the SwiftUI-preview behaviour |
| Every backend consumes patches, not view objects | [`IBridge`](../src/SwiftDotNet/Core/IBridge.cs) | A reload is just a bigger patch; no backend knows it happened |

`Invalidate()` drops the diff baseline so the next render emits a single `replace` of the root instead of
diffing against a tree the *old* code built. That matters: an edit can change the tree's shape anywhere, so
the retained node ids no longer line up and a diff would produce a subtly wrong patch. A full replace is
correct by construction and fast enough for a dev loop (45 ms end-to-end on Skia, measured).

## How to use it

Run the app under `dotnet watch` instead of `dotnet run`:

```bash
dotnet watch run --project sample/SampleApp.Skia.Silk
```

Then edit any `Body` and save:

```csharp
View RatingPage() =>
    new ScrollView(
        new Text("Rating").Font(Font.LargeTitle),
        new Text("★").Font(Font.Title).ForegroundColor(Color.Accent),
        new Rating(_rating)                        // ← add a row, retitle, restyle: all live
    ).Padding(20).NavigationTitle("Rating");
```

Nothing in your app code needs to opt in. If you want to react to a reload — a dev-only banner, re-seeding
a fake service — subscribe to the event:

```csharp
HotReload.Reloaded += types =>
    Console.WriteLine($"[hot reload] applied ({types?.Length ?? 0} updated types)");
```

## What reloads and what restarts

Verified against the sample app on Skia (.NET 10):

| Edit | Result |
|---|---|
| Change a string, colour, font, padding, or any modifier | 🔥 Reloaded |
| Add or remove views in a `Body` (a structural change) | 🔥 Reloaded |
| Change the logic inside an event handler or a computed property | 🔥 Reloaded |
| **Add a `State<T>` field to an existing view** | 🔥 Reloaded — .NET 10 supports adding instance fields |
| Change a method signature, or add/remove a method | ♻️ Rude edit → `dotnet watch` restarts the app; state is lost |
| Add a new type, or change a type's base class or interfaces | ♻️ Rude edit → restart |
| Edit `SwiftProgram.CreateSwiftApp()` (the DI graph) | 🔥 Applies, but the container was already built — restart to see it |

Rude edits are a .NET runtime constraint, not a SwiftDotNet one. `dotnet watch` handles them by rebuilding
and relaunching, so the loop keeps working; you just start from the app's first screen again.

## Per-backend status

| Backend | How to run it | Extra setup | Status |
|---|---|---|---|
| **Skia** (Silk.NET window) | `dotnet watch run --project sample/SampleApp.Skia.Silk` | none | ✅ **Verified** — string, structural, and added-field edits all applied live (45–282 ms) |
| **Skia** (macOS window / MAUI) | `dotnet watch run` on the head | none | 🧩 Expected — shares the Skia bridge, not separately run |
| **Linux / GTK** | `dotnet watch run --project sample/SampleApp.Gtk` | none | 🧩 Expected — plain `net10.0`, same as Skia; not run (needs Linux) |
| **Web** (Blazor WASM) | `dotnet watch --project sample/SampleApp.Web` | none | 🧩 **Partly verified** — `dotnet watch` compiled and applied the delta ("changes applied in 225 ms"), but no browser was attached to confirm the DOM re-rendered |
| **Apple** (iOS / tvOS) | An IDE's hot-reload command (VS / VS Code / Rider) | **`UseInterpreter=true`** (see below) | ⚠️ **Not working from the CLI** — `dotnet watch` got the app installed and launched on the simulator, but it aborted at startup: `Socket error while connecting to IDE on 127.0.0.1:10000: Connection refused` |
| **Apple** (macOS) | `dotnet watch run -f net10.0-macos` | none expected | 🧩 Expected; not run |
| **Android** | `dotnet watch run -f net10.0-android` | none expected | 🧩 Expected; not run (needs an emulator) |
| **Windows / WinUI** | `dotnet watch run -f net10.0-windows...` | none expected | 🧩 Expected; not run (needs Windows) |

Nothing backend-specific is required to *support* hot reload — a reload is an ordinary `replace` patch, and
all five bridge implementations already handle one mid-session. That is covered by a test, not just by
inspection: `SkiaSurvivesAReload_TreeIsRebuiltAndStillHitTests` in
[`HotReloadTests.cs`](../tests/SwiftDotNet.Tests/HotReloadTests.cs) replaces the whole tree mid-session and
then asserts the rebuilt tree still lays out and routes taps.

## Gotchas

**Apple targets: use an IDE, not bare `dotnet watch`.** This is the one place the story does not currently
work end-to-end, and the blocker is in the Apple SDK rather than in SwiftDotNet.

First, the SDK hard-errors without the Mono interpreter:

> `error : Can't use Hot Reload or 'dotnet watch' unless the interpreter is enabled. Set 'UseInterpreter=true' in the project file to use the interpreter.`

[`SampleApp.csproj`](../sample/SampleApp/SampleApp.csproj) has this behind an **opt-in** property, so
ordinary debug deploys are not silently switched to the interpreter:

```xml
<PropertyGroup Condition="'$(SwiftDotNetHotReload)' == 'true' And ($(TargetFramework.EndsWith('-ios')) Or $(TargetFramework.EndsWith('-tvos')))">
    <UseInterpreter>true</UseInterpreter>
</PropertyGroup>
```

```bash
dotnet build sample/SampleApp/SampleApp.csproj -f net10.0-ios -p:SwiftDotNetHotReload=true
```

With that set, the app builds, installs, and launches on the simulator — and then **aborts during startup**:

```
Microsoft.iOS: Socket error while connecting to IDE on 127.0.0.1:10000: Connection refused
… mono_runtime_run_startup_hooks → xamarin_unhandled_exception_handler → abort
```

The interpreter build expects an IDE-side debug/hot-reload tunnel that bare `dotnet watch` does not stand
up. Drive hot reload from Visual Studio, VS Code, or Rider on Apple targets until that changes.

Two further CLI quirks worth knowing if you retry this: `dotnet watch` needs `--device <UDID>` when more
than one device or simulator is visible, and it resolves the `.app` path relative to the *current
directory*, so run it from the project folder rather than the repo root or you get
`error MT0069: The app directory ... does not exist`.

**Your host needs a `SynchronizationContext`.** The runtime applies the update on its own thread, so
`Invalidate()` is raised off the UI thread. `SwiftApp` marshals the render onto whichever context was
current at `SwiftApp.Run` — but if there isn't one, it renders *inline on the agent thread*, concurrently
with your paint loop. Backends with a real UI pump (UIKit, AppKit, GTK, WinUI, Blazor) get this for free.
Bare windowing hosts do not: see `RenderLoopSyncContext` in
[`SampleApp.Skia.Silk/Program.cs`](../sample/SampleApp.Skia.Silk/Program.cs) for the ~20 lines needed.
This is worth doing regardless of hot reload — it is also what makes the documented "mutate `State` from a
background timer or socket" promise true for that host.

**Release builds are unaffected.** Everything in `HotReload` is gated on `MetadataUpdater.IsSupported`,
which the runtime reports false for Release, trimmed, and AOT builds — so the trimmer removes the whole
path rather than shipping it. There is deliberately no `#if DEBUG`, which would bake the decision into the
NuGet package instead of leaving it to the consumer's build configuration.

**Renderer registries are not flushed.** `HotReload.RegisterCacheFlush` exists for caches an edit can
invalidate, but the renderer registries
([`SkiaRenderers`](../src/SwiftDotNet.Skia/SkiaRenderers.cs) and friends) are deliberately left alone: they
hold startup registration state that would never re-run, and an edited renderer keeps its identity while
picking up the new method bodies anyway. Clearing them would break the
[custom-control seam](custom-controls.md), not refresh it.

## Status

✅ **Verified** on Skia/Silk (macOS, .NET 10): string, structural, and added-field edits applied live with
state preserved; a signature change correctly fell back to a `dotnet watch` restart. Covered by five tests
in [`HotReloadTests.cs`](../tests/SwiftDotNet.Tests/HotReloadTests.cs).

🧩 **Expected but unverified** on GTK, Web (delta applied, DOM not observed), macOS, Android, and Windows —
the mechanism is backend-agnostic, but the status table above stays honest until each one is actually run.

⚠️ **Blocked on iOS/tvOS from the CLI** — see the gotcha above. Not a SwiftDotNet defect: the app never
reaches `SwiftApp.Run`, so none of the code on this page is even involved.

## See also

- [Architecture](architecture.md) — the render pass, `TreeDiffer`, and the patch protocol a reload rides on
- [State & Data Binding](state-and-binding.md) — why `State<T>` survives a reload
- [Getting Started](getting-started.md) — building and running each head
- [Custom Controls](custom-controls.md) — the renderer registry that hot reload deliberately leaves intact
