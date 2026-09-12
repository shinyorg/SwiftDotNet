# Video, camera & web surfaces

A **surface** is a node whose pixels come from outside the engine — a decoded movie, a live camera
preview, a rendered web page. The self-drawing backends cannot host a native `AVPlayerLayer`,
`SurfaceView` or `WKWebView`, so instead of hosting one they **pump frames**: a platform producer hands
the engine raw RGBA, the engine uploads it and draws it through
[`ICanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs) like any other pixel.

That is the whole idea, and it is what makes the frame behave like UI rather than like a hole in the
window: it is clipped by its scroll view, rounded by `.CornerRadius`, moved by `.Offset`, hidden under a
sheet, and captured by the headless PNG harness.

```csharp
readonly State<bool> _playing = new(true);
readonly State<CameraFacing> _facing = new(CameraFacing.Back);

public override View Body => new VStack(
        new VideoView("https://example.com/clip.mp4")
            .ContentMode(VideoContentMode.Fill)
            .Loop()
            .Playing(_playing)
            .OnReady(i => _duration.Value = i.Duration)
            .CornerRadius(12)
            .Frame(height: 220),

        new Button(_playing.Value ? "Pause" : "Play", () => _playing.Value = !_playing.Value),

        new CameraView(_facing, _shutter)
            .OnPhoto(p => Save(p.Data))
            .CornerRadius(20)
            .Frame(height: 260))
    .Spacing(12);
```

Three controls ride the same seam:

| Control | Package | Node type |
|---|---|---|
| [`VideoView`](../src/SwiftDotNet.Media/VideoView.cs) | `SwiftDotNet.Media` | `VideoView` |
| [`CameraView`](../src/SwiftDotNet.Controls.Camera/CameraView.cs) | `SwiftDotNet.Controls.Camera` | `CameraView` |
| [`WebView`](../src/SwiftDotNet/Core/Views/WebView.cs) | core | `WebView` |

## Turning it on

Nothing is registered by default: a surface with no provider paints a labelled placeholder, never a fake
feed. Register the producer for the platform you are on, once at startup:

```csharp
builder.UseSwiftApp<ContentView>()
       .UseAppleMedia()             // iOS / Mac Catalyst / macOS / tvOS  — AVFoundation (+ WebKit with web: true)
       .UseAndroidMedia(web: true)  // Android — Camera2, MediaCodec, android.webkit.WebView
       .UseWindowsMedia()           // Windows — MediaCapture (camera only)
       .UseVlcMedia();              // any desktop — LibVLC (files, HLS, RTSP, and webcams with camera: true)
```

Each is a separate package so an app only links the media stack it asks for — the same rule that keeps
[`SwiftDotNet.Maps.Apple`](maps.md) out of an app that has no map.

| Package | `VideoView` | `CameraView` | `WebView` | Notes |
|---|---|---|---|---|
| [`SwiftDotNet.Media.Apple`](../src/SwiftDotNet.Media.Apple) | ✅ `AVPlayer` + `AVPlayerItemVideoOutput` | ✅ `AVCaptureVideoDataOutput` (not tvOS) | ⚠️ `WKWebView` snapshots — opt-in, slow | iOS / Catalyst / macOS / tvOS |
| [`SwiftDotNet.Media.Android`](../src/SwiftDotNet.Media.Android) | ✅ `MediaCodec` + audio via a surface-less `MediaPlayer` | ✅ Camera2 + `ImageReader` | ✅ off-screen `WebView`, **real touch input** | Platform SDK only — no CameraX, no AndroidX |
| [`SwiftDotNet.Media.Windows`](../src/SwiftDotNet.Media.Windows) | ❌ use the VLC package | 🧩 `MediaFrameReader` | ❌ WebView2 has no off-screen mode | **Never compiled** — needs Windows |
| [`SwiftDotNet.Media.Vlc`](../src/SwiftDotNet.Media.Vlc) | ✅ files, http/HLS/DASH, RTSP | ✅ opt-in (`dshow://`, `avcapture://`, `v4l2://`) | ❌ | Any desktop; the app supplies native libvlc |
| built-in test pattern | ✅ | ✅ | ✅ | `FrameSources.RegisterTestPattern()` — generated bars, no hardware |

## Which backends can draw a surface

A rasterizer can show frames when its image decoder also implements
[`IPixelUploader`](../src/SwiftDotNet.Graphics/Surfaces.cs) — raw pixels in, drawable image out.

| Backend | Surfaces | Why |
|---|---|---|
| [Skia](backends/skia.md) | ✅ | `SkiaImages.Upload` → `SKImage.FromPixelCopy` |
| [MonoGame](backends/monogame.md), [Unity](backends/unity.md) | ✅ | both host the Skia engine, so they inherit it |
| [WebGPU](backends/webgpu.md) | ✅ | uploads to a texture, and reuses it across frames |
| [Godot](backends/godot.md) | ✅ | `ImageTexture.Update` refreshes in place |
| [TUI](backends/tui.md) | ❌ | not the self-drawing engine — `TuiBridge` is its own `IBridge` |
| Apple / Android / GTK / WinUI / Web | n/a | widget-backed: they host a **real** player/preview/web view, and always have |

A backend with no uploader does not fail silently: the node reports *"This backend cannot display frames
(no pixel uploader)"* in its placeholder.

## How the seam works

Five small pieces, all in [`Surfaces.cs`](../src/SwiftDotNet.Graphics/Surfaces.cs) and
[`SurfaceRenderer.cs`](../src/SwiftDotNet.Graphics/SurfaceRenderer.cs):

| Piece | Role |
|---|---|
| `PixelFrame` | One frame: straight RGBA8 `Pixels`, plus `Rotation` and `Mirrored`. |
| `IFrameSource` / `FrameSource` | Produces frames. Parks the newest in `CurrentFrame`, bumps `Sequence`, asks for a repaint. |
| `FrameSources` | The registry a platform package plugs into. Most recently registered factory wins. |
| `FrameSurfaces` | Per-bridge: the live source per node, its uploaded texture, and lifetime. |
| `SurfaceRenderer` | The `IVisualRenderer` that fits, rotates, mirrors, clips and draws. |

Some deliberate choices worth knowing:

- **Nothing queues.** A source that outruns the display drops frames rather than growing a backlog, which
  is what a preview wants. The renderer re-uploads only when `Sequence` moved.
- **Rotation and mirroring are carried, not baked.** A phone camera sensor is mounted 90° out and a front
  camera is expected to be mirrored; the canvas folds both into its matrix for free, while rotating the
  buffer would cost a second full pass per frame.
- **Lifetime is resolved by sweeping.** A renderer never hears "this node went away" — a `setChildren`
  patch can drop a whole subtree silently — so every paint touches its surface and anything untouched for
  `FrameSurfaces.GraceSeconds` (2s) is disposed at the end of a frame. **A camera scrolled out of view or
  left in a hidden tab therefore shuts itself off**, and restarts when it is painted again.
- **Surfaces are per-bridge, not process-wide.** Node ids are structural paths and repeat across trees, so
  two windows (or two tests) would otherwise hand each other's cameras out under the same key.

### Interactive surfaces (`WebView`)

A page needs input, which a movie does not. A source that also implements
[`ISurfaceInput`](../src/SwiftDotNet.Graphics/Surfaces.cs) receives taps, scrolls, typing and size changes
for its node, in its **own normalized 0–1 space** — the source renders at a resolution only it knows, so
the engine divides its own pixels out rather than making every provider re-derive the scale.

Because `WebView` is a *built-in* node with its own placeholder, the engine only routes it to the surface
renderer when a provider actually serves it; otherwise it keeps painting the familiar
"native WebView — not drawable on a canvas" card.

Input fidelity differs by provider, and the difference is large:

- **Android** dispatches real `MotionEvent`s and `KeyEvent`s into a real (unattached) `WebView`, so links,
  forms, scrolling and selection behave exactly as the page expects.
- **Apple** replays input as script against the document (`elementFromPoint(...).click()`, `scrollBy`),
  because WebKit renders out of process and never sees a touch. Good enough for links and scrolling; not a
  substitute for a real web view.

### The other route: float a real control instead

Pumping frames is not the only way, and not always the best one. The
[platform-view seam](architecture.md#the-platform-view-seam) floats a **real OS control** above the canvas
for a registered node type, which the [MAUI Skia host](maui-interop.md) implements today.

| | Pumped frames (this page) | Platform view (`IPlatformViewHost`) |
|---|---|---|
| Works on hosts with no view hierarchy (Silk, MonoGame, Godot, headless) | ✅ | ❌ |
| Clipped, rounded, z-ordered, transformed with the rest of the UI | ✅ | ❌ always on top, transforms are a no-op |
| Appears in the headless PNG harness | ✅ | ❌ |
| Cost | a copy + an upload per frame | zero |
| Hardware decode path preserved end to end | partly | ✅ |
| DRM / protected content | ❌ | ✅ |
| Web input fidelity | real on Android, approximated on Apple | ✅ always real |

**Rule of thumb:** inside a MAUI app, full-screen video and a serious web view are better as platform
views. Everywhere else — and for anything that has to composite with the UI around it — pump frames.

## Gotchas

- **No built-in transport controls.** Play/pause/scrub/volume are ordinary `State<T>` in your view, so the
  chrome is composed from the DSL and is therefore themed, localized and testable. `VideoView` gives you
  `Playing`, `Seek`, `OnPosition`, `OnReady` and `OnEnded` to build it from.
- **Seeking is a value, not an edge.** `.Seek(state)` compares against the last target it sought to, so it
  survives a tree rebuild — but writing the *same* `TimeSpan` twice in a row is one seek, not two.
- **`.CornerRadius` rounds the frame only where the rasterizer can clip a rounded rect**
  ([`IRoundedClipCanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs)) — Skia today. Elsewhere the background
  rounds and the content squares off, exactly as it already does for `Image`.
- **Resolution costs CPU, twice.** Every frame is converted (YUV→RGBA on Android, BGRA→RGBA on Apple and
  Windows) and uploaded. 720p is comfortable; 4K is not. The Android camera provider deliberately picks the
  smallest supported size ≥ 720p rather than the sensor's full resolution.
- **Android opens the source twice** — once for `MediaCodec` (picture) and once for the surface-less
  `MediaPlayer` (audio + clock). Prefer a local or cached file for anything large.
- **Apple `WebView` is opt-in and slow** (`UseAppleMedia(web: true)`). `WKWebView.TakeSnapshot` is an IPC
  round trip; fine for a page you read, visibly poor for one you fling.
- **Permissions are the app's.** `NSCameraUsageDescription` in Info.plist (the process is *killed* without
  it), `android.permission.CAMERA` requested at runtime, the `webcam` capability for a packaged Windows app.
- **LibVLC needs its native library.** Add `VideoLAN.LibVLC.Windows` / `VideoLAN.LibVLC.Mac` to the head
  project, or install `libvlc` on Linux; the provider reports that clearly instead of throwing.
- **The test pattern is for wiring, not for demos of capture.** `FrameSources.RegisterTestPattern()` gives
  every backend a live, obviously synthetic feed with no hardware — the fastest way to check that a new
  host's repaint pump actually runs.

## Status

| Piece | Status |
|---|---|
| The frame seam, renderer, fit/rotation/mirroring, lifetime sweep, rounded clip | ✅ Verified — 20 headless tests in [`SurfaceSeamTests`](../tests/SwiftDotNet.Tests/SurfaceSeamTests.cs), driven against the real Skia rasterizer on macOS |
| `VideoView` DSL, event grammar, two-way `Playing` | ✅ Verified headlessly |
| End-to-end draw (video + camera + web, fit/fill/mirror/rounded) | ✅ Verified — `dotnet run --project sample/SampleApp.Skia -- <dir> media` writes `media.png` |
| Pixel uploaders: Skia, WebGPU, Godot | ✅ Skia verified by the tests above; WebGPU and Godot **compile, never run with a live source** |
| `SwiftDotNet.Media.Apple` (camera, video, web) | 🧩 **Compiles** for ios / maccatalyst / macos / tvos; **never run on a device** |
| `SwiftDotNet.Media.Android` (camera, video, web) | 🧩 **Compiles**; **never run on a device or emulator** |
| `SwiftDotNet.Media.Vlc` | 🧩 **Compiles**; **never run** (no native libvlc in this repo's build) |
| `SwiftDotNet.Media.Windows` | 🧩 **Never compiled** — needs a Windows machine |

The platform providers are the half that a PNG cannot prove. Each is written against the documented API
and builds clean; none has been exercised against real hardware yet.

## See also

- [Architecture](architecture.md) — where the frame seam sits next to the platform-view seam.
- [Skia backend](backends/skia.md) — the reference self-drawing host.
- [MAUI Interop](maui-interop.md) — the platform-view alternative, and when to prefer it.
- [Custom Controls](custom-controls.md) — the renderer registry these controls resolve through.
- [Views & Controls](views-and-controls.md#video) — `VideoView` in the vocabulary.
