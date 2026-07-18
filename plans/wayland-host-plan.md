# Plan: Direct Wayland host for the Skia engine (`SwiftDotNet.Skia.Wayland`)

**Status:** Draft for review — **not committed to build** · **Date:** 2026-07-18
**Save to (repo convention):** `plans/wayland-host-plan.md`

> **Scope note.** This plan covers **only** the direct `libwayland-client` route — a real, GTK-free,
> SDL-free Wayland client written in (near-)pure C#. The easier SDL3/GLFW-via-Silk.NET windowing route
> is deliberately **out of scope** here. We may or may not build this; the point of the doc is to make
> the feasibility and the sharp edges concrete before committing.

## Context

Wayland is a **display-server protocol, not a widget toolkit**. A Wayland client gets exactly three
things from the compositor: (1) a pixel **buffer** to draw into, (2) **window management**
(`xdg-shell`), and (3) **input events** (`wl_seat` → pointer/keyboard/touch). That is precisely the
contract the **Skia self-drawing engine** (`SwiftDotNet.Skia`, see `plans/skia-backend-plan.md` /
[[swiftdotnet-skia]]) already expects from a host. So a Wayland provider is **not a new rendering
backend** — it is a new **host** for the existing engine, parallel to `sample/SampleApp.Skia.Mac`'s
AppKit host.

The engine is fully host-agnostic. `SkiaBridge` already exposes everything a host needs:

- `Paint(SKCanvas canvas, SKSize size, bool dark)` — render current scene
- `DispatchPointer(SKPoint)` · `Scroll(SKPoint, dy)` — pointer + wheel
- `InsertText(string)` · `DeleteBackward()` — keyboard into focused control
- `LongPress(SKPoint)` · `Swipe(SKPoint, dir)` — gestures
- `Tick(dt)` — advance implicit animations
- `event Invalidate` — engine asks host to repaint after a patch

Nothing in the engine changes. **All the work in this plan is native-interop plumbing to feed those
methods.** This is the most interop-dense piece of the whole project.

### Why direct-libwayland at all (given GTK already runs on Wayland)

- The **GTK4 backend** ([[swiftdotnet-gtk]]) is already a native Wayland client today — so this is **not**
  about filling a native-control gap on Linux.
- The value is a **dependency-light, self-drawing native Linux window** — no GTK, no SDL — that is
  pixel-identical to every other platform and a stepping stone to **framebuffer/embedded (KMS/DRM)**,
  which is the same host shape minus a compositor. This is the "dependency-free desktop /
  embedded/framebuffer Linux" target the Skia plan calls out.

## The host, in one diagram

A single `WaylandHost : ISkiaHost` owns the connection, the buffers, and the loop:

```
connect ── wl_display_connect(NULL)
  └─ wl_display_get_registry ── listener: "global" events
       ├─ bind wl_compositor      → wl_surface
       ├─ bind wl_shm             → advertised formats; create pool + buffers
       ├─ bind xdg_wm_base        → xdg_surface → xdg_toplevel     (window)
       └─ bind wl_seat            → wl_pointer / wl_keyboard / wl_touch

loop (own poll on wl_display_get_fd, or dispatch_pending + flush):
  xdg_wm_base.ping        → xdg_wm_base.pong                       (keepalive)
  xdg_surface.configure   → ack_configure + (re)allocate buffers, mark dirty
  wl_surface.frame.done   → engine.Tick(dt); Paint into shm buffer;
                            attach + damage + commit; request next frame
  wl_pointer  motion/button/axis  → DispatchPointer / Scroll
  wl_keyboard key (via xkbcommon) → InsertText / DeleteBackward
  wl_touch    down/motion/up      → DispatchPointer / gestures
```

Everything right of the arrows is a call into the existing engine. Everything left of them is this plan.

## The contract we implement (`ISkiaHost`, unchanged)

```csharp
public interface ISkiaHost { bool Dark { get; } void Invalidate(); }
```

`WaylandHost` implements `Dark` (from the appearance portal, see §Open questions) and `Invalidate`
(set a dirty flag; the frame-callback loop repaints). It drives the engine methods listed above from
protocol events. Mirror the Mac host's construction: take a `SkiaBridge`, subscribe to its `Invalidate`
event, run the loop.

## Crux #1 — libwayland's marshalling model (the thing that makes managed Wayland possible)

libwayland is **not** a flat C ABI you bind one-DllImport-per-request. Everything funnels through two
calls, and both are bindable from C# **without libffi and without a native shim**:

### Requests → `wl_proxy_marshal_flags(proxy, opcode, interface, version, flags, …varargs)`

The request args are C **varargs**. Call it from C# with **`__arglist`** (the same mechanism used to
call C `printf`) — this is ABI-correct for variadics, including the x86-64 SysV `%al` vector-count
register that the naïve "fixed-signature DllImport per call" trick gets *wrong* (works by luck for
integer/pointer args, breaks unpredictably):

