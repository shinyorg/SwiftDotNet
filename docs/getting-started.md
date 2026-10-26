# Getting Started

This guide gets the shared sample UI ([`sample/SharedUI/ContentView.cs`](../sample/SharedUI/ContentView.cs))
running on each platform. Every backend renders the *same* C# view tree — the only thing that differs is
which renderer you build for.

## Prerequisites

| Target | You need |
|--------|----------|
| All | .NET 10 SDK |
| iOS / macOS / tvOS | A Mac + **Xcode 27.1 or later**; the Swift bridge built once (`native/SwiftDotNetBridge/build-xcframework.sh`, which refuses older Xcodes — the shim still deploys to iOS 17 but calls iOS 27.1 foldable API behind `#available`) |
| Android | JDK 21 + Android SDK; the Compose bridge `.aar` built once (Gradle) |
| Linux / GTK | GTK4 native libs (`brew install gtk4` / `apt install libgtk-4-1`) |
| Windows | A Windows machine (WinUI 3 / Windows App SDK don't build on macOS) |
| Web | The `wasm-tools` workload (`dotnet workload install wasm-tools`) |
| Skia | Nothing extra — pure C#, SkiaSharp is a NuGet package |
| MonoGame | Nothing extra — MonoGame is a NuGet package |
| Godot | The **.NET ("mono") build** of Godot 4.x. The plain build has no C# support. |

All projects are wired into **[`SwiftDotNet.slnx`](../SwiftDotNet.slnx)** at the repo root.

## Hello, counter

The authoring surface mirrors SwiftUI. A view subclass owns some `State<T>` and returns a `Body`:

```csharp
public sealed class ContentView : View
{
    readonly State<int> _count = State(0);      // mirrors @State private var count = 0

    public override View Body =>
        new VStack(
            new Text($"Count: {_count.Value}").Font(Font.LargeTitle),
            new Text("Tap the button to increment").Font(Font.Caption).ForegroundColor(Color.Secondary),
            new Button("Increment", () => _count.Value++)
        ).Spacing(24);
}
```

Assigning `_count.Value` invalidates the view and triggers a re-render; the [diff engine](architecture.md#diff-engine)
turns that into a minimal patch that reaches only the changed nodes. See
**[State & Data Binding](state-and-binding.md)** for the full model.

## Build & run per platform

### iOS (SwiftUI)

```bash
# 1. Build the Swift bridge (iOS/tvOS/macOS slices, min iOS 17)
native/SwiftDotNetBridge/build-xcframework.sh
# 2. Build the sample app for the simulator
dotnet build sample/SampleApp/SampleApp.csproj -f net10.0-ios -r iossimulator-arm64
# 3. Install + launch
xcrun simctl install booted sample/SampleApp/bin/Debug/net10.0-ios/iossimulator-arm64/SampleApp.app
xcrun simctl launch booted com.swiftdotnet.sample
```

> **iOS 27: the scene life cycle is mandatory.** An app built with the iOS 27 SDK that has no
> `UIApplicationSceneManifest` is killed at launch (`UIScene life cycle is required for apps built with this
> SDK` — seen on the iOS 27.1 simulator). Declare one whose `UIWindowSceneSessionRoleApplication` delegate is
> `SwiftDotNetSceneDelegate`, as [the sample's Info.plist](../sample/SampleApp/Platforms/iOS/Info.plist) does;
> `SwiftDotNetAppDelegate` then leaves the window to the scene. It's also the host the iPhone Duo hinge
> updates were verified on. (MAUI heads, e.g. the Skia MAUI sample, point the manifest at a
> `MauiUISceneDelegate` subclass instead.)
>
> **Foldables check:** `SIMCTL_CHILD_SDN_SAMPLE_PAGE=foldables xcrun simctl launch booted com.swiftdotnet.sample`
> opens the Foldables page as the root (`tabs` wraps the tour in a tab bar; `foldables-wide` forces the
> side-by-side split; `SIMCTL_CHILD_SDN_TWOPANE_NATIVE=0` starts with the composite). On an iPhone Duo
> simulator, the hinge is driven from Xcode 27.1's **DeviceHub** app (there is no `simctl` command for it).

### macOS / tvOS

Reuse the same xcframework from step 1 above, then select the target with `-f`:

```bash
dotnet build sample/SampleApp -f net10.0-macos
dotnet build sample/SampleApp -f net10.0-tvos
```

### Apple Watch (watchOS)

A separate head — a plain `net10.0` project the watch SDK turns into a watchOS `.app` (NativeAOT, restamped from
the iOS runtime packs). It compiles the shared `Bridge.swift` itself, so the xcframework isn't needed:

```bash
dotnet run --project sample/SampleApp.Watch                # live mode: C# on the Mac, the watch simulator renders
dotnet run --project sample/SampleApp.Watch -c Release     # NativeAOT, installed + launched on a watch simulator
# pick a simulator: -p:WatchSimulator="Apple Watch Series 12 (46mm)" (name or UDID)
```

Needs Xcode with a watchOS simulator runtime. See [Apple Watch](backends/watchos.md).

### Android (Jetpack Compose)

```bash
# Build the .aar first, then the app
native/SwiftDotNetComposeBridge/gradlew -p native/SwiftDotNetComposeBridge assembleRelease
dotnet build sample/SampleApp -f net10.0-android
```

> **Gotcha:** after rebuilding the `.aar`, delete the `obj`/`bin` of both `src/SwiftDotNet` and
> `sample/SampleApp` before rebuilding — incremental builds reuse a stale binding. See
> [Android backend](backends/android.md).

> **Your activity must declare `ConfigurationChanges`.** Folding, unfolding and rotating change the screen
> size; without these flags Android destroys and recreates the activity — and with it the render tree and
> every `State<T>` — instead of letting Compose and [`WindowPosture`](modifiers-gestures-animation.md#window-posture-foldables--iphone-duo)
> re-lay out the same tree:
>
> ```csharp
> [Activity(Label = "MyApp", MainLauncher = true,
>     ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
>         | ConfigChanges.ScreenLayout | ConfigChanges.Orientation)]
> public sealed class MainActivity : SwiftDotNetActivity { … }
> ```
>
> It can't be inherited from `SwiftDotNetActivity`: `ActivityAttribute` is `Inherited = false`, so the
> manifest entry comes only from your concrete class.
>
> **Foldables check:** a Pixel Fold AVD (`avdmanager create avd -d pixel_fold …`), then
> `adb shell setprop debug.mono.env SDN_SAMPLE_PAGE=foldables` before launching (Debug builds) opens the
> Foldables page as the root. `adb emu posture 2` half-opens it, `adb emu rotate` turns a book fold into a
> tabletop one, `adb emu fold` closes it onto the outer display.

### Wear OS (Compose for Wear OS)

The same Gradle run also builds the Wear skin (`wear` module). Copy both `.aar`s into `build/`, boot a Wear
OS AVD, then install the watch sample:

```bash
cp native/SwiftDotNetComposeBridge/build/outputs/aar/SwiftDotNetComposeBridge-release.aar build/SwiftDotNetComposeBridge.aar
cp native/SwiftDotNetComposeBridge/wear/build/outputs/aar/wear-release.aar build/SwiftDotNetWearBridge.aar
dotnet build sample/SampleApp.Wear -t:Install -p:AdbTarget="-s emulator-5554"
```

See [Wear OS backend](backends/wear-os.md).

### CarPlay & Android Auto

```bash
# CarPlay — install on an iOS simulator, then Simulator → I/O → External Displays → CarPlay
dotnet build sample/SampleApp.Car -f net10.0-ios
# Android Auto — no head unit needed to check the templates: tap "Validate Android Auto templates"
dotnet build sample/SampleApp.Car -f net10.0-android -t:Install
adb logcat -s CarDiag
```

The CarPlay category entitlement works in the simulator without Apple's approval. A Desktop Head Unit run
needs the full Android Auto app (emulator images ship a stub). See [CarPlay & Android Auto](car.md).

### Windows (WinUI 3) — on a Windows machine

```powershell
dotnet run --project sample/SampleApp -f net10.0-windows10.0.19041.0
```

The sample is unpackaged + self-contained, so it runs with no prerequisites beyond the .NET SDK.

### Windows (WPF) — on a Windows machine

```powershell
dotnet run --project sample/SampleApp.Wpf
```

Real WPF controls, no shim. See [WPF](backends/wpf.md).

### Windows Forms — on a Windows machine

```powershell
dotnet run --project sample/SampleApp.Skia.WinForms
```

WinForms has no native-control backend by design; this is the [Skia](backends/skia.md) canvas in a
`Control`. See [WinForms](backends/winforms.md) for why.

> **All three Windows-desktop heads build on macOS and Linux too**, because their projects set
> `EnableWindowsTargeting` — which downloads the Windows targeting packs. You cannot *run* them off
> Windows, but a break is caught on the dev machine:
>
> ```bash
> dotnet build src/SwiftDotNet.Wpf
> dotnet build sample/SampleApp.Wpf
> ```
>
> CI does the same on a real `windows-latest` runner (the `windows-desktop` job). The WinUI 3 head is the
> exception — it genuinely needs Windows, and has never compiled anywhere.

### Linux / GTK

```bash
# needs GTK4 (brew install gtk4 / apt install libgtk-4-1)
dotnet run --project sample/SampleApp.Gtk
```

On non-Linux, set `DYLD_FALLBACK_LIBRARY_PATH` / `LD_LIBRARY_PATH` to the GTK libs. See
[Linux/GTK backend](backends/linux-gtk.md) for the macOS `DYLD_*` caveat.

### Web (Blazor WebAssembly)

```bash
dotnet run --project sample/SampleApp.Web          # → http://localhost:5000
```

### Skia (self-drawing)

```bash
# Headless: render ContentView to PNGs
dotnet run --project sample/SampleApp.Skia -- <output-dir>

# Interactive macOS window (AppKit)
dotnet build sample/SampleApp.Skia.Mac -c Release   # then launch the .app

# Dependency-free desktop (Windows/macOS/Linux) via Silk.NET + OpenGL
dotnet run --project sample/SampleApp.Skia.Silk

# Windows desktop, hosted in a WPF window or a WinForms form
dotnet run --project sample/SampleApp.Skia.Wpf
dotnet run --project sample/SampleApp.Skia.WinForms

# Mobile, via the MAUI host (iOS simulator / Android emulator)
dotnet build sample/SampleApp.Skia.Maui -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64
xcrun simctl install booted sample/SampleApp.Skia.Maui/bin/Debug/net10.0-ios/iossimulator-arm64/SampleApp.Skia.Maui.app
xcrun simctl launch --console-pty booted com.swiftdotnet.skia.maui

dotnet build sample/SampleApp.Skia.Maui -f net10.0-android -t:Install   # then launch from the launcher
```

`-p:NoShiny=true` builds the MAUI sample without the Shiny plugins. **Toggling that flag needs a clean**
(`rm -rf sample/SampleApp.Skia.Maui/bin sample/SampleApp.Skia.Maui/obj`) — the app bundle is patched
incrementally, so assemblies from the previous configuration linger and the app dies at launch with
`TypeLoadException: VTable setup of type Microsoft.Maui.Controls.Page failed`.

### MonoGame

```bash
# a window
dotnet run --project sample/SampleApp.MonoGame

# render frames, save the back buffer, exit (no display interaction needed beyond a window)
dotnet run --project sample/SampleApp.MonoGame -- --shot out.png

# tap a node by id first, so the capture exercises the whole loop
dotnet run --project sample/SampleApp.MonoGame -- --shot out.png --tap 0.0.0.0
```

### Godot

Needs the .NET build of the editor/runtime — `Godot_v4.x-stable_mono_<platform>`. Point `GODOT` at the
executable inside the app bundle on macOS.

```bash
GODOT=/Applications/Godot_mono.app/Contents/MacOS/Godot

# a window, drawn with Godot's own renderer (no SkiaSharp)
"$GODOT" --path sample/SampleApp.Godot

# capture and exit
"$GODOT" --path sample/SampleApp.Godot --quit-after 300 -- --shot out.png --tap 0.0.0.0

# the same sample on the Skia-into-a-texture route
"$GODOT" --path sample/SampleApp.Godot res://MainSkia.tscn --quit-after 300 -- --shot out.png
```

A game project referencing the backend needs `<EnableDynamicLoading>true</EnableDynamicLoading>` if it pulls
in any NuGet package — see [the Godot backend's gotchas](backends/godot.md#gotchas).

### Terminal (TUI)

Nothing to install — a plain `net10.0` console app, so it runs wherever a TTY does.

```bash
dotnet run --project sample/SampleApp.Tui

# Real Sixel/Kitty images instead of character art (iTerm2, kitty, WezTerm…)
SDN_TUI_GRAPHICS=1 dotnet run --project sample/SampleApp.Tui

# Compare the character-art modes
SDN_TUI_IMAGE_MODE=quadrant dotnet run --project sample/SampleApp.Tui
```

See [Terminal/TUI](backends/tui.md) — in particular the namespace collision between the DSL and
Terminal.UI, which app code has to steer around.

## Hot reload

Swap `dotnet run` for `dotnet watch run` on any of the heads above and edits to a `Body` apply to the
running app — keeping the page you pushed and the text you typed:

```bash
dotnet watch run --project sample/SampleApp.Skia.Silk
```

No opt-in is needed in your app code. iOS and tvOS are the exception — they need the Mono interpreter and
a reference to the SDK's delta applier, both behind one property:

```bash
cd sample/SampleApp
dotnet watch run -f net10.0-ios --property:SwiftDotNetHotReload=true --device <SIMULATOR-UDID>
```

See **[Hot Reload](hot-reload.md)** for what reloads, what forces a restart, the Apple recipe, and the
per-backend status.

## Preview and inspect

Render your views inside an IDE tool window, and watch the live node tree:

```bash
# an interactive Skia preview of a shared UI project, over a socket
dotnet run --project src/SwiftDotNet.Preview.Host -- \
    --assembly sample/SharedUI/bin/Debug/net10.0/SharedUI.dll --port 51799

# the Rider plugin (run configurations per head, inspector, preview)
cd tooling/rider && ./gradlew runIde
```

Both work without an IDE — the preview host is an ordinary console app. See
**[Rider Plugin & Dev Tools](rider-plugin.md)**.

## Consuming the library in your own app

Reference the combined **`SwiftDotNet`** project/package. Then:

- **Apple targets** — also add `<Import Project="…/SwiftDotNet/SwiftDotNetBridge.targets" />` to your app's
  `.csproj`. This is **required**: `NativeReference` items don't flow transitively into the app's native link,
  so without it you'll get `Undefined symbols _swiftdotnet_*`.
- **GTK / Web / Skia** — plain project references (`SwiftDotNet.Gtk` / `SwiftDotNet.Web` / `SwiftDotNet.Skia`);
  no import needed.

The per-OS bootstrap lives in the library as reusable abstract hosts, so your platform entry point is a
one-liner that names your root view — see [Architecture → Centralized hosting](architecture.md#centralized-hosting--registration).
