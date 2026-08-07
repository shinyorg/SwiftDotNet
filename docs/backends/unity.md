# Unity backend

Runs a SwiftDotNet UI inside a Unity scene. The engine is unchanged — layout, hit-testing, gestures and the
paint pass are the same [`SwiftDotNet.Graphics`](../../src/SwiftDotNet.Graphics) code every other
self-drawing backend uses. Unity supplies only what a host owes the engine: a surface to draw into, a
pointer stream, and a repaint signal.

> **Status up front: this backend is unverified.** The .NET side (Core and the engine targeting
> netstandard2.1) is built and green in CI. The Unity host component itself has **never been compiled or
> run** — that needs a Unity Editor install, which the development machine did not have. Treat
> [`SwiftDotNetView.cs`](../../unity/com.swiftdotnet.unity/Runtime/SwiftDotNetView.cs) as a careful first
> draft against the documented Unity API, not as tested code. See [Status](#status).

## Which route, and why

Two designs were possible:

| | **Route A — SkiaSharp into a `Texture2D`** (chosen) | Route B — node tree → UI Toolkit `VisualElement`s |
|---|---|---|
| Effort | Host adapter only | Re-implement the whole node→widget mapping |
| Fidelity | Pixel-identical to every other Skia target | Unity-native, inspectable in UI Builder |
| Layout | The engine's (SwiftUI semantics) | Unity's Yoga flexbox, which disagrees in places |
| Controls | Everything already verified on Skia | Each control ported again |

Route A reuses a backend that already works and costs days rather than months. Route B is the
"bindable toolkit" route (the same family as [GTK](linux-gtk.md), [WinUI](windows.md) and [Web](web.md)) and
remains open if Unity-native-ness ever becomes the point — see [roadmap](../roadmap.md).

## How it works

```
SwiftDotNetView (MonoBehaviour)
   │
   ├── SkiaBridge ────────► the shared engine
   ├── SkiaPointerRouter ─► taps / long-press / swipe / drag / scroll
   │
   └── Texture2D ◄── SKSurface draws directly into the texture's own memory
           │
           └──► RawImage (UGUI)   or   GUI.DrawTexture (full-screen)
```

Skia composites straight into the texture's raw bytes, so a frame costs one `Apply()` upload and no
intermediate copy or encode. Repaint is demand-driven: the engine raises `Invalidate` when a patch lands,
which is the only thing that can change the pixels.

## Usage

```csharp
using SwiftDotNet;
using SwiftDotNet.Unity;

public sealed class MyAppView : SwiftDotNetView
{
    protected override View BuildRoot() => new ContentView();
}
```

Attach `MyAppView` to a GameObject. Assign a `RawImage` to `Target` to render into a UGUI canvas, or leave
it empty to draw full-screen.

## Setup

1. **Build the .NET assemblies for netstandard2.1:**
   ```sh
   dotnet build src/SwiftDotNet/SwiftDotNet.csproj             -f netstandard2.1
   dotnet build src/SwiftDotNet.Graphics/SwiftDotNet.Graphics.csproj -f netstandard2.1
   ```
2. Drop `SwiftDotNet.dll`, `SwiftDotNet.Graphics.dll`, `SwiftDotNet.Skia.dll` and `SkiaSharp.dll` into
   `Assets/Plugins/`.
3. Add the native Skia binary (`libSkiaSharp`) for each target platform under `Assets/Plugins/<platform>/`.
4. Copy [`unity/com.swiftdotnet.unity`](../../unity/com.swiftdotnet.unity) into `Packages/`.

## Why Core multi-targets netstandard2.1

Unity 6's scripting runtime is **netstandard2.1**; CoreCLR is coming but is not something to build on yet.
Core and the engine therefore add a `netstandard2.1` TFM alongside `net10.0`. Three things that needed:

- **Compiler polyfills** — `init`, `required` members and the trimming annotations need
  `IsExternalInit`, `RequiredMemberAttribute` and friends to *exist*. They are declared in
  [`Compat/Netstandard.cs`](../../src/SwiftDotNet/Compat/Netstandard.cs), compiled only for that TFM and
  `internal` so they never become API.
- **BCL gaps** — `ArgumentNullException.ThrowIfNull` and `ObjectDisposedException.ThrowIf` are .NET 6+, so
  every guard now routes through [`Throw`](../../src/SwiftDotNet/Core/Throw.cs) on *all* targets rather than
  scattering `#if`. `ZLibStream` is .NET 6+ too, so the PNG decoder strips zlib's 2-byte header and inflates
  with `DeflateStream` there. `string.EnumerateRunes()` is unavailable, so text segmentation uses
  [`TextLayout.CodePoints`](../../src/SwiftDotNet.Graphics/Text.cs) everywhere — one implementation, so every
  target segments identically.
- **Excluded files** — [`HotReload.cs`](../../src/SwiftDotNet/Core/HotReload.cs) rides on
  `System.Reflection.Metadata.MetadataUpdateHandler`, which predates .NET 5. Unity has its own domain-reload
  story, so the file is excluded rather than stubbed into something that silently never fires.

### An MSBuild trap worth knowing

Adding the TFM surfaced a latent bug: for any **non-.NET5+ TFM, MSBuild defaults
`$(TargetPlatformIdentifier)` to `windows`**. Core gated both its WinUI `PackageReference` and its entire
`Platforms/Windows/**` compile item on `'$(TargetPlatformIdentifier)' == 'windows'`, so the netstandard2.1
build silently pulled in the Windows App SDK (which then failed its own "requires .NET 6.0" check) and tried
to compile the whole WinUI backend. Both are now conditioned on `$(TargetFramework.Contains('-windows'))`,
which is what the file's own property groups already used.

## Per-backend behaviour

Rendering is the Skia backend's, so [its behaviour table](skia.md) applies verbatim. Host-level differences:

| Concern | Behaviour |
|---|---|
| Input | Legacy `Input` (universally available). The new Input System would be a drop-in replacement in `PumpInput`. |
| Coordinates | Unity's origin is bottom-left, the engine's is top-left; `ToCanvas` flips and maps into the `RawImage` rect. |
| Display scale | `Screen.dpi / 96` by default; set `UseDisplayScale = false` to render at 1× and upscale. |
| Dark mode | Manual — set the `Dark` field. Unity exposes no OS appearance API. |
| Safe area | **Not wired.** `SafeArea.Update` is internal to Core; feeding Unity's `Screen.safeArea` through needs a host-facing entry point that does not exist yet. |
| Text input | **Not wired.** The engine reports focus via `FocusChanged`; a Unity host would open `TouchScreenKeyboard` from it. |

## Status

| Piece | Status |
|---|---|
| Core targeting netstandard2.1 | ✅ Builds |
| `SwiftDotNet.Graphics` targeting netstandard2.1 | ✅ Builds |
| Existing backends unaffected by the retarget | ✅ 271 tests green |
| `SwiftDotNetView` MonoBehaviour | 🧩 **Never compiled or run** — no Unity install available |
| IL2CPP / AOT | 🧩 Untested. Core is already reflection-free and `IsTrimmable`, so this *should* be free — unverified. |
| Safe area, soft keyboard | ❌ Not implemented |

The honest next step is to open the package in a Unity 6 project, fix whatever the compiler says, and run
it. Everything below the host — the entire engine — is the same code the 271-test suite already covers.

## Source

- [`unity/com.swiftdotnet.unity/`](../../unity/com.swiftdotnet.unity) — the Unity package
- [`Compat/Netstandard.cs`](../../src/SwiftDotNet/Compat/Netstandard.cs) — the polyfills
- [`Throw.cs`](../../src/SwiftDotNet/Core/Throw.cs) — the guard helper the retarget needed

## See also

- [Skia backend](skia.md) — what actually draws the pixels
- [Architecture → the renderer seam](../architecture.md#the-renderer-seam)
- [Backends overview](README.md)