```csharp
[DllImport("libwayland-client.so.0", CallingConvention = CallingConvention.Cdecl)]
static extern IntPtr wl_proxy_marshal_flags(
    IntPtr proxy, uint opcode, IntPtr iface, uint version, uint flags, __arglist);

// wl_compositor.create_surface (opcode 0, creates a wl_surface):
IntPtr surface = wl_proxy_marshal_flags(
    compositor, 0, pWlSurfaceInterface, GetVersion(compositor), 0,
    __arglist(IntPtr.Zero /* new_id placeholder */));
```

One helper covers **every** request; write ~20 thin wrappers for the requests a one-window app uses
(create_surface, get_xdg_surface, get_toplevel, attach, damage, commit, frame, create_pool,
create_buffer, ack_configure, pong, registry bind, seat get_pointer/keyboard, destroy, …).
`WL_MARSHAL_FLAG_DESTROY = 1` for the destructor requests.

### Events → `wl_proxy_add_listener(proxy, &funcs, data)`

`funcs` is a struct of C function pointers, **one per event in protocol order**. Use
`[UnmanagedCallersOnly]` function pointers in a **blittable** struct:

```csharp
[UnmanagedCallersOnly(CallConvs = new[]{ typeof(CallConvCdecl) })]
static void OnRegistryGlobal(IntPtr data, IntPtr reg, uint name, IntPtr iface, uint version) { … }

[StructLayout(LayoutKind.Sequential)]
struct RegistryListener {                       // must match wl_registry_listener exactly
    public delegate* unmanaged[Cdecl]<IntPtr,IntPtr,uint,IntPtr,uint,void> global;
    public delegate* unmanaged[Cdecl]<IntPtr,IntPtr,uint,void>            global_remove;
}
```

**Pin the listener struct** (allocate in unmanaged memory or a pinned `GCHandle`) and keep it alive for
the proxy's lifetime — a moved/collected listener is the #1 crash source. Same pattern per interface
that has events (registry, xdg_surface, xdg_toplevel, wl_pointer, wl_keyboard, wl_touch, wl_buffer,
xdg_wm_base, wl_callback).

## Crux #2 — supplying `wl_interface` metadata that libwayland does NOT export

`wl_proxy_marshal_flags` and `wl_registry_bind` need a pointer to the `wl_interface` struct for each
object type.

- **Core interfaces** (`wl_compositor_interface`, `wl_shm_interface`, `wl_shm_pool_interface`,
  `wl_surface_interface`, `wl_buffer_interface`, `wl_seat_interface`, `wl_pointer_interface`,
  `wl_keyboard_interface`, `wl_touch_interface`, `wl_registry_interface`, `wl_callback_interface`) are
  **exported symbols in `libwayland-client.so`** → `dlopen`/`dlsym` them. Done.

- **`xdg-shell` is an extension protocol.** Its `wl_interface`/`wl_message` tables are generated from
  `xdg-shell.xml` by `wayland-scanner` and are **not** in libwayland. You must supply them. **This is the
  one decision to make before coding:**

  - **(a) Tiny generated C shim (recommended to start).** `wayland-scanner private-code xdg-shell.xml
    xdg-shell-protocol.c`, compile the single file into `libswiftdotnet-wl.so`, `dlsym`
    `xdg_wm_base_interface` etc. from it. Safe and boring; ~30 lines of build glue. **Cost:** one native
    build artifact — contradicts the "zero native shim" purity goal, but ships reliably.
  - **(b) Reconstruct `wl_interface` in C# (purity pass, later).** The structs are plain data —
    `wl_interface { char* name; int version; int method_count; wl_message* methods; int event_count;
    wl_message* events }` and `wl_message { char* name; char* signature; wl_interface** types }`. Build
    the xdg-shell graph in unmanaged memory at startup. Fully doable but fiddly — a wrong signature
    string corrupts marshalling **silently**. Codegen it from the XML; do **not** hand-write.

  **Recommendation:** start with (a) to get a window on screen; treat (b) as an optional
  "zero-native-artifacts" follow-up. The same choice recurs for **every** extension you add
  (`xdg-decoration`, pointer-constraints, …).

## The rest — mechanical, but note the extra native deps

### SHM buffer (this is libc interop, not a Wayland problem)

`memfd_create` (or `shm_open`) → `ftruncate(w*h*4)` → `mmap` → hand the pointer to Skia as an
`SKSurface` over `WL_SHM_FORMAT_ARGB8888` pixels (BGRA byte order little-endian). Pass the **fd** to
`wl_shm_create_pool` (libwayland sends it as an ancillary fd inside the marshal for the `h` arg type).
Create `wl_buffer` from the pool. **Double-buffer**: two buffers, ping-pong driven by the
`wl_buffer.release` event so you never draw into a buffer the compositor is still reading.

Render path per frame: `Paint` straight into the mmap'd buffer → `wl_surface.attach` → `damage`/
`damage_buffer` → `wl_surface.commit`. (Note: this is *faster* than the Mac host, which encodes PNG →
NSImage; here Skia rasterizes directly into the shared buffer.)

### Keyboard → **libxkbcommon** (second native dep, unavoidable)

