# SwiftDotNet for Unity

Renders a [SwiftDotNet](../../README.md) UI tree inside a Unity scene.

The engine is unchanged — layout, hit-testing, gestures and the paint pass are the same
`SwiftDotNet.Graphics` code every other self-drawing backend uses. This package supplies only the Unity
host: a `Texture2D` to draw into, a pointer pump, and a repaint signal.

> ⚠️ **Unverified.** This package has never been compiled or run — it was written without a Unity Editor
> available. The .NET assemblies it depends on *are* built and tested; the MonoBehaviour is a careful first
> draft. Expect to fix compile errors on first open.

## Install

1. Build the .NET assemblies for Unity's scripting runtime:
   ```sh
   dotnet build src/SwiftDotNet/SwiftDotNet.csproj                   -f netstandard2.1
   dotnet build src/SwiftDotNet.Graphics/SwiftDotNet.Graphics.csproj -f netstandard2.1
   ```
2. Copy `SwiftDotNet.dll`, `SwiftDotNet.Graphics.dll`, `SwiftDotNet.Skia.dll` and `SkiaSharp.dll` into
   `Assets/Plugins/`.
3. Add the native `libSkiaSharp` binary for each target platform under `Assets/Plugins/<platform>/`.
4. Copy this folder into your project's `Packages/`.

## Use

```csharp
using SwiftDotNet;
using SwiftDotNet.Unity;

public sealed class MyAppView : SwiftDotNetView
{
    protected override View BuildRoot() => new ContentView();
}
```

Attach to a GameObject. Assign a UGUI `RawImage` to `Target` to render into a canvas, or leave it empty to
draw full-screen.

## Known gaps

- Safe area is not wired (`SafeArea.Update` is internal to Core).
- Soft keyboard is not wired — the engine reports focus via `SkiaBridge.FocusChanged`, and a host would open
  `TouchScreenKeyboard` from it.
- Input uses the legacy `Input` API; the new Input System would be a drop-in replacement in `PumpInput`.
- IL2CPP/AOT is untested. Core is reflection-free and `IsTrimmable`, so it should be fine.

Full detail: [`docs/backends/unity.md`](../../docs/backends/unity.md).
