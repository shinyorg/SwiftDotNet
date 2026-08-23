# Unity backend

Runs a SwiftDotNet UI inside a Unity scene. The engine is unchanged — layout, hit-testing, gestures and the
paint pass are the same [`SwiftDotNet.Graphics`](../../src/SwiftDotNet.Graphics) code every other
self-drawing backend uses. Unity supplies only what a host owes the engine: a surface to draw into, a
pointer stream, and a repaint signal.

> **Status up front: this backend has never been run.** The .NET side is built and green in CI, and the
> host component now **compiles** against real UnityEngine reference assemblies
> ([`tooling/unity-compile-check.sh`](../../tooling/unity-compile-check.sh)) — but nothing here has been
> loaded into an Editor, drawn a frame, or handled a click. Treat
> [`SwiftDotNetView.cs`](../../unity/com.swiftdotnet.unity/Runtime/SwiftDotNetView.cs) as code that type-checks
> against the Unity API, not as tested code. See [Status](#status).
>
> Of the three game-engine hosts, **[Godot](godot.md) and [MonoGame](monogame.md) are verified** and this one
> is not — for the mundane reason that both of those can be installed and driven from a shell, and Unity
> cannot.

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

## The compile check

[`tooling/unity-compile-check.sh`](../../tooling/unity-compile-check.sh) fetches UnityEngine reference
assemblies from NuGet into a temp directory and compiles `SwiftDotNetView.cs` against them plus the
netstandard2.1 build of the engine. It is opt-in and downloads nothing into the repo — the reference package
is a third-party republish of Unity's assemblies, not something to take a build dependency on.

```sh
tooling/unity-compile-check.sh
```

What it proves: the host's API surface is real — no misspelled members, no wrong signatures, and the
netstandard2.1 assemblies it needs actually resolve. What it does not prove:

- It is **not a run**. Nothing creates a `Texture2D`, pumps input, or draws a frame.
- The references are **Unity 2021.1**, the newest republished on NuGet. Every API the host touches
  (`MonoBehaviour`, `Texture2D`, `Input`, `Screen`, `GUI`, `NativeArrayUnsafeUtility`) long predates that,
  but a Unity 6-only regression would not show up.
- **uGUI is stubbed.** `RawImage` ships in `com.unity.ugui`, not `UnityEngine.dll`, so the two members the
  host touches are declared in the script. Those two are checked against Unity's docs, not a compiler.

### The defect it surfaced

The setup instructions above used to be impossible to follow. They say to drop `SwiftDotNet.Skia.dll` into
`Assets/Plugins/` — but that project targeted **net10.0 only**, and Unity's scripting runtime cannot load a
net10.0 assembly. Core and Graphics had been retargeted for Unity; the Skia adapter, which is the thing that
actually draws, had not. It now multi-targets `net10.0;net8.0;netstandard2.1` like the rest of the stack.
That one-line omission would have been the first thing anyone following this page hit.

## Status

| Piece | Status |
|---|---|
| Core targeting netstandard2.1 | ✅ Builds |
| `SwiftDotNet.Graphics` targeting netstandard2.1 | ✅ Builds |
| `SwiftDotNet.Skia` targeting netstandard2.1 | ✅ Builds — **new**; the package was previously unloadable without it |
| Existing backends unaffected by the retarget | ✅ 364 tests green |
| `SwiftDotNetView` MonoBehaviour | 🧩 **Compiles** against UnityEngine 2021.1 reference assemblies; **never run** |
| IL2CPP / AOT | 🧩 Untested. Core is already reflection-free and `IsTrimmable`, so this *should* be free — unverified. |
| Safe area, soft keyboard | ❌ Not implemented |

The honest next step is still to open the package in a Unity 6 project and run it. Everything below the host
— the entire engine — is the same code the test suite already covers, and it is now demonstrably reachable
from a netstandard2.1 assembly.

## Source

- [`unity/com.swiftdotnet.unity/`](../../unity/com.swiftdotnet.unity) — the Unity package
- [`tooling/unity-compile-check.sh`](../../tooling/unity-compile-check.sh) — the compile check
- [`Compat/Netstandard.cs`](../../src/SwiftDotNet/Compat/Netstandard.cs) — the polyfills
- [`Throw.cs`](../../src/SwiftDotNet/Core/Throw.cs) — the guard helper the retarget needed

## See also

- [Skia backend](skia.md) — what actually draws the pixels
- [Godot backend](godot.md) and [MonoGame backend](monogame.md) — the same host shape, verified
- [Architecture → the renderer seam](../architecture.md#the-renderer-seam)
- [Backends overview](README.md)
