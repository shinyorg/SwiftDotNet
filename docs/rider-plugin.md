# Rider Plugin & Dev Tools

Run any head from one configuration, watch the live view tree, and preview your views inside the IDE.

Three pieces, usable together or separately:

| Piece | Where it lives | What it is |
|---|---|---|
| **SwiftDotNet App** run configuration | [`tooling/rider`](../tooling/rider) | Discovers the heads in your solution, offers the ones this OS can build, launches them with hot reload and the debugger |
| **Patch Inspector** | [`tooling/rider`](../tooling/rider) + [`SwiftDotNet.DevTools`](../src/SwiftDotNet.DevTools) | The live node tree, on **every** backend, from one implementation |
| **Skia Preview** | [`tooling/rider`](../tooling/rider) + [`SwiftDotNet.Preview.Host`](../src/SwiftDotNet.Preview.Host) | Your views rendered and interactive in a tool window |

> **Status:** ✅ **Verified inside Rider, headlessly.** `swiftdotnet-doctor` runs the plugin's discovery,
> host-OS gate, device listing and launch planning inside a real Rider 2026.2 process and finds **12 of 12
> heads** on this machine, including iOS and Android with a live simulator and emulator. The commands it
> plans are the ones that deploy — both mobile apps were launched from them. What has *not* happened is a
> human clicking the Run button. See [Status](#status).

## What the plugin is for

Hot reload on this framework needs no IDE at all —
[`HotReload`](../src/SwiftDotNet/Core/HotReload.cs) is a plain `[MetadataUpdateHandler]`, so `dotnet
watch` drives every backend (see [Hot Reload](hot-reload.md)). The plugin is not what makes reload work.
It exists to remove the per-backend incantations in [Getting Started](getting-started.md) and to add the
two things the CLI cannot: a tree inspector and a preview.

## How to use it

### Build and install

```bash
cd tooling/rider
./gradlew buildPlugin        # → build/distributions/swiftdotnet-rider-0.1.0.zip
./gradlew runIde             # or: a sandbox Rider with the plugin loaded, opened on this repo
```

Install the zip through **Settings → Plugins → ⚙ → Install Plugin from Disk…**.

### Check what the plugin sees — `swiftdotnet-doctor`

```bash
cd tooling/rider
./gradlew runIde -Pdoctor
```

Runs headless, inside a real Rider, and prints every head, whether this OS can build it, which devices are
attached, and the exact command each would launch:

```
solution     : /Users/you/SwiftDotNet
host os      : MACOS
dotnet       : /usr/local/share/dotnet/dotnet
adb          : /Users/you/Library/Android/sdk/platform-tools/adb

heads (12)
  ✓ SampleApp (net10.0-ios)
      backend  : Apple (SwiftUI)
      devices  : iPhone Air (499AF569-C96C-4E5E-9361-CCEF93410629)
      run      : dotnet build …/SampleApp.csproj -t:Run -f net10.0-ios -c Debug \
                 -p:SwiftDotNetHotReload=true -p:_DeviceName=:v2:udid=499AF569-…
  ✓ SampleApp (net10.0-android)
      backend  : Android (Jetpack Compose)
      devices  : sdk gphone16k arm64 (emulator-5554)
      run      : dotnet build …/SampleApp.csproj -t:Run -f net10.0-android -c Debug \
                 -p:AdbTarget=-s emulator-5554
…
12 of 12 head(s) runnable on macos
```

This is the first thing to run when a head is missing from the dropdown, and it exits non-zero when
nothing is runnable, so it works as a CI check. The printed command is copy-pasteable — if the plugin can
plan it, you can run it by hand.

### Declare your heads

Head discovery reads one MSBuild property; it never guesses from package references:

```xml
<PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <SwiftDotNetPlatform>skia</SwiftDotNetPlatform>
</PropertyGroup>
```

Valid values: `apple`, `android`, `gtk`, `windows`, `web`, `skia`, `skia-maui`. For a head whose target
framework already answers the question — `net10.0-ios`, `net10.0-android` — you can leave it out and
[`SwiftDotNet.Platform.targets`](../msbuild/SwiftDotNet.Platform.targets) derives it. Declare it
explicitly when the TFM would lie: `sample/SampleApp.Skia.Mac` is `net10.0-macos` but draws with Skia,
not SwiftUI.

Check what the IDE sees:

```bash
dotnet msbuild sample/SampleApp.Gtk -getProperty:SwiftDotNetPlatform -getProperty:SwiftDotNetIsAppHead
```

### Expose the patch stream

One line in a host's startup, and nothing happens unless an IDE asks:

```csharp
SwiftApp.Run(root, DevTools.Wrap(bridge, "skia"), services);
```

`DevTools.Wrap` returns the bridge **unchanged** unless `SWIFTDOTNET_DEVTOOLS_PORT` is set, which the run
configuration sets when "Attach the inspector and preview" is on. No variable → no listener, no threads.

### Preview from the command line

The preview host is an ordinary console app, so it works without the IDE:

```bash
dotnet run --project src/SwiftDotNet.Preview.Host -- \
    --assembly sample/SharedUI/bin/Debug/net10.0/SharedUI.dll --port 51799
```

It prints its port, streams PNG frames, and takes `tap x y` / `scroll x y dy` / `text …` back.

## Per-backend behaviour

What the run configuration actually executes:

| Backend | Command | Notes |
|---|---|---|
| `skia`, `gtk`, `web` | `dotnet watch run --project … --non-interactive` | Plain `dotnet run` with watch off |
| `apple` — `net10.0-macos`, `-maccatalyst` | `dotnet watch run -f …` | Runs on this machine; no device needed |
| `apple` — `net10.0-ios`, `-tvos` | `dotnet build … -t:Run -p:_DeviceName=:v2:udid=…` | Adds `-p:SwiftDotNetHotReload=true` when watch is on |
| `android`, `skia-maui` (android) | `dotnet build … -t:Run -p:AdbTarget=-s …` | No interpreter needed |
| `windows` | `dotnet watch run -f net10.0-windows…` | Windows hosts only |

Deployment uses `dotnet build -t:Run` rather than hand-rolled `simctl install` + `simctl launch`: the SDK
targets already know about provisioning, the app-bundle path and fast deployment.

### The host-OS gate

Heads this machine cannot build are **shown greyed with the reason**, not hidden — the platform matrix is
the framework's whole pitch, and a Windows developer who cannot find the iOS head deserves to be told why.

| Host OS | Offered | Suppressed |
|---|---|---|
| **macOS** | iOS, tvOS, macOS, Mac Catalyst, Android, Skia, Web, GTK | WinUI |
| **Windows** | WinUI, Android, Skia, Web, GTK | Apple targets |
| **Linux** | GTK, Skia, Web, Android | Apple targets, WinUI |

GTK is offered everywhere on purpose: it is a plain `net10.0` executable that builds anywhere and runs
wherever GTK4 is installed (`brew install gtk4`).

## The dev-tools protocol

One frame is an ASCII header line then exactly `length` bytes:

```
SDN1 patch 512\n{"ops":[…]}
```

Length-prefixed, not newline-delimited, because the same socket carries patch JSON and PNG frames — and
PNG bytes are full of `0x0A`.

| Direction | Frames |
|---|---|
| App → IDE | `hello`, `patch` (`<seq>\n<json>`), `event` (`<id>\t<value>`), `frame` (PNG), `log` |
| IDE → app | `ping`, `input`, `resize`, `theme`, `reload` |

Both ends are tested against the byte layout rather than against each other —
[`DevToolsTests.cs`](../tests/SwiftDotNet.Tests/DevToolsTests.cs) and
[`DevToolsProtocolTest.kt`](../tooling/rider/src/test/kotlin/com/swiftdotnet/rider/DevToolsProtocolTest.kt).

## Gotchas

**The inspector is not Skia-specific, the preview is.** The inspector reconstructs the tree from the patch
stream, which every backend consumes, so it shows the same tree for a SwiftUI app as for a Skia one. The
preview *renders* with Skia — a control that is a native `UISwitch` on iOS is drawn by Skia there.

**The preview reloads the assembly; it does not hot reload.** It throws away a collectible
`AssemblyLoadContext` and loads the rebuilt assembly, so **every** edit applies, including the rude ones
.NET hot reload refuses — a new type, a changed signature, a new base class. The trade is that state
resets, which is what a preview does anyway. Verified: adding a whole new type and referencing it from
`Body` appeared in the preview after a rebuild.

**The preview needs a `net10.0` view assembly.** It loads `sample/SharedUI`-shaped projects. A head
targeting `net10.0-ios` cannot be loaded into a `net10.0` host.

**Custom renderers need `--init`.** Renderer registration is a startup step the *head* performs
(`SkiaSampleRenderers.RegisterAll`), and the preview loads a view assembly rather than a head. Without
`--init Namespace.Type.Method`, custom controls preview as the ⚠️ placeholder. See
[Custom Controls](custom-controls.md).

**Rider's version numbering.** The JetBrains repository publishes the RD-262 build as **2026.2**;
its "2026.1" is RD-261. `gradle.properties` pins `riderVersion=2026.2`, and `plugin.xml` accepts 262 only —
the plugin binds to Rider run/debug classes that are internal API, so the supported range is deliberately
narrow and re-verified per release rather than left open.

**Rider generates its own mobile configurations, and gets them wrong.** Opening this solution in Rider
produces `XamarinIOSProject` and `XamarinAndroidProject` configurations from its own heuristics —
including ones for `SwiftDotNet.DevTools` and `SharedUI`, which are **libraries**, not apps. They appear
because .NET Android builds apps as `OutputType=Library`, so "has an Android TFM" is the only signal
available without a declared contract. The plugin's `SwiftDotNetIsAppHead` is that contract, and its list
contains 12 real heads rather than 19 mixed ones. Both sets coexist; ours is the one with hot reload and
the dev-tools channel wired in.

**Deployed heads keep the process attached.** `dotnet build -t:Run` does not return — it streams the app's
output until the app exits, which is exactly what a run configuration wants (the Stop button kills it).
Do not mistake it for a hang.

**Dev tools bind loopback only** and are off unless the environment variable is set. The channel hands out
your entire view tree; it has no business being reachable off the machine.

## Status

| Piece | Status |
|---|---|
| `SwiftDotNetPlatform` / `SwiftDotNetIsAppHead` MSBuild contract | ✅ **Verified** — correct for all 9 sample heads, including the Android case where `OutputType` is `Library` |
| Dev-tools protocol + patch tap | ✅ **Verified** — 20 tests, plus a live capture from a running app |
| Preview host | ✅ **Verified** on macOS — rendered `sample/SharedUI`, a tap navigated to another page, and a rude edit reloaded |
| Head discovery, OS gate, device listing, launch planning | ✅ **Verified inside Rider** — `swiftdotnet-doctor` found 12 of 12 heads on macOS with a live simulator and emulator |
| **iOS launch** | ✅ **Verified** — the doctor's exact command deployed and started the app on the iOS 26.5 simulator |
| **Android launch** | ✅ **Verified** — the doctor's exact command deployed and started the app on `emulator-5554` (Compose UI on screen) |
| Kotlin plugin (run config, inspector, preview panel) | 🧩 **Loads in Rider**, 54 tests pass — but the Run *button* has not been pressed; nothing has exercised `getStateAsync` |
| Debugging | 🧩 **Scaffolded** — the configuration extends Rider's `RiderAsyncRunConfiguration` with its `DotNetExeExecutorFactory` and marks itself `IRiderDebuggable`, so Rider's own runners should handle it. Unverified in the IDE. |

The unfinished work and the reasoning behind each decision live in
[`plans/rider-plugin-plan.md`](../plans/rider-plugin-plan.md).

## See also

- [Hot Reload](hot-reload.md) — the mechanism the run configuration turns on, and the iOS recipe
- [Getting Started](getting-started.md) — the CLI commands the plugin wraps
- [Architecture](architecture.md) — the patch protocol the inspector renders
- [Skia backend](backends/skia.md) — `SkiaImageHost`, the preview engine
- [Custom Controls](custom-controls.md) — the renderer registry the preview needs `--init` for
