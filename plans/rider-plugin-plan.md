# Plan: A SwiftDotNet plugin for JetBrains Rider

**Status — 2026-07-31: Phases 1–4 built and verified inside Rider (headlessly); iOS and Android both
deploy from the plugin's own planned commands.**
The user-facing reference is [Rider Plugin & Dev Tools](../docs/rider-plugin.md) and
[Hot Reload](../docs/hot-reload.md); this doc records the decisions, the measurements, and what is left.
Findings in §2 were observed against a real Rider install, not assumed.
**Scope:** An IDE plugin that discovers the app heads in a solution, offers the ones the **host OS** can
actually run, launches them with the debugger and hot reload attached, and adds two SwiftDotNet-specific
dev affordances (a live patch inspector and an interactive Skia preview).

---

## Why

The framework's dev loop is currently a set of memorised `dotnet` incantations that differ per backend —
`dotnet watch run --project sample/SampleApp.Skia.Silk`, `dotnet build -f net10.0-ios -r
iossimulator-arm64` then `simctl install`/`launch`, `-p:SwiftDotNetHotReload=true` for the interpreter,
`--device <UDID>` when two simulators are visible, and a `cd` into the project folder or you get `MT0069`.
See [Getting Started](../docs/getting-started.md) and the gotchas in [Hot Reload](../docs/hot-reload.md).

Every one of those is knowable from the solution plus the host OS. That is what a plugin is for.

The second reason is the one that isn't just convenience: SwiftDotNet has **no preview**. SwiftUI's headline
authoring feature is seeing the view as you type it, and we ship a self-drawing backend
([`SkiaImageHost`](../src/SwiftDotNet.Skia/SkiaHost.cs)) that already renders a tree to a PNG and accepts
taps, scrolls, drags, typing and animation ticks. The gap between that and an in-IDE interactive preview is
a transport, not a renderer.

## 1. What already works with no plugin at all

This matters because it bounds the plugin's job. Rider runs, debugs and hot-reloads these **today**:

| Head | Why it already works |
|---|---|
| [`SampleApp.Skia.Silk`](../sample/SampleApp.Skia.Silk), `SampleApp.Skia`, `SampleApp.Skia.Renderers` | Plain `net10.0` `Exe` — an ordinary .NET run configuration |
| [`SampleApp.Gtk`](../sample/SampleApp.Gtk) | Same; plain `net10.0` `Exe` |
| [`SampleApp.Skia.Mac`](../sample/SampleApp.Skia.Mac) | `net10.0-macos`, `osx-arm64` — a normal .NET run configuration on a Mac |
| [`SampleApp.Web`](../sample/SampleApp.Web) | Blazor WASM; Rider runs and debugs it, plus browser refresh |

