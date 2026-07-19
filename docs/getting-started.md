# Getting Started

This guide gets the shared sample UI ([`sample/SharedUI/ContentView.cs`](../sample/SharedUI/ContentView.cs))
running on each platform. Every backend renders the *same* C# view tree — the only thing that differs is
which renderer you build for.

## Prerequisites

| Target | You need |
|--------|----------|
| All | .NET 10 SDK |
| iOS / macOS / tvOS | A Mac + Xcode; the Swift bridge built once (`native/SwiftDotNetBridge/build-xcframework.sh`) |
| Android | JDK 21 + Android SDK; the Compose bridge `.aar` built once (Gradle) |
| Linux / GTK | GTK4 native libs (`brew install gtk4` / `apt install libgtk-4-1`) |
| Windows | A Windows machine (WinUI 3 / Windows App SDK don't build on macOS) |
| Web | The `wasm-tools` workload (`dotnet workload install wasm-tools`) |
| Skia | Nothing extra — pure C#, SkiaSharp is a NuGet package |

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

### macOS / tvOS

Reuse the same xcframework from step 1 above, then select the target with `-f`:

```bash
dotnet build sample/SampleApp -f net10.0-macos
dotnet build sample/SampleApp -f net10.0-tvos
```

### Android (Jetpack Compose)

```bash
# Build the .aar first, then the app
native/SwiftDotNetComposeBridge/gradlew -p native/SwiftDotNetComposeBridge assembleRelease
dotnet build sample/SampleApp -f net10.0-android
```

> **Gotcha:** after rebuilding the `.aar`, delete the `obj`/`bin` of both `src/SwiftDotNet` and
> `sample/SampleApp` before rebuilding — incremental builds reuse a stale binding. See
> [Android backend](backends/android.md).

### Windows (WinUI 3) — on a Windows machine

```powershell
dotnet run --project sample/SampleApp -f net10.0-windows10.0.19041.0
```

The sample is unpackaged + self-contained, so it runs with no prerequisites beyond the .NET SDK.

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
```

## Consuming the library in your own app

Reference the combined **`SwiftDotNet`** project/package. Then:

- **Apple targets** — also add `<Import Project="…/SwiftDotNet/SwiftDotNetBridge.targets" />` to your app's
  `.csproj`. This is **required**: `NativeReference` items don't flow transitively into the app's native link,
  so without it you'll get `Undefined symbols _swiftdotnet_*`.
- **GTK / Web / Skia** — plain project references (`SwiftDotNet.Gtk` / `SwiftDotNet.Web` / `SwiftDotNet.Skia`);
  no import needed.

The per-OS bootstrap lives in the library as reusable abstract hosts, so your platform entry point is a
one-liner that names your root view — see [Architecture → Centralized hosting](architecture.md#centralized-hosting--registration).
