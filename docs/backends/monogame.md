# MonoGame backend

Runs a SwiftDotNet UI inside a MonoGame game — a whole-window UI for a tool or launcher, or a panel over a
running scene. The engine is unchanged: layout, hit-testing, gestures and the paint pass are the same
[`SwiftDotNet.Graphics`](../../src/SwiftDotNet.Graphics) code every other self-drawing backend uses.
MonoGame supplies only what a host owes the engine — a surface, a pointer stream, and a repaint signal.

> **Status: verified.** The shared sample renders and responds to input in a real MonoGame window on macOS
> (DesktopGL, .NET 10). See [Status](#status).

## Why MonoGame at all

Unlike Godot and Unity, **MonoGame ships no UI toolkit** — projects reach for Myra, GeonBit.UI or
ImGui.NET, or draw menus by hand with `SpriteBatch`. That is the gap this fills, and it is why this backend
is the cheapest of the three engine hosts to justify.

It is also the cheapest to *use*. There is no editor, no scripting runtime, no `Assets/Plugins` to populate:
MonoGame is a NuGet package on a normal `.csproj`, so the backend is a `ProjectReference` and nothing else.

## How it works

```
SwiftDotNetComponent (DrawableGameComponent)
   │
   ├── SkiaBridge ─────────► the shared engine
   ├── SkiaPointerRouter ──► taps / long-press / swipe / drag / scroll
   │
   └── byte[] pixels ◄── SKSurface composites straight into the array
           │
           └──► Texture2D.SetData ──► SpriteBatch.Draw
```

The pixel buffer is pinned once and Skia draws into it in place, so a repaint costs one upload and no
intermediate bitmap. Repaint is demand-driven: `Update` only marks the surface dirty when a patch lands or
an animation is running, and `Draw` re-blits the existing texture otherwise.

## Usage

Whole-window UI — [`SwiftDotNetGame`](../../src/SwiftDotNet.MonoGame/SwiftDotNetGame.cs) is the stock
`Game` wiring:

```csharp
using var game = new SwiftDotNetGame(() => new ContentView()) { Title = "My tool" };
game.Run();
```

A HUD or menu over an existing game — add the component to your own `Game`:

```csharp
protected override void Initialize()
{
    Components.Add(new SwiftDotNetComponent(this, new PauseMenu())
    {
        Transparent = true,                             // the scene shows through
        Bounds = new Rectangle(20, 20, 360, 220),       // back-buffer pixels; empty = full window
    });
    base.Initialize();
}
```

`Services` takes the provider from a `SwiftDotNetApp` builder, so `[Inject]` works exactly as it does on
every other head — see [Hosting & DI](../hosting-and-di.md).

## Options

| Property | Default | What it does |
|---|---|---|
| `Bounds` | empty (full back buffer) | Where the UI draws, in back-buffer pixels. Pointer coordinates are mapped into it. |
| `Transparent` | `false` | Clear to transparent instead of the theme background, so the scene behind shows through. |
| `Dark` | `false` | Dark theme. MonoGame exposes no OS appearance API, so this is the host's call. |
| `RenderScale` | `1` | Device pixels per layout unit — `2` renders the UI at double resolution into the same rect. |
| `ScrollSpeed` | `40` | Layout units per wheel notch. |
| `HandleTextInput` | `true` | Route `Window.TextInput` into the focused field. Turn off for a game that owns the keyboard. |

## Per-backend behaviour

Rendering is the Skia backend's, so [its behaviour table](skia.md) applies verbatim. Host-level
differences:

| Concern | Behaviour |
|---|---|
| Input | Polled, in `Update`. Touch (`TouchPanel`) takes priority over mouse, because on a phone head MonoGame still reports a phantom `(0,0)` mouse position. |
| Pinch | **Not wired.** MonoGame's `GestureType.Pinch` would feed `PointerRouter.Pinch`; nothing does yet, so `.OnMagnify` is inert. |
| Text input | `Window.TextInput`, forwarded only while a field has focus. Backspace is handled; caret movement and selection are not. |
| Dark mode | Manual (`Dark`). |
| Safe area | **Not wired.** `SafeArea.Update` is internal to Core; feeding a phone head's insets through needs a host-facing entry point that does not exist yet. |
| HiDPI | MonoGame's back buffer is in pixels and it does no scaling of its own, so a Retina window wants `RenderScale = 2`. |
| Blend | `BlendState.AlphaBlend` against Skia's premultiplied surface, which is correct for both the opaque and transparent cases. |

## Gotchas

- **The library binds against `MonoGame.Framework.DesktopGL` with `PrivateAssets="all"` and
  `ExcludeAssets="runtime"`.** Every MonoGame platform package (DesktopGL, WindowsDX, Android, iOS) publishes
  the same `MonoGame.Framework` assembly, so a library must compile against one and let the *game* choose
  which one ships. Referencing DesktopGL here is a choice of contract copy, not of platform.
- **`Bounds` is in back-buffer pixels, not window points.** On a HiDPI display those differ, and a HUD
  positioned in points lands in the wrong place.
- **A transparent HUD needs `Transparent = true`,** not just an alpha background on the root view — the
  paint pass clears the surface first, and by default it clears to the theme's opaque window background.
  See [`VisualBridge.ClearColor`](../../src/SwiftDotNet.Graphics/VisualBridge.cs).
- **`SwiftApp` holds a single global root**, so one component per process. Two `SwiftDotNetComponent`s in
  one game would fight over it.

## Running the sample

```sh
# a window
dotnet run --project sample/SampleApp.MonoGame

# render N frames, save the back buffer, exit — the non-interactive check
dotnet run --project sample/SampleApp.MonoGame -- --shot out.png

# tap a node by id first, so the capture proves the whole loop, not just the paint pass
dotnet run --project sample/SampleApp.MonoGame -- --shot out.png --tap 0.0.0.0
```

The `--tap` form is worth knowing: it drives hit-test → `Emit` → state → diff → repaint and captures the
result, which is how this backend was verified without a human clicking.

## Status

| Piece | Status |
|---|---|
| Renders the shared sample | ✅ Verified — real MonoGame window and back buffer, macOS/DesktopGL/.NET 10 |
| Tap → `Emit` → state → repaint | ✅ Verified — `--tap 0.0.0.0` pushes the detail page and captures it |
| Scroll, drag, long-press, swipe | 🧩 Wired through `PointerRouter`, not driven by hand |
| Transparent HUD over a scene | 🧩 Implemented and unit-tested at the engine level ([`GameHostTests`](../../tests/SwiftDotNet.Tests/GameHostTests.cs)); not tried in a real game |
| Pinch / `.OnMagnify` | ❌ Not wired |
| Safe area, HiDPI auto-detect | ❌ Not implemented |
| WindowsDX / Android / iOS heads | 🧩 Should work — same assembly contract — never built |

## Source

- [`src/SwiftDotNet.MonoGame/`](../../src/SwiftDotNet.MonoGame) — the component and the `Game` subclass
- [`sample/SampleApp.MonoGame/`](../../sample/SampleApp.MonoGame) — the sample head and screenshot harness

## See also

- [Skia backend](skia.md) — what actually draws the pixels
- [Godot backend](godot.md) — the other engine host, and the one that does *not* use Skia
- [Unity backend](unity.md)
- [Architecture → the renderer seam](../architecture.md#the-renderer-seam)
- [Backends overview](README.md)
