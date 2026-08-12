# Linux / Wayland (self-drawing)

**What it is.** The Skia self-drawing backend running on a **native Wayland surface** — no GTK, no GLFW, no
X11. The app talks `xdg-shell` to the compositor directly, paints into a `wl_shm` buffer with SkiaSharp, and
draws its own titlebar. It is the "dependency-free desktop Linux" target the
[Skia backend](skia.md) has always pointed at, made real.

This is a **sibling** of the [GTK4 backend](linux-gtk.md), not a replacement. GTK4 gives you real GTK widgets
and — importantly — free AT-SPI accessibility. This one gives you a pixel-identical, GTK-free binary.

| | [GTK4](linux-gtk.md) | Wayland (this page) |
|---|---|---|
| Renders as | Real GTK4 widgets | Self-drawn Skia canvas |
| Runtime deps | GTK4, GObject introspection | `libwayland-client`, `libxkbcommon` |
| Accessibility | ✅ AT-SPI via GTK | ❌ none (see [Gotchas](#gotchas)) |
| Look | Native, follows GTK theme | Identical on every platform |
| Session | Wayland **and** X11 (via GDK) | Wayland only |

## How to use it

```csharp
using SwiftDotNet;
using SwiftDotNet.Sample;

var app = SwiftProgram.CreateSwiftApp();

WaylandSkiaHost.Run(
    app.CreateRoot,
    app.Services,
    new WaylandHostOptions
    {
        Title  = "SwiftDotNet · Wayland",
        AppId  = "net.swiftdotnet.sample",   // must match your .desktop filename
        Width  = 440,
        Height = 820,
    });
```

Run the sample:

```bash
dotnet run --project sample/SampleApp.Wayland
```

## What the host actually does

The adapter itself is thin — under 300 lines — because `SwiftDotNet.Graphics` already owns layout,
hit-testing, gestures, focus and the animation clock. Everything Wayland-specific lives in the shared
`Wayland.Platform` library (see [Where the code lives](#where-the-code-lives)):

| Concern | Handled by |
|---|---|
| `xdg_toplevel`, configure/ack handshake, state | `Wayland.Platform` → `WaylandWindow` |
| Client-side decorations, resize edges, caption buttons | `WindowFrame` + `SolidDecorationPainter` |
| Buffers | `ShmSwapchain` — memfd pool, triple-buffered, release-tracked |
| Pointer / keyboard / touch | `WaylandInput` + `XkbKeyboardState` |
| Fractional scaling | `wp_fractional_scale_v1` + `wp_viewporter` |
| Clipboard, IME | `WaylandClipboard`, `zwp_text_input_v3` |
| Light/dark | `DesktopSettings` (xdg-desktop-portal) |
| Event loop, `SynchronizationContext` | `WaylandApplication` |

`WaylandSkiaHost` supplies only: a canvas over the buffer, the title text, input translation into
`SkiaPointerRouter`, and the animation tick.

## Per-backend behaviour

| Behaviour | This backend |
|---|---|
| Rendering | SkiaSharp CPU raster straight into the compositor's shared memory (no GPU path yet) |
| Pixel format | `WL_SHM_FORMAT_ARGB8888` = `SKColorType.Bgra8888` premultiplied — no conversion pass |
| Window chrome | Client-drawn: shadow, rounded frame, titlebar, three caption buttons |
| Title text | Drawn by this host with Skia (`Cantarell` → `Inter` → default) |
| Scaling | Fractional (1.25, 1.5, …) where the compositor supports it; integer otherwise |
| Text input | xkbcommon incl. dead-key compose; `text-input-v3` IME is wired in the platform layer but **not yet routed into the Skia text controls** |
| Key repeat | Synthesized client-side from `repeat_info` |
| `WebView` / `Map` | ❌ Same limitation as every Skia host — they need a native-view overlay. `wl_subsurface` is bound and is the intended mechanism, but no overlay is implemented |
| Accessibility | ❌ None |

## Gotchas

- **GNOME never grants server-side decorations.** The client *must* draw its own frame or it gets a bare
  rectangle with no way to move, resize or close it. That is why `SolidDecorationPainter` exists and why it is
  on by default rather than opt-in. On KDE and wlroots compositors the backend asks for server-side
  decorations first and uses them if granted.
- **Wayland-only.** A pure X11 session (still common on RHEL-family desktops) cannot run this backend — the
  connection fails with a clear message. XWayland runs X11 clients on Wayland, which is the opposite
  direction. Use the [GTK4 backend](linux-gtk.md) there.
- **Accessibility is the real cost of self-drawing on Linux.** AT-SPI2 is a D-Bus protocol that a self-drawn
  toolkit has to implement from scratch. GTK4 gets it for free. If accessibility matters, ship the GTK4
  backend.
- **The first commit must be empty.** Wayland maps a window only after an attach-less commit, a configure,
  and an ack. `WaylandWindow` sequences this; hand-rolling it is the most common way to get a window that
  never appears.
- **Buffers must not be repainted while the compositor holds them.** The swapchain tracks `wl_buffer.release`
  and skips a frame rather than tearing if every buffer is busy.
- **App id must match the `.desktop` basename**, or the desktop shows a generic icon and will not group the
  window with its launcher.

## Where the code lives

The Wayland layers are **not** in this repo — they are shared with the .NET MAUI Wayland backend, which was
the point of factoring them out:

| Layer | Project | Repo |
|---|---|---|
| Protocol bindings + connection | `Wayland.Client` | `maui-wayland` |
| Windowing, input, buffers, CSD | `Wayland.Platform` | `maui-wayland` |
| SwiftDotNet host | [`src/SwiftDotNet.Wayland`](../../src/SwiftDotNet.Wayland) | this repo |
| Sample | [`sample/SampleApp.Wayland`](../../sample/SampleApp.Wayland) | this repo |

Clone `maui-wayland` beside `SwiftDotNet`, or build with
`-p:WaylandPlatformRoot=<path>/maui-wayland/src`.

## Status

🧩 **Scaffolded — builds clean, never run against a live compositor.**

Honestly stated: this was written and compiled on macOS, where no Wayland session exists. What *is* verified:

- All four projects compile with no warnings.
- 23 tests in `maui-wayland/tests/Wayland.Client.Tests` validate the protocol tables — every signature's
  argument arity against its type array, every cross-interface reference, the native `wl_interface` /
  `wl_message` / `wl_argument` struct layout against the C ABI, and fixed-point conversion. That is the part
  a compiler cannot check and the part that corrupts memory inside libwayland when it is wrong.

What is **not** verified: anything requiring a compositor — that a window actually maps, that decorations
look right, that input lands where it should, that resize is smooth. The first run on GNOME, KDE and a
wlroots compositor is the outstanding work, and those three diverge enough (decorations, fractional scale,
layer-shell availability) that all three need checking.

See [`docs/roadmap.md`](../roadmap.md) for what comes next.
