# Backends

One `View` subclass, many renderers. There are **two families** of backend:

- **Native-fidelity** — map the view tree to the OS's *real* controls, with the platform's own layout, fonts,
  animations, and accessibility.
- **Self-drawing** — paint every pixel with SkiaSharp for a pixel-identical look everywhere.

And **two routes** to get there (see [Architecture → the two backend routes](../architecture.md#the-two-backend-routes)):

- **Native shim** for the compiler-plugin toolkits (SwiftUI, Compose) — a thin Swift/Kotlin layer.
- **Pure C#** for the bindable ones (GTK, WinUI, Web) and the self-drawing one (Skia) — no native code.

## Platform matrix

| Platform | Renders as | Route | Status | Doc |
|----------|-----------|-------|--------|-----|
| iOS | SwiftUI | Swift shim (xcframework, P/Invoke) | ✅ Verified on simulator | [Apple](apple.md) |
| macOS | SwiftUI (AppKit-hosted) | Same Swift shim | ✅ Verified on desktop | [Apple](apple.md) |
| tvOS | SwiftUI | Same Swift shim (`#if os(tvOS)` fallbacks) | ✅ Verified on Apple TV sim | [Apple](apple.md) |
| Android | Jetpack Compose | Kotlin shim (`.aar`, JNI) | ✅ Verified on emulator | [Android](android.md) |
| Linux | GTK4 | Pure C# (Gir.Core, no shim) | ✅ Verified on desktop | [Linux/GTK](linux-gtk.md) |
| Windows | WinUI 3 | Pure C# (no shim) | 🧩 Scaffolded — **never compiled**, no tests | [Windows](windows.md) |
| Web | HTML/DOM | Pure C# (Blazor WASM, no shim) | ✅ Verified in Chrome | [Web](web.md) |
| **Any (Skia)** | **Self-drawn canvas** | **Pure C# (SkiaSharp)** | ✅ Verified (macOS window + PNG) | [Skia](skia.md) |

> **What "Verified" means, and what CI actually covers.** ✅ Verified means the backend was *run* and
> inspected on the stated target — it is not a claim of test coverage. The automated suite
> ([`tests/SwiftDotNet.Tests`](../../tests/SwiftDotNet.Tests), 137 green) exercises **Core and Skia only**.
> There are no GTK, Web, WinUI, SwiftUI or Compose rendering tests, so per-backend behaviour in the tables
> throughout these docs is verified by hand, not by CI. Prefer adding a Core or Skia test for new
> behaviour — those are the ones that run on macOS.

## Choosing a backend

- **Want the real platform look & accessibility?** Use the native-fidelity backend for that OS.
- **Want a uniform look on every platform, or a target the native backends can't reach** (dependency-free
  desktop, embedded/framebuffer Linux)? Use **[Skia](skia.md)**. Trade-off: no native accessibility, and
  `WebView` / `Map` can't be painted onto a canvas (they need a native-view overlay).

## What's shared vs. per-backend

Everything in [`Core`](../../src/SwiftDotNet/Core) — DSL, `State<T>`, `Node`, `TreeDiffer`, patch protocol,
`SwiftApp` — is shared verbatim. A backend implements exactly one interface,
[`IBridge`](../../src/SwiftDotNet/Core/IBridge.cs), plus a host. The same
[`SharedUI.ContentView`](../../sample/SharedUI/ContentView.cs) renders on all of them.