And hot reload on all four comes free, because
[`HotReload.cs`](../src/SwiftDotNet/Core/HotReload.cs) is a plain `[MetadataUpdateHandler]` — *any* host
that applies deltas (`dotnet watch`, Rider's runner, VS) ends up calling `SwiftApp.Invalidate()`.

**So the plugin is not what makes hot reload work.** It is convenience for Tier 0, and capability only for
mobile and for the two dev affordances.

## 2. Findings from the Rider install (RD-262)

Everything here was checked in the shipped application bundle, with the paths recorded so it can be
re-checked when Rider moves.

### 2.1 The exact plugin shape we need is bundled, and is an ordinary plugin

`plugins/rider-plugins-dotnetwatch/lib/rider-plugins-dotnetwatch.jar` — JetBrains' own ".NET Watch Run
Configuration". Its whole `META-INF/plugin.xml` is:

```xml
<depends>com.intellij.modules.rider</depends>
<extensions defaultExtensionNs="com.intellij">
  <configurationType implementation="…dotnetwatch.run.DotNetWatchRunConfigurationType" />
  <programRunner   implementation="…dotnetwatch.run.DotNetWatchDebugRunner" />
</extensions>
```

with `DotNetWatchRunConfiguration`, `DotNetWatchRunState`, `DotNetWatchDebugRunner` and
`DotNetWatchDebugProcess` inside. Two conclusions:

- A third-party plugin **can** register a run configuration that combines `dotnet watch` with Rider's .NET
  debugger. This is not a private-feature area; it is a `configurationType` + `programRunner`.
- Watch **and** debugging coexist — `DotNetWatchDebugProcess` exists — so "hot reload while debugging" is
  not something we have to invent.

### 2.2 JetBrains' hot-reload agent is toolkit-pluggable, and we don't need it

`lib/ReSharperHost/` ships `JetBrains.HotReloadAgent.Core.dll` plus per-toolkit providers:
`JetBrains.HotReloadAgent.Maui.dll` (`MauiProvider`), `.Wpf.dll`, `.XamarinForms.dll`, and
`JetBrains.Rider.{Browser,Xaml}HotReload.JetMetadata.sstg`.

Those providers exist because MAUI/WPF need an **IDE-side nudge** after a delta lands — re-render, re-parse
XAML. SwiftDotNet's `MetadataUpdateHandler` does that itself. There is no published extension point to
register a fourth provider, and **we do not want one**: a JetBrains provider would duplicate
`SwiftApp.Invalidate()` and could double-render.

### 2.3 Rider *does* ship iOS run/debug machinery — an earlier version of this document said otherwise

**Correction.** The first pass at this looked for a mobile *plugin directory* under `Contents/plugins/`,
found none, and concluded there was no iOS support to build on. That was wrong: it is in
`lib/intellij.rider.jar`, not in a plugin folder.

```
com/jetbrains/rider/run/configurations/multiPlatform/ios/IOSConfigurationType.class
                                                    …/IOSConfigurationFactory.class
                                                    …/IOSExecutorFactory.class
                                                    …/ConnectToRemoteMacAction.class
com/jetbrains/rider/run/multiPlatform/ios/IOSConstants.class
```

`IOSConstants` is the interesting one — its constant pool holds `__XAMARIN_DEBUG_HOSTS__`,
`__XAMARIN_DEBUG_PORT__`, `XAMARIN_DEBUG_ADDRESS_ENV` and `XAMARIN_DEBUG_PORT_ENV`. That is the same
channel `MonoTouchDebugConfiguration.txt` names inside a built `.app`, so Rider already implements the
iOS side of it.

`lib/ReSharperHost/Mono.Debugger.Soft.dll` is present too (Unity uses it).

The lesson worth keeping: grep the *jars*, not the directory listing.

### 2.4 The seam that gives Run, Debug and hot reload for free

The single most useful finding. Rider's own executable configuration is built from two public-in-practice
pieces:

```
RiderAsyncRunConfiguration(name, project, factory, editorFactory, AsyncExecutorFactory)
DotNetExeExecutorFactory(DotNetExeConfigurationParameters)   // implements AsyncExecutorFactory
IRiderDebuggable                                             // marker, one default method
```

A configuration that extends `RiderAsyncRunConfiguration`, hands it a `DotNetExeExecutorFactory`, and
implements `IRiderDebuggable` is a configuration Rider's own `DotNetProgramRunner` and debug runner will
execute — and `DotNetHotReloadConfigurationExecutorExtension` is already wired into that path. **The
plugin therefore contains no debugger, no launcher, and no reload mechanism.**

Two traps found while wiring it, both by the compiler:

- Do not *name* `DotNetExeConfiguration` in Kotlin. It implements interfaces from a Rider module the
  plugin has no dependency on, and Kotlin resolves every supertype of a type it is asked to name. Reach
  the parameters through `RiderConfigurationParametersAware<*>` instead.
- Do not call the sixteen-argument `DotNetExeConfigurationParameters` constructor. Borrow a defaulted
  instance from `DotNetExeConfigurationType.factory.createTemplateConfiguration(project)` and set the
  four fields that matter. Depending on Rider's *defaults* survives an upgrade; depending on its
  argument order does not.

### 2.5 The API we'd lean on is internal

The bundled watch plugin pins `since-build` and `until-build` to a **single exact build**
(`262.8665.385`). JetBrains can do that because they rebuild it every release. A marketplace plugin using
the same run/debug classes carries a per-release breakage risk. This is the standing tax on the whole idea
and it should be priced in, not discovered later.

## 3. Decisions

### Decision 1 — frontend-only, or frontend + a ReSharper backend half?

| Option | Verdict |
|---|---|
| **Kotlin frontend only** | **Chosen for Phases 1–3.** Run configurations, tool windows, OS gating and process orchestration are all frontend concerns. Halves the build (no rdgen protocol, no backend release train). |
| Frontend + backend (`.dll` in the R# host, rd protocol) | Deferred to Phase 4 only. The one thing that genuinely needs it is reusing `Mono.Debugger.Soft` in-process. Nothing before that does. |

### Decision 2 — how the plugin triggers a reload

| Option | Verdict |
|---|---|
| **Nothing — let the runtime do it** | **Chosen.** `[MetadataUpdateHandler]` → `SwiftApp.Invalidate()` → full `replace` patch. Works on every backend, already covered by `HotReloadTests`. |
| Ship a `JetBrains.HotReloadAgent.SwiftDotNet` provider | **Rejected.** No extension point (§2.2), and it would duplicate the invalidate we already do. |
| Host Roslyn's `WatchHotReloadService` ourselves | **Rejected.** Re-implements `dotnet watch` to gain nothing; Rider and the SDK both already have a delta pipeline. |

### Decision 3 — one run configuration type, or generated per-head configurations?

| Option | Verdict |
|---|---|
| **A `SwiftDotNet App` `configurationType`** with a head picker + watch toggle | **Chosen.** Mirrors §2.1 exactly. One config the user retargets, rather than N configs to keep in sync. |
| Generate `.run/*.xml` files into the repo | **Rejected.** Checked-in, per-developer-OS churn; can't gate dynamically. |
| A pure "run head" action with no configuration | **Rejected.** Loses the Debug button, env vars, and the Services tool window. |

### Decision 4 — how head detection works

Today: infer from the project's backend reference (`SwiftDotNet.Skia` / `.Gtk` / `.Web` / `.Skia.Maui`) plus
its TFM. That is heuristic and will misfire on an app that references two.

The clean version is already designed elsewhere: [MSBuild SDK plan](msbuild-sdk-plan.md) §1 Option A adds a
declared **`SwiftDotNetPlatform`** property. If that lands, head detection becomes *reading one MSBuild
property* — no inference. **These two plans should be sequenced together**; the SDK is the plugin's
metadata source, and it is cheap and low-risk on its own.

### Decision 5 — where the OS gate comes from

`SystemInfo.isMac/isWindows/isLinux` in the frontend, gating a static matrix that mirrors what
[`SampleApp.csproj`](../sample/SampleApp/SampleApp.csproj) already encodes with
`$([MSBuild]::IsOSPlatform(...))`:

| Host OS | Offer | Suppress |
|---|---|---|
| **macOS** | iOS, tvOS, macOS, Mac Catalyst, Android, all Skia heads, Web, (GTK if `gtk4` is installed) | WinUI |
| **Windows** | WinUI, Android, Skia (Silk/console), Web | Apple targets, GTK |
| **Linux** | GTK, Skia (Silk/console), Web, Android | Apple targets, WinUI |

Suppressed heads should be **shown greyed with the reason** ("needs a Mac + Xcode"), not hidden — the
matrix is a teaching surface for a framework whose whole pitch is every platform.

### Decision 6 — the preview transport

`SkiaImageHost` already exposes `RenderPng(w,h)`, `Tap`, `Scroll`, `Type`, `Backspace`, `LongPress`,
`Swipe`, `Drag`, `Magnify` and `Advance(dt)`. It is an interactive headless app that speaks PNG.

| Option | Verdict |
|---|---|
| **Out-of-process host + a socket; frames to a Swing panel; input events back** | **Chosen.** No JCEF, no native embedding; the previewed app crashing doesn't take the IDE with it; the protocol is trivially inspectable. |
| Embed the real Silk window into a tool window | **Rejected.** Native window reparenting, per-OS, fragile. |
| Render in-IDE by loading the user's assemblies into the plugin | **Rejected.** Version-skewed .NET-in-JVM nightmare, and no isolation. |

### Decision 7 — how the patch inspector taps the stream

`SwiftApp.Render()` ends at `_bridge.Render(patch.ToJson())` against the
[`IBridge`](../src/SwiftDotNet/Core/IBridge.cs) two-method interface. So the tap is a **decorating
`IBridge`** that forwards the JSON to a socket and delegates on — **zero changes to Core**, works for
*every* backend at once because they all consume the same patch stream, and it's an opt-in a dev build
wraps around its real bridge.

## 4. What was built

### Phase 1 — run configurations ✅

- [`SwiftDotNetConfigurationType`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/run/SwiftDotNetConfigurationType.kt)
  + factory + [editor](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/run/SwiftDotNetConfigurationEditor.kt):
  head picker, watch toggle, dev-tools toggle, device picker, configuration, MSBuild properties.
- [`SwiftDotNetRunConfiguration`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/run/SwiftDotNetRunConfiguration.kt)
  per Decision 2.4's seam — Run, Debug and hot reload come from Rider.
- [`HeadDiscovery`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/msbuild/HeadDiscovery.kt) reads
  `SwiftDotNetPlatform` / `SwiftDotNetIsAppHead` via `dotnet msbuild -getProperty:`, per TFM.
- [`OsGate`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/model/Heads.kt) — unsupported heads
  greyed with the reason, never hidden.
- [`LaunchPlanner`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/run/LaunchPlan.kt) — the
  per-backend command line, and the only piece with real logic, so it is pure Kotlin and unit-tested.

### Phase 2 — the two affordances ✅

- [`SwiftDotNet.DevTools`](../src/SwiftDotNet.DevTools) — the length-prefixed frame protocol, a loopback
  server, and `PatchTapBridge`, an `IBridge` decorator. **Zero changes to Core**, exactly as Decision 7
  predicted.
- [`InspectorToolWindow`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/inspector/InspectorToolWindow.kt)
  + [`PatchModel`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/devtools/PatchModel.kt) — applies
  `replace` / `updateProps` / `setChildren` the same way a backend does.
- [`SwiftDotNet.Preview.Host`](../src/SwiftDotNet.Preview.Host) — collectible `AssemblyLoadContext` +
  `SkiaImageHost`, streaming PNG frames.

### Phase 3 — mobile launch ✅

`dotnet build -t:Run` with `-p:_DeviceName=:v2:udid=…` (Apple) or `-p:AdbTarget=-s …` (Android), plus
[`DeviceLister`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/mobile/DeviceLister.kt) over
`simctl` / `adb`.

**Design change from the plan:** deployment does *not* orchestrate `simctl install` + `simctl launch` by
hand. `dotnet build -t:Run` is the SDK's own entry point and already knows about provisioning, the
app-bundle path, fast deployment and the device selector. The original plan underestimated how much of
Phase 3 was already written by Microsoft.

### Phase 4 — the iOS spike ✅ — see §5

### Verification — `swiftdotnet-doctor`

Pressing Run is the one thing an agent cannot do, so the plugin grew a headless
[`ApplicationStarter`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/doctor/SwiftDotNetDoctor.kt)
that runs discovery, the OS gate, device listing and launch planning **inside a real Rider process** and
prints the result. `gradle runIde -Pdoctor` on this machine reports:

```
host os : MACOS   dotnet : /usr/local/share/dotnet/dotnet
adb     : /Users/…/Library/Android/sdk/platform-tools/adb
✓ SampleApp (net10.0-ios)      devices: iPhone Air (499AF569-…)
✓ SampleApp (net10.0-android)  devices: sdk gphone16k arm64 (emulator-5554)
…
12 of 12 head(s) runnable on macos
```

Both mobile commands it printed were then run verbatim and both apps started —
`[lifecycle] created SampleRootView` on the simulator, `MainActivity` top-resumed on the emulator.

It earns its place beyond verification: "why isn't my iOS head in the list?" is now answerable without a
screenshot, and it exits non-zero when nothing is runnable, so it works as a CI check.

### Two things only running it on a real machine could have found

- **`adb` is usually not on `PATH`.** The device picker came up empty on a machine with a running
  emulator: Android Studio installs the SDK to `~/Library/Android/sdk` and nothing exports
  `ANDROID_HOME`. An IDE does not inherit a login shell's environment either, so a `.zshrc` that works in
  a terminal proves nothing. `DeviceLister` now probes the conventional locations.
- **Rider's own mobile configurations are wrong for this solution.** Opening it generates
  `XamarinIOSProject` / `XamarinAndroidProject` entries for `SwiftDotNet.DevTools` and `SharedUI` —
  libraries, not apps — because .NET Android builds apps as `OutputType=Library` and "has an Android TFM"
  is the only signal available without a declared contract. 19 mixed entries against the plugin's 12 real
  heads. This is the clearest argument for `SwiftDotNetIsAppHead` that exists.

## 5. The iOS spike — solved, and the documented cause was wrong

The spike was scheduled to answer one question: can the IDE stand up the `127.0.0.1:10000` channel that
`docs/hot-reload.md` blamed for iOS hot reload aborting? The answer turned out to be *that was never the
problem*.

**What was measured** (iOS 26.5 simulator, Xcode 26.6, .NET SDK 10.0.302):

| Experiment | Result |
|---|---|
| Interpreter build, launched by `simctl`, **listener on 10000** | App **runs**. Connection accepted; runtime sends nothing and waits. |
| Interpreter build, launched by `simctl`, **nothing listening** | `Socket error … Connection refused` logged — and the app **runs anyway**. |
| Same build launched by **`dotnet watch`** | **Aborts**: `mono_runtime_run_startup_hooks` → `xamarin_register_assembly` → `abort` |
| `simctl` launch with `DOTNET_STARTUP_HOOKS` set to the SDK's delta applier | **Identical abort** — cause isolated |
| Delta applier **copied into** the `.app`, hook pointed at it | Still aborts |
| Delta applier added as a **`<Reference>`** so the registrar knows it | **App starts.** |
| `dotnet watch` end-to-end with that reference | 🔥 **`C# and Razor changes applied in 407ms`**, live on the simulator |

**Root cause.** `dotnet watch` delivers edits by injecting `DOTNET_STARTUP_HOOKS` pointing at
`Microsoft.Extensions.DotNetDeltaApplier.dll` in the SDK. The Xamarin registrar only loads assemblies it
knew about at build time, so a hook assembly outside the bundle aborts startup. The socket error is a
**red herring** — it appears in runs that hot reload perfectly.

**The fix** is four lines in the app's `.csproj`, gated behind the existing opt-in and portable via
`$(NetCoreRoot)` / `$(NETCoreSdkVersion)` — see
[`SampleApp.csproj`](../sample/SampleApp/SampleApp.csproj) and [Hot Reload](../docs/hot-reload.md).
Verified that a build *without* `-p:SwiftDotNetHotReload=true` does not bundle the applier.

**What this changes.** Phase 4 was the plan's expensive, open-ended item — a Mono soft-debugger handshake
against an undocumented protocol. It is now a `<Reference>`. Nothing needs the ReSharper backend half
(Decision 1), and the plugin needs no iOS-specific code at all: `LaunchPlanner` already sets the property.

**Still unproven:** the same recipe on tvOS (shares the opt-in, not run) and on a physical device
(provisioning is a different problem).

## 6. Risks, revised

| Risk | State |
|---|---|
| **Internal Rider API churn** | Real, unchanged. Mitigated by keeping the surface tiny — one configuration type, one executor factory, one marker interface — and by pinning `until-build`. Note the numbering trap: the JetBrains repository publishes RD-262 as **2026.2**, and its "2026.1" is RD-261. Building against the wrong one produces a plugin the IDE silently refuses to load ("requires IDE build 262 or newer"). |
| Phase 4 open-ended | **Resolved.** See §5. |
| Head detection heuristic | **Resolved.** `SwiftDotNetPlatform` + `SwiftDotNetIsAppHead` are declared and verified across all nine sample heads. |
| Preview diverges from real backends | Unchanged, and honestly labelled. It is a *Skia* preview. |
| Plugin becomes the only documented path | Unchanged. Every command the plugin runs is a `dotnet` command a developer can type, and `docs/getting-started.md` stays CLI-first. |
| **Not yet run inside Rider** | The new one. The plugin loads (`Loaded custom plugins: SwiftDotNet (0.1.0)`) and the solution opens with no errors from its classes, but nobody has clicked Run. |

## 7. Open questions

1. **Does the Run *button* work?** Partly answered. Discovery, the gate, device listing and planning are
   verified inside Rider by the doctor, and the planned commands deploy on both mobile platforms. What is
   still unexercised is the last hop: `getStateAsync` → `DotNetExeExecutorFactory` → Rider's runner. A
   platform test for it was attempted and abandoned — `BasePlatformTestCase` boots the ReSharper backend,
   which fails in the sandbox with `Invalid path to dotnet executable`. Pressing Run is a one-minute
   manual check; two ready-made configurations are in [`.run/`](../.run) to make it a single click.
2. **Does `IRiderDebuggable` satisfy `DotNetProgramRunner.canRun`?** Assumed from its signature, not
   observed. If not, the fallback is a `programRunner` of our own.
3. **Where should the iOS delta-applier reference live long-term?** It is in
   [`SampleApp.csproj`](../sample/SampleApp/SampleApp.csproj) because that is where it was proven. Every
   consumer needs it, so it belongs in [`SwiftDotNet.Sdk`](msbuild-sdk-plan.md). It was *not* moved into
   `msbuild/SwiftDotNet.Platform.targets` yet because `UseInterpreter` has to be set before the Apple SDK
   targets read it, and a `Directory.Build.targets` import may be too late — that ordering needs testing
   rather than assuming.
4. **Marketplace or internal?** Unchanged.
5. **Is the preview worth more than the mobile work?** Answered by events: both got built, and the mobile
   work turned out to be far cheaper than estimated.

## 8. Where things landed

| Piece | Where | Note |
|---|---|---|
| Patch tap + protocol | [`src/SwiftDotNet.DevTools`](../src/SwiftDotNet.DevTools) | Separate project; Core stays dependency-free and trim/AOT-safe |
| Preview host | [`src/SwiftDotNet.Preview.Host`](../src/SwiftDotNet.Preview.Host) | Plain `net10.0` console app; usable without any IDE |
| Run configs, tool windows, OS gate, device listing | [`tooling/rider`](../tooling/rider) | Kotlin; 45 tests |
| `SwiftDotNetPlatform` contract | [`msbuild/`](../msbuild) + [`sample/Directory.Build.*`](../sample) | To be absorbed by [`SwiftDotNet.Sdk`](msbuild-sdk-plan.md) |
| iOS hot-reload fix | [`sample/SampleApp.csproj`](../sample/SampleApp/SampleApp.csproj) | See open question 3 |

## See also

- [Hot Reload](../docs/hot-reload.md) — the mechanism, the per-backend status table, and the iOS blocker
- [Getting Started](../docs/getting-started.md) — the CLI commands the plugin would be wrapping
- [MSBuild SDK / custom TFMs](msbuild-sdk-plan.md) — the metadata source for head detection
- [Architecture](../docs/architecture.md) — the patch protocol the inspector would render
- [Skia backend](../docs/backends/skia.md) — `SkiaImageHost`, the preview engine

