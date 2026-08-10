# Game surface & real-time rendering

**Status — 2026-08-08: draft, nothing built.** There is no frame clock, no immediate-mode drawing surface,
and no raw input anywhere in the framework. This doc is the design. There is no `docs/` page because there
is no feature yet.

The prompt was "Flutter is a credible game engine — what are we missing?" The answer is *not* more controls.
This plan explains why, and what the two-layer answer looks like.

## The framing: Flutter is not a game engine, Flame is

Flutter's widget tree contributes exactly three things to a game: a self-drawn surface, a vsync frame
callback, and raw input. Flame — the actual engine — **bypasses** the widget tree. It occupies a single
`CustomPaint` leaf, runs its own `Ticker`-driven loop, keeps its own component tree, and draws straight to a
`Canvas`. Widgets never see a sprite.

That is the shape SwiftDotNet needs. Trying to make [`TreeDiffer`](../src/SwiftDotNet/Core/TreeDiffer.cs)
carry 500 sprites at 60 fps is the wrong fight (see [Decision 1](#decision-1--the-surface-is-a-diffed-leaf-that-then-bypasses-ibridge)).

## Where we actually stand

The bottom third of a game engine is already built, which is why this is worth doing at all.

| Piece | Today |
|---|---|
| Rasterizer seam | ✅ [`ICanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs) — save/restore, transform stack, rect clip, rounded rects/ovals/circles/lines/images/text |
| Self-drawing backends | ✅ Skia (SkiaSharp) and WebGPU (from-scratch SDF rasterizer on wgpu-native, verified on Metal) |
| Geometry | ✅ [`Geometry.cs`](../src/SwiftDotNet.Graphics/Geometry.cs) — `Point`/`Size`/`Rect` with `Contains`, `IntersectsWith`, `Intersect`, `Offset` |
| Pointer plumbing | ✅ [`PointerRouter`](../src/SwiftDotNet.Graphics/PointerRouter.cs) — down/move/up with timestamps, tap slop, long-press, swipe distance + velocity |
| Positioning | ✅ AbsoluteLayout / `.LayoutBounds` |
| Transforms | ⚠️ `.Offset` / `.Rotation` / `.ScaleEffect` / `.Opacity` — separate modifiers, 9-point `Alignment` anchor only |
| Animation | ⚠️ Declarative and **native-owned**: `.Animation(spec, on:)` and [`.Keyframes`](../src/SwiftDotNet/Core/Keyframes.cs) hand interpolation to SwiftUI/CSS/Compose and never report back |
| Frame clock | ❌ **Nothing.** No ticker, no delta time, no elapsed time, no pause/resume |
| Immediate-mode drawing | ❌ `ICanvas` is internal to the paint pass; no DSL-level `Canvas` view |
| Arbitrary paths | ❌ Deliberately excluded — see the remarks on `ICanvas` |
| Keyboard / multi-touch / hover / gamepad | ❌ Nothing |
| Audio | ❌ Nothing |

### The finding that shapes everything

[`IBridge.Render`](../src/SwiftDotNet/Core/IBridge.cs) takes a **`string json`**. Not a `Node`. Every
backend — including the pure-C# in-process ones (GTK, Web, WinUI, Skia, TUI) — serializes through
[`NodeJson.cs`](../src/SwiftDotNet/Core/NodeJson.cs) and re-parses on the other side. So:

- The JSON wire is **not** a native-shim problem. It is universal.
- No per-frame game content can travel through `IBridge` on **any** backend, self-drawing or not.
- Conversely, the P/Invoke boundary on the Apple/Android shim route is *not* the bottleneck. A C-ABI call
  is nanoseconds; JSON serialization is the cost. A binary command buffer over a pinned pointer crosses it
  perfectly well at 60 fps.

That last point is what makes this plan portable rather than Skia-only, and it is why the
[F8 recommendation in `controls-missing-features-plan.md`](controls-missing-features-plan.md#f8--dsl-drawing-surface-retained-vector-canvas)
("a draw closure can't cross, so defer") deserves revisiting. The closure doesn't cross — **its output**
does, as a flat binary buffer, exactly the way Flutter's Dart layer records a `DisplayList` for its C++
engine to replay.

## Design principles

1. **Two layers, hard boundary.** Core gains a *surface* (canvas + clock + raw input) and nothing
   game-specific. Everything recognizably game-shaped — sprites, camera, collision, particles — lives in a
   separate `SwiftDotNet.Game` project that consumes the surface like any other library.
2. **The surface earns its place without games.** A public canvas plus a frame callback also unblocks
   charts, signature pads, custom gauges, waveforms, and the SignaturePad/ImageEditor controls F8 was for.
   If the game layer is never built, Phase 1–3 still pay for themselves.
3. **Record in C#, replay per backend.** One recording format, one replay implementation per backend. No
   backend ever sees game concepts.
4. **Allocation-free on the hot path.** The command buffer is a reused struct array. Core is already
   trim/AOT-safe and reflection-free; the frame path must additionally be GC-quiet.
5. **Honest opt-out.** The TUI has no pixel grid and no animation clock; it declares the capability absent
   rather than faking it. Same rule as everywhere else in this repo — documented no-op, never a silent lie.

---

## Decision 1 — the surface is a diffed leaf that then bypasses `IBridge`

`Canvas` is a normal node type. `TreeDiffer` creates it once, sizes it, positions it in the layout like any
other view, and thereafter **never sees its contents**. Its children do not exist; it has none.

Once created, each backend's renderer for that node registers the live surface handle back into a per-node
side channel. Frames flow through that handle directly, never through `SwiftApp.Render` or
`IBridge.Render`.

**Rejected — sprites as real nodes.** 500 sprites = 500 nodes diffed and JSON-serialized 60 times a second.
Non-starter, and it would push game concerns into `TreeDiffer` forever.

**Rejected — retained draw-command *prop* (the original F8 shape).** Re-serializing the command list into a
node prop each frame puts it back on the JSON wire, which is the exact thing to avoid. The command list is
right; shipping it as a *prop* is not. Keep the recording, drop the node round-trip.

## Decision 2 — the recording format is a binary command buffer

The draw callback writes into a `DisplayList`: a growable `struct` array of fixed-size ops plus side arrays
for strings and image handles. Op codes mirror `ICanvas` one-for-one, plus the Phase-3 additions.

```
struct DrawOp { OpCode Code; float A, B, C, D, E, F; uint Paint; int Aux; }
```

- **In-process backends** (Skia, WebGPU, GTK, Web, WinUI) replay it by walking the array and calling their
  own `ICanvas` — for Skia and WebGPU that is *literally the existing implementation*, so replay is a loop
  and a switch.
- **Shim backends** (SwiftUI, Compose) get a pinned pointer + count across the existing C ABI, and replay
  natively. No JSON, no per-op marshalling.
- **Web** writes the buffer once into shared WASM memory and makes **one** JS interop call per frame, which
  walks it against a `<canvas>` 2D context. Per-op interop would be fatal; per-frame is fine.

**Why not hand each backend a live `ICanvas` and let user code call it directly?** It works for in-process
backends and is impossible for the shim ones. Recording is the only formulation that spans both routes, and
it costs the in-process backends almost nothing.

## Decision 3 — `.OnFrame(dt)` is a modifier on the surface, driven by real vsync

```csharp
new Canvas(Draw)
    .OnFrame(Update)
    .Frame(width: 800, height: 600)

void Update(FrameInfo f)  // f.Delta, f.Elapsed, f.FrameNumber
{
    _x += 120 * f.Delta.TotalSeconds;
}

void Draw(DrawingContext ctx)
{
    ctx.Clear(Colors.Black);
    ctx.DrawImage(_sprite, Rect.Create(_x, _y, 32, 32));
}
```

The clock is per-backend and real, not a timer:

| Backend | Vsync source |
|---|---|
| Skia (iOS/Android via MAUI) | `CADisplayLink` / `Choreographer` |
| Skia (Silk desktop) | the existing [`SampleApp.Skia.Silk`](../sample/SampleApp.Skia.Silk) render loop |
| WebGPU | the surface present loop (needs the windowed host — already on the roadmap) |
| Web | `requestAnimationFrame` |
| SwiftUI | `CADisplayLink` in the shim |
| Compose | `withFrameNanos` |
| GTK | `GtkWidget.AddTickCallback` |
| WinUI | `CompositionTarget.Rendering` |
| TUI | ❌ no clock — declared unsupported |

`FrameInfo.Delta` is clamped (default 100 ms) so a debugger pause doesn't teleport the world. Fixed-timestep
accumulation is a `SwiftDotNet.Game` concern, not Core's.

**`State<T>` is not involved.** Mutating `State` inside `OnFrame` would call `RequestRender` and diff the
whole tree every frame. The surface reads plain fields. This must be loud in the docs — it is the single
most likely misuse.

## Decision 4 — raw input is surface-scoped, not new global gestures

Existing gestures (`.OnDrag`, `.OnMagnify`, `.OnTapGesture`) stay exactly as they are. The surface gets its
own input channel because games need things the gesture vocabulary deliberately doesn't have:

- **Multi-touch** — simultaneous pointers with stable IDs (`PointerRouter` tracks one today). Dual virtual
  joysticks are impossible without this.
- **Keyboard** — down/up/repeat, modifier state, and a queryable *held* set. There is no key input anywhere
  in the framework today; WASD cannot be expressed.
- **Pointer move / hover / wheel** — free movement without a button held.
- **Deferred:** gamepad, pointer capture, cursor lock/hide.

These ride the surface handle out of band, like frames. They do **not** go through the event-channel string
grammar — a per-frame pointer stream through `RegisterAction` would be the JSON problem again.

## Decision 5 — paths and blend modes land behind a capability check

`ICanvas`'s own remarks argue against arbitrary paths in the interface: it would make every future backend
owe a vector rasterizer. That reasoning holds. So:

- `ICanvas` stays as-is. A separate optional `IPathCanvas` / `IBlendCanvas` is queried at surface creation
  and reported through `SurfaceCapabilities`.
- Skia gets both free. WebGPU gets blend modes cheaply (a pipeline state change) and would need something
  Vello-shaped for paths — deferred, and honestly documented as such.
- Web/GTK/WinUI/SwiftUI/Compose all have real path APIs underneath.

Without **blend modes** there are no additive glows, explosions, or credible particles. Without **paths**
there is no non-rect clipping, no terrain, no polygon sprite. Both are Phase 3, not "someday".

---

## Phases

### Phase 1 — the surface  ·  effort L  ·  Skia + WebGPU only

`Canvas` node, `DisplayList` recorder, `DrawingContext` over the existing `ICanvas` op set, replay in
`SwiftDotNet.Graphics`. Verified in the Silk desktop host, which already owns a render loop.

Ships something useful on its own: custom drawing on the two self-drawing backends.

### Phase 2 — the clock and raw input  ·  effort M

`.OnFrame(dt)`, `FrameInfo`, the per-backend vsync sources from Decision 3, multi-touch + keyboard +
hover/wheel on the surface channel. Still Skia + WebGPU.

**After Phase 2 a real game is possible** — badly, with rects and images only, but possible. That is the
checkpoint to decide whether Phase 4 is worth building.

### Phase 3 — the drawing vocabulary  ·  effort M–L

Arbitrary paths (fill/stroke/clip), blend modes, arbitrary transform matrix with a free pivot, sprite
atlases (`DrawImageRect` — source rect → dest rect, the single biggest throughput lever), batched
`DrawAtlas`, stroked/outlined text, bitmap fonts, render-to-texture. All behind `SurfaceCapabilities`.

### Phase 4 — `SwiftDotNet.Game`  ·  effort XL  ·  separate project

Component tree with local transforms · sprite + sprite-sheet + frame animation · camera/viewport (world
space, follow, zoom, letterbox/fit, parallax) · collision (AABB/circle, spatial hash, raycast) ·
imperative interruptible tweens with sequencing · particle emitters · fixed-timestep loop · frame-stats
overlay. Tilemaps and physics stay out of scope; wrap an existing physics library if it ever matters.

### Phase 5 — remaining backends  ·  effort L

Web (`<canvas>` + one interop call per frame), GTK (`Snapshot`/Cairo), WinUI (Win2D), then the SwiftUI and
Compose shims. Sequenced last because each is independent and none blocks the others.

### Not in this plan

- **Audio.** Genuinely required for a game and genuinely not a UI framework concern. It belongs with
  [F10 platform services](controls-missing-features-plan.md#f10--platform-services-media-picker-filesystem-haptics-geocode--effort-ml--defer)
  as a service abstraction (`AVAudioEngine` / `SoundPool` / Web Audio / OpenAL), not here. Flagging it so
  its absence is a decision rather than an oversight.
- **Shaders.** A `FragmentProgram` equivalent is reachable — the WebGPU backend already authors WGSL in
  [`Shaders.cs`](../src/SwiftDotNet.WebGpu/Shaders.cs) and Skia has `SkRuntimeEffect` — but a portable
  shader language across six backends is its own plan.

## Dependencies and interactions

- **Binary bridge protocol** (roadmap, framework-wide): *not* a blocker. The surface bypasses `IBridge`
  entirely, which is precisely why this is buildable before that lands.
- **View-instance reconciliation** (the cross-cutting milestone): not a blocker either. The surface holds
  plain fields, not per-view state.
- **F8 drawing canvas** in [`controls-missing-features-plan.md`](controls-missing-features-plan.md):
  **superseded by Phase 1–3 of this plan.** SignaturePad and ImageEditor become consumers of the same
  surface. That plan's F8 section should be marked as pointing here.
- **F11 geometry reader**: complementary — a surface needs its resolved size, which is exactly what F11
  provides. Either F11 lands first or the surface reports its own size in `FrameInfo`.
- **Accessibility**: a game surface is one opaque rectangle to VoiceOver/TalkBack, the same problem
  [`accessibility-plan.md`](accessibility-plan.md) names for Skia. The honest answer is that a surface
  declares its own accessibility elements explicitly. Out of scope here; do not let it silently regress the
  a11y story.

## Per-backend cost reality

Following the cost model in `controls-missing-features-plan.md`: this is a **new node type**, so it needs a
renderer registered per backend (the `Map`/`CameraView` model), *plus* a `DisplayList` replayer, *plus* a
vsync source, *plus* an input pump. Budget roughly:

| Backend | Replay | Clock | Input | Notes |
|---|---|---|---|---|
| Skia | trivial — wraps existing `SkiaCanvas` | host-dependent | `PointerRouter` extension | cheapest; do it first |
| WebGPU | trivial — wraps existing `WebGpuCanvas` | needs the windowed host | new | second; batching work lands here |
| Web | moderate — one JS call/frame, buffer walk in JS | `rAF` | DOM events | |
| GTK | moderate — Cairo/Snapshot | `AddTickCallback` | GTK events | |
| WinUI | moderate — Win2D | `CompositionTarget.Rendering` | pointer/key events | never compiled; expect surprises |
| SwiftUI | high — native replay in the shim | `CADisplayLink` | UIKit | Swift-side work, no C# seam |
| Compose | high — native replay in the shim | `withFrameNanos` | Android | Kotlin-side work, no C# seam |
| TUI | ❌ | ❌ | ❌ | declares the capability absent |

## Verification

- **Phase 1:** a `Canvas` drawing 1,000 rects renders identically on Skia and WebGPU; screenshot-diff
  against a golden. Recording allocates zero bytes after warm-up (assert with `GC.GetAllocatedBytesForCurrentThread`).
- **Phase 2:** a box moves at a constant 120 pt/s regardless of frame rate; `Delta` clamps across a
  debugger break; simultaneous two-finger input reports two distinct pointer IDs; a held key reports held
  across frames.
- **Phase 3:** a path fill and a non-rect clip match the golden on Skia; blend modes composite correctly;
  a sprite-atlas draw pulls the right source rect. Capability probes report honestly where unsupported.
- **Throughput gate:** 2,000 sprites at 60 fps on the Silk desktop host, and on an iOS device via the
  Skia MAUI head. If Phase 2 misses this by a wide margin, stop and reconsider before Phase 4.
- **Regression:** the existing `ContentView` tour still renders unchanged on all seven backends — a node
  type that bypasses `IBridge` must not perturb the ones that don't.

## Open questions

1. **Does `SwiftApp`'s singleton state need to go first?** The surface holds a live handle outside the
   diffed tree, which is a second piece of per-process global state. It may reasonably ride along with
   [de-singletoning `SwiftApp`](windows-plan.md) rather than adding a second static registry.
2. **Who owns the surface handle registry** — `SwiftApp`, `RenderContext`, or a new `SurfaceRegistry`? It
   must survive re-renders that leave the `Canvas` node in place, and be torn down when the node is removed.
3. **Is `CustomView` the right seam, or does `Canvas` deserve a first-class node type?** `CustomView` gives
   the per-backend renderer registry for free but only carries string/double/bool props and one event
   callback — it has no vocabulary for a live handle. Probably first-class.
4. **Does `Canvas` participate in `.Opacity` / `.ScaleEffect` from the outer tree?** Cheap on Skia
   (`SaveLayer`), awkward on a shim-hosted native surface. Leaning: yes for transforms, documented
   approximation for group opacity.
5. **Phase 4 at all?** Phases 1–3 are justified by charts, signature pads, and custom controls alone. The
   game layer is a genuinely separate bet and should be decided at the Phase 2 checkpoint, not now.

## Cross-links

- [`docs/architecture.md`](../docs/architecture.md) — the two backend routes and the patch protocol
- [`docs/modifiers-gestures-animation.md`](../docs/modifiers-gestures-animation.md) — the declarative
  animation this plan deliberately does *not* replace
- [`docs/custom-controls.md`](../docs/custom-controls.md) — the renderer-registry seam a `Canvas` renderer
  would use
- [`docs/backends/skia.md`](../docs/backends/skia.md), [`docs/backends/webgpu.md`](../docs/backends/webgpu.md)
  — the two backends Phases 1–3 target
- [`controls-missing-features-plan.md`](controls-missing-features-plan.md) — F8 (superseded), F10 (audio),
  F11 (geometry)
