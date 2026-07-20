# SwiftDotNet Documentation

**SwiftUI for .NET — everywhere.** Write declarative UI once in C# and render it as *real* native UI on
every platform: SwiftUI on iOS/macOS/tvOS, Jetpack Compose on Android, GTK4 on Linux, WinUI 3 on Windows,
and HTML/DOM on the Web — plus a self-drawing SkiaSharp backend for a pixel-identical look anywhere.

This is the documentation set. Start with **[Getting Started](getting-started.md)**, then read
**[Architecture](architecture.md)** to understand how one C# view tree becomes native UI on every backend.

## Contents

### Start here
- **[Getting Started](getting-started.md)** — install prerequisites, build the native bridges, and run the
  sample app on each platform.
- **[Architecture](architecture.md)** — the Core, the diff engine, the patch/event protocol, and the two
  backend routes (native shim vs. pure-C# interpreter).
- **[Hot Reload](hot-reload.md)** — edit a `Body` and see it live under `dotnet watch`, with `State<T>`
  preserved across the reload.

### Authoring UI
- **[Hosting & Dependency Injection](hosting-and-di.md)** — `SwiftProgram.CreateSwiftApp()`, the builder,
  `[Inject]` services, view lifecycle, and the `UseX()` seam.
- **[Views & Controls](views-and-controls.md)** — the full vocabulary: layout, navigation & presentation,
  inputs, and display views.
- **[Modifiers, Gestures & Animation](modifiers-gestures-animation.md)** — the universal modifier pass,
  one-shot gestures, and implicit animation.
- **[State & Data Binding](state-and-binding.md)** — `State<T>`, two-way bindings, and the re-render loop.
- **[Collection View (`List`)](collection-view.md)** — keyed identity, recycling, virtualization, selection,
  grids, sections, refresh, and load-more.
- **[Global Styles](global-styles.md)** — the environment cascade, control-style protocols, reusable
  modifier bundles, and design tokens (`Theme`).
- **[Custom Controls](custom-controls.md)** — composite views (the common case) and custom native primitives
  via the renderer registry.
- **[The Controls Library](controls-library.md)** — the `SwiftDotNet.Controls` companion package, what each
  control depends on, and the honest per-backend support matrix.

### Backends
- **[Backends Overview](backends/README.md)** — the platform matrix and the two rendering families.
- **[Apple — iOS / macOS / tvOS](backends/apple.md)** (SwiftUI)
- **[Android](backends/android.md)** (Jetpack Compose)
- **[Linux / GTK](backends/linux-gtk.md)** (GTK4, pure C#)
- **[Windows](backends/windows.md)** (WinUI 3, pure C#)
- **[Web](backends/web.md)** (Blazor WebAssembly → HTML/DOM)
- **[Skia](backends/skia.md)** (self-drawing SkiaSharp toolkit)

### Companions & planning
- **[Maps](maps.md)** — the opt-in `SwiftDotNet.Maps` companion (MapKit / MapLibre).
- **[Roadmap](roadmap.md)** — open design questions and planned work, indexed against [`plans/`](../plans).

## Project status at a glance

| Platform | Renders as | Route | Status |
|----------|-----------|-------|--------|
| iOS | SwiftUI | Swift shim (xcframework, P/Invoke) | ✅ Verified on simulator |
| macOS | SwiftUI (AppKit-hosted) | Same Swift shim | ✅ Verified on desktop |
| tvOS | SwiftUI | Same Swift shim (`#if os(tvOS)` fallbacks) | ✅ Verified on Apple TV sim |
| Android | Jetpack Compose | Kotlin shim (`.aar`, JNI) | ✅ Verified on emulator |
| Linux | GTK4 | Pure C# (Gir.Core, no shim) | ✅ Verified on desktop |
| Windows | WinUI 3 | Pure C# (no shim) | 🧩 Scaffolded — **never compiled**, no tests |
| Web | HTML/DOM | Pure C# (Blazor WASM, no shim) | ✅ Verified in Chrome |
| **Any (Skia)** | **Self-drawn canvas** | **Pure C# (SkiaSharp)** | ✅ Verified (macOS window + headless PNG) |

> The top-level [`README.md`](../README.md) is the marketing/overview entry point; these docs are the
> reference. When they disagree, the docs are authoritative for detail and the README for the pitch.
