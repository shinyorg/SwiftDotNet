# Godot backend

Runs a SwiftDotNet UI inside a Godot 4 scene as an ordinary `Control` node. Unlike every other self-drawing
backend, the default route **does not use SkiaSharp**: the paint pass is translated into Godot's own 2D draw
commands, so there is no native library to ship and the UI exports wherever Godot exports.

> **Status: verified.** The shared sample renders and responds to input on Godot 4.7.2 (macOS, Metal,
> Forward+), on both rendering routes. See [Status](#status).

## Two routes, both shipped

| | **Native** — [`SwiftDotNetControl`](../../src/SwiftDotNet.Godot/SwiftDotNetControl.cs) | **Texture** — [`SwiftDotNetTextureControl`](../../src/SwiftDotNet.Godot.Skia/SwiftDotNetTextureControl.cs) |
|---|---|---|
| Draws with | Godot's 2D renderer | SkiaSharp, into an `ImageTexture` |
| Native dependency | **none** | `libSkiaSharp` per export target |
| Repaint cost | the changed draw commands | re-upload the whole surface |
| Fidelity | Godot's fonts and antialiasing | pixel-identical to the Skia head |
| Package | `SwiftDotNet.Godot` | `SwiftDotNet.Godot.Skia` |

Prefer the native one. Take the texture one when pixel parity with the other self-drawing backends matters
(a shared screenshot suite) or when you have an existing `ISkiaRenderer` custom control. They are separate
projects precisely so the native route never drags a native binary in.

## Usage

```csharp
public partial class MainMenu : SwiftDotNetControl
{
    protected override View BuildRoot() => new MenuView();
}
```

Attach that script to a `Control` node. Anchor it full-rect for a screen, or put it in a corner — or in a
`CanvasLayer` over a running scene — for a HUD:

```csharp
public partial class Hud : SwiftDotNetControl
{
    public Hud() => Transparent = true;   // only the UI's own pixels; the scene shows through
    protected override View BuildRoot() => new HudView();
}
```

`Services` takes the provider from a `SwiftDotNetApp` builder, so `[Inject]` works as on every other head
— see [Hosting & DI](../hosting-and-di.md).

## How the native route works

Godot's canvas API covers the whole closed vocabulary of
[`ICanvas`](../../src/SwiftDotNet.Graphics/ICanvas.cs) — which is exactly why that vocabulary was kept
closed. The mapping:

| Seam call | Godot |
|---|---|
| `DrawRect`, `DrawRoundRect` | `StyleBoxFlat` (the only canvas primitive with corner radii, borders **and** a soft shadow) |
| `DrawOval`, `DrawCircle` | `canvas_item_add_ellipse` / `add_circle`; stroked variants become a closed polyline |
| `DrawLine` | `canvas_item_add_line` |
| `DrawImage` | `canvas_item_add_texture_rect` |
| `DrawText` | `Font.draw_string` — Godot's own HarfBuzz shaping and fallback chain |
| Gradient fill | a `GradientTexture2D` drawn inside a `CLIP_ONLY` canvas group, so the shape masks it |

### The structural mismatch, and how it is resolved

`ICanvas` is immediate-mode with a save/restore stack. Godot's canvas is **retained**: clipping and group
opacity are properties of a canvas *item*, not of a draw call. So each frame builds a small tree of canvas
items under the host control:

- **`ClipRect`** pushes a child item with `canvas_item_set_clip` and a custom rect. Godot intersects a
  clip with its ancestors', so nested clips (a list inside a scroll view) compose correctly.
- **`SaveLayer`** pushes a child item in `CanvasGroupMode.Transparent` with a modulate alpha. That
  composites the subtree off-screen and then fades it — *not* the same as fading each child, which is the
  whole reason the seam has a layer call.
- A Godot item always draws its own commands **before** its children, so **a group never carries commands
  of its own**: every primitive goes into a leaf child, and draw indices are assigned in issue order.

Items are pooled across frames — a repaint clears and reuses RIDs rather than churning them.

### The bug that shape hid

The first working build drew a pushed navigation page's *content* correctly but left its *background*
behind the list it was supposed to cover. The cause is worth recording, because any retained-mode backend
will hit it: on `RestoreToCount`, restoring the leaf item saved at `Save()` time is only valid while that
leaf is still the newest child of its group. Once a clip or layer has been pushed under the same group, the
saved leaf has a lower draw index — so anything drawn into it paints *underneath*. `GodotCanvas` tracks the
newest child per group and invalidates a stale leaf on restore.

## Per-backend behaviour

| Concern | Native route | Texture route |
|---|---|---|
| Text | Godot's fonts (`ThemeDB` fallback by default; set `Fonts.Regular` / `Fonts.Bold`). Metrics differ slightly from Skia's, so line lengths and wrap points can differ by a pixel or two. | Skia's, identical to every other Skia head |
| Shadows | Real soft shadows on rects and rounded rects (`StyleBoxFlat`). Its falloff is tighter and heavier than Skia's for the same radius, so a card's shadow reads slightly stronger. **Approximate on ellipses and circles** — a solid offset ellipse, because Godot has no canvas blur. | Exact |
| Rounded corners | `StyleBoxFlat` tessellates each corner; `CornerDetail` is scaled with the radius here, because Godot's default of 8 segments is visibly polygonal on a pill or a large card. | Exact |
| Gradients | Linear and radial, masked to the shape | Exact |
| Rotated clips | Degrade to the rotated rect's bounding box — Godot's canvas clip is a scissor, not a stencil | Exact |
| Arbitrary paths | Not in the seam, so not an issue on either route | — |
| Dark mode | `DisplayServer.IsDarkMode()` when the platform reports one, else the `Dark` export | Same |
| Pinch | **Real OS gesture** — `InputEventMagnifyGesture` feeds `.OnMagnify`, rather than the ctrl+wheel substitute the GLFW host uses | Same |
| Touch | `InputEventScreenTouch` / `ScreenDrag`, so a phone export works | Same |
| Soft keyboard | `DisplayServer.VirtualKeyboardShow` on focus, when the platform has one | Same |
| Safe area | **Not wired** — `SafeArea.Update` is internal to Core and needs a host-facing entry point | Same |

## Gotchas

- **The backend projects use `Godot.NET.Sdk`, not `Microsoft.NET.Sdk`.** Godot dispatches engine callbacks
  (`_Ready` / `_Process` / `_Draw` / `_GuiInput`) through code its source generators emit per node class. A
  node type compiled without them is inert — the engine simply never calls it.
- **Your game project needs `<EnableDynamicLoading>true</EnableDynamicLoading>`** if it pulls in NuGet
  packages (the DI abstractions, for instance). Godot loads the game assembly into its own load context, and
  without this the package assemblies are not copied next to it — the failure is a `FileNotFoundException`
  at the first `[Inject]`.
- **Godot 4.x runs .NET 8 assemblies, and will also load a net10.0 one** if that runtime is installed —
  verified here. `SwiftDotNet.Godot` targets `net8.0` so it works either way, which is why Core, Graphics
  and Skia all carry a `net8.0` target alongside `net10.0` and `netstandard2.1`.
- **Do not mix a `netstandard2.1`-compiled backend with a `net10.0` engine assembly.** `init`-only setters
  carry a `modreq` on `IsExternalInit`, which is a *polyfilled internal type* on netstandard2.1 and a BCL
  type on net8.0+. Mixing them throws `MissingMethodException` on the first `with` expression at run time,
  which is how the `net8.0` target came to exist.
- **A transparent HUD needs `Transparent = true`.** The paint pass clears the surface before drawing, and
  by default it clears to the theme's opaque window background.
- **`SwiftApp` holds a single global root**, so one SwiftDotNet control per process.

## Running the sample

```sh
GODOT=/path/to/Godot_mono.app/Contents/MacOS/Godot   # the .NET ("mono") build

# a window, native rendering
"$GODOT" --path sample/SampleApp.Godot

# capture and exit — the non-interactive check
"$GODOT" --path sample/SampleApp.Godot --quit-after 300 -- --shot out.png

# tap a node by id first, so the capture proves the whole loop
"$GODOT" --path sample/SampleApp.Godot --quit-after 300 -- --shot out.png --tap 0.0.0.0

# the Skia-into-a-texture route, same sample
"$GODOT" --path sample/SampleApp.Godot res://MainSkia.tscn --quit-after 300 -- --shot out.png
```

## Status

| Piece | Status |
|---|---|
| Native route renders the shared sample | ✅ Verified on Godot 4.7.2, macOS/Metal/Forward+ |
| Texture (Skia) route renders the shared sample | ✅ Verified, same build |
| Tap → `Emit` → state → repaint | ✅ Verified — `--tap` pushes the detail page and captures it |
| Clipping, gradients, shadows, rounded cards | ✅ Verified by capturing the sample's Shapes and Cards pages and comparing against the Skia head |
| Scroll, drag, long-press, swipe, pinch | 🧩 Wired through `PointerRouter` / Godot's gesture events, not driven by hand |
| Transparent HUD over a scene | 🧩 Implemented and unit-tested at the engine level ([`GameHostTests`](../../tests/SwiftDotNet.Tests/GameHostTests.cs)); not tried over a real scene |
| Text input / on-screen keyboard | 🧩 Wired, never exercised |
| Export to Android / iOS / desktop | 🧩 Never exported. The native route has no native dependency, so it *should* be free; the texture route needs `libSkiaSharp` per target. |
| Safe area | ❌ Not implemented |

## Source

- [`src/SwiftDotNet.Godot/`](../../src/SwiftDotNet.Godot) — the control, the `ICanvas` adapter, fonts and images
- [`src/SwiftDotNet.Godot.Skia/`](../../src/SwiftDotNet.Godot.Skia) — the texture route
- [`sample/SampleApp.Godot/`](../../sample/SampleApp.Godot) — the sample project and screenshot harness

## See also

- [MonoGame backend](monogame.md) — the other verified engine host
- [Unity backend](unity.md)
- [Skia backend](skia.md)
- [Architecture → the renderer seam](../architecture.md#the-renderer-seam)
- [Backends overview](README.md)