The compositor sends the keymap over an fd (`wl_keyboard.keymap` event, xkb text format). `mmap` it and
feed it to `xkb_keymap_new_from_string` / `xkb_state_update_key` / `xkb_state_key_get_utf8` to turn
raw keycodes → UTF-8 for `InsertText`, and to detect Backspace/Enter/arrows for
`DeleteBackward`/editing. Handle `modifiers`, and `repeat_info` for key-repeat. There is **no** correct
international-keyboard path without libxkbcommon.

### Window decorations (a real gap on GNOME)

Wayland guarantees **no** server-side titlebar. Mutter/GNOME won't offer SSD. Options:
- Bind `zxdg_decoration_manager_v1` and request SSD (another extension → Crux #2 again), honored on
  KDE/wlroots, **ignored on GNOME**; or
- Draw **client-side decorations** yourself. The engine can paint a titlebar/border, but hit-testing the
  resize edges + move-drag (`xdg_toplevel.move`/`.resize`) is host work.

Milestone 1 can ship **borderless / CSD-off** (fine for a spike and for embedded, which has no
compositor at all).

## Deliverables / project layout (mirrors the Skia + GTK backends)

- `src/SwiftDotNet.Skia.Wayland` (net10.0, `RootNamespace SwiftDotNet`, refs `SwiftDotNet.Skia`):
  - `Interop/WaylandClient.cs` — `dlopen` + `wl_proxy_marshal_flags` (`__arglist`) helper + request wrappers.
  - `Interop/WaylandListeners.cs` — `[UnmanagedCallersOnly]` callbacks + pinned listener structs.
  - `Interop/Interfaces.cs` — `dlsym` of core `wl_*_interface`; xdg-shell per Crux #2 (a) or (b).
  - `Interop/Xkb.cs` — libxkbcommon binding.
  - `Interop/Shm.cs` — memfd/mmap + double-buffer pool.
  - `WaylandHost.cs` — `ISkiaHost`; registry bind, xdg handshake, frame loop, input dispatch.
  - `Build/` — (route a) `wayland-scanner` invocation + `libswiftdotnet-wl.so` build target.
- `sample/SampleApp.Skia.Wayland` (net10.0 Exe) — `WaylandHost(new SkiaBridge(...)).Run(new ContentView())`;
  reuses the shared `ContentView`.
- **slnx**: add both projects. **README** backend table + Projects list: add the Wayland host row.

## Milestones

1. **Handshake + a filled window.** connect → registry → bind compositor/shm/xdg_wm_base → xdg_surface
   configure/ack → single shm buffer → solid-color commit. Proves marshalling, listeners, xdg-shell
   metadata (Crux #1 + #2) end-to-end. *Verify on real Linux (Wayland session).*
2. **Paint the engine.** Swap the solid fill for `bridge.Paint`; double-buffer + frame-callback tick
   loop. Renders the whole `ContentView` (Skia already does the toolkit).
3. **Pointer + wheel.** `wl_pointer` motion/button/axis → `DispatchPointer`/`Scroll`. Tap-interactive.
4. **Keyboard.** libxkbcommon → `InsertText`/`DeleteBackward`; focus a TextField and type.
5. **Polish.** resize (reallocate buffers on configure), dark-mode via appearance portal, touch,
   long-press/swipe, key-repeat. Decorations decision (borderless vs CSD vs xdg-decoration).

## Honest feasibility read

- **Can we do it?** Yes, in near-pure C#. The app-side surface is small (~6 protocol interfaces for one
  window). No libffi, no mandatory native shim *if* we take Crux #2 route (b).
- **Genuinely tricky C#:** the `__arglist` marshal helper and **keeping listener structs pinned**.
  Everything else is rote once those patterns exist.
- **Native deps:** `libwayland-client.so.0`, `libxkbcommon.so.0`, libc (memfd/mmap), plus either a
  generated `xdg-shell` shim `.so` (route a) or hand-built interface tables (route b). All present on any
  Wayland desktop.
- **Effort:** days, not an afternoon (vs. the SDL host's afternoon) — but **bounded**. The risk is
  concentrated in Milestone 1; after the handshake works, the rest is wiring known engine methods.
- **Verification caveat:** like GTK on macOS, real validation must happen on a **Linux Wayland session**
  — the engine can be exercised headlessly on macOS, but the protocol path cannot.

## Open questions (decide before coding)

1. **Crux #2:** generated C shim (a) or in-C# interface tables (b) for xdg-shell? *Recommend (a) first.*
2. **GPU or software?** This plan assumes **wl_shm software** buffers (simplest, matches the raster
   engine). EGL/Vulkan (Ganesh) is a later perf option and adds `libwayland-egl` + `libEGL`.
3. **Decorations:** borderless M1 → CSD or `xdg-decoration` later? *Recommend borderless for the spike.*
4. **Dark mode source:** `org.freedesktop.appearance color-scheme` via the settings portal (needs a
   D-Bus call) vs. a fixed/env default for M1. *Recommend env/default first.*
5. **Event-loop integration:** own `poll()` on `wl_display_get_fd` vs. `wl_display_dispatch_pending` +
   `flush` on a timer. *Recommend own poll for clean frame pacing.*
