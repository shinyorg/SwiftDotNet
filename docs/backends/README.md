# Backends

One `View` subclass, many renderers. There are **two families** of backend:

- **Native-fidelity** — map the view tree to the OS's *real* controls, with the platform's own layout, fonts,
  animations, and accessibility.
- **Self-drawing** — paint every pixel ourselves for a pixel-identical look everywhere. The engine that does
  this is rasterizer-agnostic (see [the renderer seam](../architecture.md#the-renderer-seam)), so the same
  layout, hit-testing, gestures and paint pass drive SkiaSharp, a from-scratch WebGPU renderer, a game
  engine's texture, or — on Godot — the engine's own 2D draw commands.

And **two routes** to get there (see [Architecture → the two backend routes](../architecture.md#the-two-backend-routes)):

- **Native shim** for the compiler-plugin toolkits (SwiftUI, Compose) — a thin Swift/Kotlin layer.
- **Pure C#** for the bindable ones (GTK, WinUI, WPF, Web) and the self-drawing one (Skia) — no native code.

## Platform matrix

| Platform | Renders as | Route | Status | Doc |
|----------|-----------|-------|--------|-----|
| iOS | SwiftUI | Swift shim (xcframework, P/Invoke) | ✅ Verified on simulator | [Apple](apple.md) |
| macOS | SwiftUI (AppKit-hosted) | Same Swift shim | ✅ Verified on desktop | [Apple](apple.md) |
| tvOS | SwiftUI | Same Swift shim (`#if os(tvOS)` fallbacks) | ✅ Verified on Apple TV sim | [Apple](apple.md) |
| Android | Jetpack Compose | Kotlin shim (`.aar`, JNI) | ✅ Verified on emulator | [Android](android.md) |
| Linux | GTK4 | Pure C# (Gir.Core, no shim) | ✅ Verified on desktop | [Linux/GTK](linux-gtk.md) |
| Linux | Self-drawn on a native Wayland surface | Pure C# (libwayland/xkbcommon P/Invoke, no shim) | 🧩 Scaffolded — builds clean; **never run against a compositor** (protocol tables unit-tested) | [Linux/Wayland](wayland.md) |
| Windows | WinUI 3 | Pure C# (no shim) | 🧩 Scaffolded — **never compiled**, no tests | [Windows](windows.md) |
| Windows | WPF | Pure C# (no shim) | 🧩 Scaffolded — **compiles clean** (macOS cross-target + `windows-latest` CI), **never run** | [WPF](wpf.md) |
| Windows | Self-drawn on a WinForms / WPF surface | Pure C# (Skia engine, no shim) | 🧩 Host **compiles clean** (same CI job), **never run**; the engine it hosts is CI-verified | [WinForms](winforms.md) |
| Web | HTML/DOM | Pure C# (Blazor WASM, no shim) | ✅ Verified in Chrome | [Web](web.md) |
| **Any (Skia)** | **Self-drawn canvas** | **Pure C# (SkiaSharp)** | ✅ Verified (macOS window + PNG) | [Skia](skia.md) |
| **Any (WebGPU)** | **Self-drawn, on the GPU** | **Pure C# (wgpu-native, no Skia)** | ✅ Verified on Metal via headless pixel readback (8 CI tests); Vulkan/D3D12 unexercised | [WebGPU](webgpu.md) |
| **MonoGame** | **Self-drawn into a `Texture2D`** | **Pure C# (Skia engine + game component)** | ✅ Verified — real window and back buffer on macOS/DesktopGL | [MonoGame](monogame.md) |
| **Godot** | **Godot's own 2D draw commands** (no Skia) | **Pure C# (`Control` node)** | ✅ Verified on Godot 4.7.2, macOS/Metal — both the native and the Skia-texture route | [Godot](godot.md) |
| **Unity** | **Self-drawn into a `Texture2D`** | **Pure C# (Skia engine + Unity host)** | 🧩 Host **compiles** against UnityEngine reference assemblies; **never run** | [Unity](unity.md) |
| **Any (terminal)** | **Characters in a TTY** | **Pure C# (XenoAtom.Terminal.UI)** | ✅ Verified headlessly on macOS (35 CI tests); not yet driven by hand in a live terminal | [Terminal/TUI](tui.md) |

> **What "Verified" means, and what CI actually covers.** ✅ Verified means the backend was *run* and
> inspected on the stated target — it is not a claim of test coverage. The automated suite
> ([`tests/SwiftDotNet.Tests`](../../tests/SwiftDotNet.Tests), 364 green) exercises **Core, Skia and the
> terminal backend only**. There are no GTK, Web, WinUI, WPF, WinForms, SwiftUI, Compose, MonoGame or Godot rendering
> tests, so per-backend behaviour in the tables throughout these docs is verified by hand, not by CI. The
> two game-engine backends that are marked ✅ were verified by driving their sample heads
> non-interactively — `--shot` / `--tap`, described on each page — and reading the captured frame. Prefer adding a Core, Skia
> or TUI test for new behaviour — those are the ones that run on macOS.

## Choosing a backend

- **Want the real platform look & accessibility?** Use the native-fidelity backend for that OS.
- **Want a uniform look on every platform, or a target the native backends can't reach** (dependency-free
  desktop, embedded/framebuffer Linux)? Use **[Skia](skia.md)**. Trade-off: no native accessibility, and
  `WebView` / `Map` can't be painted onto a canvas — they need a real OS control floated over it, which
  today only the MAUI host does (see the [platform-view seam](../maui-interop.md#the-platform-view-seam)).
- **Already have a .NET MAUI app?** You don't need a backend — host a SwiftDotNet tree in a MAUI page with
  `SwiftDotNetSkiaView`, and put real MAUI controls back inside it with
  [`MauiView`](../maui-interop.md#mauiview). There is deliberately **no MAUI backend** (nothing here
  translates nodes into `Label`/`Button`/`Entry`): it would reach no platform the Apple, Compose and WinUI
  backends don't already reach with higher fidelity. See [MAUI Interop](../maui-interop.md).
- **Want the self-drawn look with no native imaging dependency, or GPU-resident rendering?** Use
  **[WebGPU](webgpu.md)**. Trade-off: PNG-only images, no complex text shaping, and group opacity is
  approximated.
- **Rendering inside a game engine?** Three hosts, and the choice is mostly which engine you are already in:
  **[MonoGame](monogame.md)** (verified; MonoGame ships no UI of its own, so this fills a real gap),
  **[Godot](godot.md)** (verified; the default route draws with Godot's renderer, so there is **no native
  dependency** and it exports wherever Godot does), and **[Unity](unity.md)** (compiles, never run). All
  three support a transparent HUD over a live scene.
- **On Windows specifically?** Three answers, and they are genuinely different products.
  **[WinUI 3](windows.md)** is the modern Fluent one (but has never compiled). **[WPF](wpf.md)** is real
  Win32 controls with UI Automation accessibility, and is the one that actually builds today.
  **[WinForms](winforms.md)** gets the Skia canvas and *only* the Skia canvas — a native WinForms backend
  would have had to no-op half the modifier vocabulary, so it was deliberately not built. The same Skia
  canvas is available on WPF too (`SwiftDotNet.Skia.Wpf`) when you want the uniform look instead of the
  native one.
- **No display server at all — SSH, a container, CI?** Use **[Terminal/TUI](tui.md)**. Trade-off: one glyph
  size (so `.Font` becomes emphasis, not scale), no transforms or animation, and images become character
  art unless the terminal speaks Sixel/Kitty.

## What's shared vs. per-backend

Everything in [`Core`](../../src/SwiftDotNet/Core) — DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol,
`SwiftApp` — is shared verbatim. A few pure-math helpers are shared *into* the backends too, so they can't
drift apart: [`GridEngine`](../../src/SwiftDotNet/Core/GridEngine.cs) (track parsing + grid cell placement)
and [`AbsoluteLayoutBounds`](../../src/SwiftDotNet/Core/GridLayout.cs) (proportional-bounds resolution) are
called by every C# backend and ported line-for-line into the Swift and Kotlin shims. A backend implements exactly one interface,
[`IBridge`](../../src/SwiftDotNet/Core/IBridge.cs), plus a host. The same
[`SharedUI.ContentView`](../../sample/SharedUI/ContentView.cs) renders on all of them.
