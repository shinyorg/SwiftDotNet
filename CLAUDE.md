# CLAUDE.md

Guidance for Claude Code when working in this repository.

## What this project is

SwiftDotNet: write declarative UI once in C# and render it as **real native UI** on every platform (SwiftUI on
iOS/macOS/tvOS, Jetpack Compose on Android, GTK4 on Linux, WinUI 3 on Windows, HTML/DOM on Web), plus a
self-drawing **SkiaSharp** backend. One `Core` (DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol,
`SwiftApp`), two backend routes (native shim for compiler-plugin toolkits; pure-C# interpreter for bindable
ones). Read [`docs/architecture.md`](docs/README.md) before making cross-cutting changes.

## Documentation is part of "done" — document every feature the same way

The reference docs live in **[`docs/`](docs/README.md)**, split into logical sections. **Whenever you add or
materially change a feature, update the docs in the same change** — the same way the existing pages are
written. Treat docs as part of the definition of done, not a follow-up.

### Where a change goes

Match the feature to the section it belongs to (add a new page only for a genuinely new area):

| Feature area | Page |
|---|---|
| A new view / control | [`docs/views-and-controls.md`](docs/views-and-controls.md) |
| A modifier, gesture, or animation | [`docs/modifiers-gestures-animation.md`](docs/modifiers-gestures-animation.md) |
| State / binding behavior | [`docs/state-and-binding.md`](docs/state-and-binding.md) |
| `List` / CollectionView | [`docs/collection-view.md`](docs/collection-view.md) |
| Environment cascade / styles / theme | [`docs/global-styles.md`](docs/global-styles.md) |
| Custom-control / renderer-registry seam | [`docs/custom-controls.md`](docs/custom-controls.md) |
| A new backend, or backend-specific behavior | [`docs/backends/`](docs/backends/README.md) (overview + per-backend page) |
| Maps companion | [`docs/maps.md`](docs/maps.md) |
| Anything cross-cutting / architectural | [`docs/architecture.md`](docs/architecture.md) |
| A build/run step | [`docs/getting-started.md`](docs/getting-started.md) |
| Planned / deferred work | [`docs/roadmap.md`](docs/roadmap.md) |

### The house style for a feature (per-feature checklist)

Each documented feature should cover, briefly:

1. **What it is** — one or two sentences, and the SwiftUI analog if there is one.
2. **How to use it** — a short, real C# snippet in the DSL's fluent style (mirror the sample
   [`ContentView`](sample/SharedUI/ContentView.cs), not invented APIs).
3. **Per-backend behavior** — a small table when the mapping differs by platform, and call out any **no-ops or
   fallbacks** explicitly (e.g. `.ScaleEffect` is a no-op on GTK; tvOS control fallbacks).
4. **Gotchas** — the non-obvious constraints (wire encodings, ordering, feedback-loop guards, build quirks).
5. **Status** — verified vs. scaffolded/deferred; link the relevant [`plans/`](plans) doc for unfinished work.
6. **Cross-links** — link related pages with relative links, and link source files with a repo-relative path
   (e.g. `` [`Foo.cs`](src/SwiftDotNet/Core/Foo.cs) ``) so they're clickable in the repo.

### Rules

- **Links must be repo-relative** so they work rendered on GitHub (these docs are meant to be browsed there).
- **Keep [`docs/README.md`](docs/README.md) (the index) and the platform matrix in sync** when you add a page
  or change a backend's status.
- The **status tables must stay honest** — `✅ Verified` only for things actually run; use `🧩 Scaffolded` /
  "deferred" otherwise, and say *where* it was verified (simulator, emulator, Chrome, …).
- The **top-level [`README.md`](README.md)** is the pitch/overview; **`docs/` is the detailed reference.** Keep
  the README's "Documentation" link list current when pages are added or renamed.
- When docs and code disagree, fix the docs in the same change.

## Repo conventions worth knowing

- Everything shared is in [`src/SwiftDotNet/Core`](src/SwiftDotNet/Core) and must stay dependency-free and
  trim/AOT-safe (JSON is hand-rolled in `NodeJson.cs` on purpose — no reflection).
- GTK / Web / Skia are **separate** projects (they share the `net10.0` TFM with Core, so they can't fold into
  the multi-target library without forcing their dependency on every consumer).
- Apple targets need the app `.csproj` to `<Import>` `SwiftDotNetBridge.targets` (`NativeReference` doesn't
  flow transitively).
- Tests live in [`tests/SwiftDotNet.Tests`](tests/SwiftDotNet.Tests); Skia is the most thoroughly
  test-verified backend. Prefer adding a Core/Skia test for new behavior since those run on macOS.
- Long-lived project context is also kept in Claude's memory index; the docs are the canonical human-facing
  source of truth.
