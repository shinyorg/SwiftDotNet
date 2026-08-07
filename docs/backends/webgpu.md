# WebGPU backend

A **from-scratch GPU rasterizer** for the self-drawing engine. Where the [Skia](skia.md) backend hands the
paint pass to SkiaSharp, this one implements it directly: every shape is a signed-distance field evaluated
in a fragment shader, text comes from a glyph atlas, and the whole UI is drawn in one instanced draw call.

There is **no Skia anywhere in this backend's dependency graph**. Glyph rasterization uses the pure-managed
[stb_truetype](https://www.nuget.org/packages/StbTrueTypeSharp) port and image decoding uses the shared
[`PngDecoder`](../../src/SwiftDotNet.Graphics/Png.cs).

> **The "W" is a spec name, not a deployment target.** This is a desktop/mobile backend. WebGPU *from
> Blazor WASM* would mean per-frame JS interop for command encoding, which loses to the existing
> [Web backend](web.md)'s DOM output. Use the Web backend in a browser.

## How it works

```
VisualBridge (shared engine: layout, hit-test, gestures, paint pass)
        │  ICanvas calls
        ▼
WebGpuCanvas      records each primitive as one GPU "instance" (no rasterization)
        │  storage buffer
        ▼
WebGpuRenderer    one instanced draw per image batch
        │
        ▼
wgpu-native  →  Metal (Apple) · Vulkan (Linux/Android) · D3D12 (Windows)
```

The engine's [`ICanvas`](../../src/SwiftDotNet.Graphics/ICanvas.cs) exposes a deliberately small, closed set
of primitives — rounded rects, ovals, circles, lines, images and text under a transform stack with
rectangular clipping. That closure is what makes a from-scratch GPU backend tractable: rounded rectangles,
capsules, circles, borders and shadows are all the *same two distance functions*, and antialiasing falls out
of the distance's screen-space derivative instead of needing multisampling.

A line is drawn as a capsule in a rotated local frame rather than getting its own shader path — the
rounded-rect field already *is* a capsule when its radius is half its height.

## Usage

```csharp
using var bridge = new WebGpuBridge();
using var renderer = new WebGpuRenderer();

SwiftApp.Run(new ContentView(), bridge);

// Each frame: record on the CPU, then submit.
var canvas = bridge.Record(new Size(width, height), dark: false);
renderer.Render(canvas, swapChainTextureView);
```

Headless, for tests and screenshots:

```csharp
using var bridge = new WebGpuBridge();
using var host = new WebGpuImageHost(bridge);

SwiftApp.Run(new ContentView(), bridge);

byte[] rgba = host.RenderRgba(400, 800);   // straight RGBA8
host.Tap(200, 120);                        // drive interaction, then re-render
```

## Fonts

There is no cross-platform font enumeration API that does not drag in the native dependency this backend
exists to avoid, so faces are found by well-known path — Arial/Helvetica on macOS, DejaVu/Liberation on
Linux, Segoe UI/Arial on Windows. Supply your own (and ship it with the app, which is the reliable option):

```csharp
WebGpuFonts.RegularPath = Path.Combine(AppContext.BaseDirectory, "Inter-Regular.ttf");
WebGpuFonts.BoldPath    = Path.Combine(AppContext.BaseDirectory, "Inter-Bold.ttf");
```

Glyphs are rasterized once per (face, size, codepoint) into a 1024×1024 single-channel atlas and cached for
the process lifetime.

## Per-backend behaviour

| Feature | WebGPU | Skia | Notes |
|---|---|---|---|
| Rounded rects, capsules, circles | ✅ SDF | ✅ | Exact, and cheaper than a rasterized path |
| Borders / strokes | ✅ SDF ring | ✅ | |
| Drop shadows | ✅ SDF falloff | ✅ image filter | Same spec, rendered in the same draw call — **not** a fallback |
| Linear & radial gradients | ✅ | ✅ | N stops, interpolated in the shape's local space so they rotate with it |
| Transforms (offset / scale / rotation) | ✅ | ✅ | Applied to the distance field, so rotation stays exact |
| Clipping | ✅ rect only | ✅ | Per-instance, so the frame stays one draw call |
| Text + font fallback | ✅ atlas | ✅ HarfBuzz | See the shaping gotcha below |
| Images | ✅ PNG only | ✅ all Skia formats | See below |
| Group opacity (`.Opacity`) | ⚠️ approximated | ✅ real layer | See below |
| Arbitrary `Path` geometry | ❌ | ❌ | Not in `ICanvas` on any backend |

## Gotchas

- **Group opacity is approximated.** `SaveLayer` multiplies alpha rather than compositing a real offscreen
  layer. Correct for a faded subtree whose children do not overlap; where they *do* overlap, they show
  through one another instead of fading as one composite. Fixing it needs a second render target and a
  nested pass.
- **Clips are axis-aligned boxes in device space.** `ClipRect` transforms the rect's corners and takes their
  bounding box. The engine only clips scroll viewports, which are never rotated, so this is exact in
  practice — but a clip applied under a rotation would over-admit at the corners.
- **PNG only for images.** That is the honest limit of the shared decoder. A JPEG or WebP asset decodes to
  null and the node paints nothing, exactly as it does for a failed download. Register a richer decoder via
  `WebGpuImages.Fallback`.
- **No complex text shaping.** Glyphs are placed by advance width, one code point at a time. Latin, digits
  and emoji are fine; scripts needing ligatures, reordering or contextual forms (Arabic, Devanagari) are
  not. The Skia backend has HarfBuzz for this; matching it here means adding a shaping library.
- **Reserved words in generated shader code.** The WGSL is translated to MSL/HLSL/SPIR-V by naga, and a WGSL
  identifier that is a *reserved word in the target language* produces broken output rather than a clean
  error. `device` and `half` are both reserved in Metal Shading Language — that cost a debugging cycle
  here, so avoid them and their peers when editing [`Shaders.cs`](../../src/SwiftDotNet.WebGpu/Shaders.cs).
- **Headless needs an explicit poll.** WebGPU leaves callback resolution to the host environment; a browser
  has an event loop, a headless process has nothing. `RenderToRgba` pumps `wgpuDevicePoll` until the
  readback map completes.

## Status

**✅ Verified against a real GPU.** Rendered on wgpu-native → **Metal** (Apple M5 Pro) with pixel readback
asserted by 8 tests in [`WebGpuRenderTests`](../../tests/SwiftDotNet.Tests/WebGpuRenderTests.cs): fill
placement and colour, corner cutting, circle roundness, gradient interpolation, clipping, translation, glyph
coverage, and an end-to-end run through the DSL.

Those tests skip themselves when no adapter is present, so CI without a GPU stays green — which also means
**Vulkan and D3D12 are unexercised**. Nothing in the backend is Metal-specific, but neither has been run.

Not yet done: a windowed host (only the headless one exists — a Silk.NET windowed host would mirror
[`SampleApp.Skia.Silk`](../../sample/SampleApp.Skia.Silk)), and no sample app.

## Source

- [`WebGpuCanvas.cs`](../../src/SwiftDotNet.WebGpu/WebGpuCanvas.cs) — records `ICanvas` calls as instances
- [`Shaders.cs`](../../src/SwiftDotNet.WebGpu/Shaders.cs) — the WGSL SDF shader
- [`WebGpuRenderer.cs`](../../src/SwiftDotNet.WebGpu/WebGpuRenderer.cs) — device, pipeline, submission, readback
- [`WebGpuFonts.cs`](../../src/SwiftDotNet.WebGpu/WebGpuFonts.cs) — stb_truetype provider + glyph atlas
- [`WebGpuImages.cs`](../../src/SwiftDotNet.WebGpu/WebGpuImages.cs) — PNG decode → texture
- [`WebGpuBridge.cs`](../../src/SwiftDotNet.WebGpu/WebGpuBridge.cs) — bridge + headless host

## See also

- [Architecture → the renderer seam](../architecture.md#the-renderer-seam) — why the engine is separable
- [Skia backend](skia.md) — the other consumer of the same engine
- [Backends overview](README.md)
