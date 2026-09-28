# Plan: Apple Watch (`SwiftDotNet.Watch`)

**Status:** Phases 1–5 built; **verified on the watchOS 27.0 simulator** (Apple Watch Series 12, Xcode 26.6 /
watchOS SDK 26.5). Device, App Store and paired-phone paths unverified · **Date:** 2026-09-28
**Docs:** [Apple Watch backend](../docs/backends/watchos.md)

> **Provenance.** Adapted from the `Shiny.Wearables` prototype (`~/Desktop/dev/Prototypes/MauiNext/Shiny.Wearables`).
> What came across is its **infrastructure** — the NativeAOT + Mach-O retarget build, the inverted C ABI, the
> main-thread pump, the native services and live mode. What deliberately did **not** come across is its view DSL
> (`View`/`VStack`/`Modifiers`/`Primitives`) and its SwiftUI renderer: SwiftDotNet already has both, and a watch
> app written against a second DSL would be a second framework.

## Context

There is no watchOS runtime in .NET — Xamarin.watchOS was never ported, and neither Mono nor NativeAOT ships a
watchOS pack. The prototype proved a way around that without forking the runtime:

1. NativeAOT-compile the app with the **iOS** runtime packs (`iossimulator-arm64` / `ios-arm64`).
2. The objects are arm64 machine code against `libSystem` — exactly what watchOS uses. Only the Mach-O
   `LC_BUILD_VERSION` platform stamp differs, and `ld` refuses to mix platforms, so
   [`retarget-macho.py`](../src/SwiftDotNet.Watch/sdk/tools/retarget-macho.py) rewrites that stamp in place.
3. Link the retargeted runtime + app into a SwiftUI watch app with `swiftc`, generate `Info.plist`, sign.

SwiftDotNet's Apple heads already have the other half: a SwiftUI interpreter
([`Bridge.swift`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift)) fed by `TreeDiffer`
patches. The job was to put the two together.

## Decisions

### D1 — Share `Bridge.swift`; don't fork a watch renderer

The shared interpreter compiles for watchOS with **eight** `#if os(watchOS)` guards, the same pattern tvOS uses:

| Unavailable on watchOS | Fallback |
|---|---|
| `TextEditor` | `TextField` (tvOS fallback reused) |
| `ColorPicker` | label + swatch (tvOS fallback reused) |
| `DisclosureGroup` | header button + conditional content (tvOS fallback reused) |
| `Menu` | items shown inline under the label |
| `.textFieldStyle(.roundedBorder)` | omitted |
| `.indexViewStyle(.page(backgroundDisplayMode:))` | omitted; `.tabViewStyle(.page)` kept |
| `MagnificationGesture` (`.OnMagnify`) | no-op |
| `UIView` / hosting controllers (`MauiView`, `swiftdotnet_make_host_controller`) | placeholder text / compiled out |

**Rejected:** porting Shiny.Wearables' `WearablesRenderer.swift`. It interprets a different tree format (full tree
per render, positional handler ids), so it would have meant a second wire protocol and a second set of every
control's semantics. Sharing means the watch gets every node the phone gets, on day one, and every future fix.

The iOS/macOS/tvOS xcframework still builds from the same file (re-run of `build-xcframework.sh`, all five slices).

### D2 — Invert the host; keep the renderer's C ABI untouched

On iOS, .NET owns the process and P/Invokes the framework. On the watch, the **Swift app owns the process** and
calls into .NET. The renderer does not notice: patches still arrive at `swiftdotnet_render`, and UI events still
leave through the callback installed with `swiftdotnet_set_event_callback`. The new surface is a separate header,
[`swiftdotnet_watch.h`](../src/SwiftDotNet.Watch/sdk/swift/swiftdotnet_watch.h): `register_callbacks` (render,
request_drain, log, call), `bootstrap` (generated in the app assembly), `event`, `native_event`, `phase`, `drain`.

### D3 — The render loop is `SwiftApp`, unchanged

The prototype re-rendered and re-serialized the whole tree on every event. Here the managed host
([`WatchHost`](../src/SwiftDotNet.Watch/WatchHost.cs)) just runs `SwiftApp.Run` against a
[`WatchBridge : IBridge`](../src/SwiftDotNet.Watch/WatchHost.cs), so the watch gets `TreeDiffer` patches,
render coalescing and hot reload for free. What makes coalescing work is a
[`MainThreadSynchronizationContext`](../src/SwiftDotNet.Watch/MainThreadSynchronizationContext.cs) installed
**before** `SwiftApp.Run` captures `SynchronizationContext.Current`: a posted render asks Swift for
`swiftdotnet_watch_drain` on the main queue. A UI event drains inline after the handler, so a controlled control
never shows a frame of stale state.

### D4 — Entry point: `SwiftProgram.CreateSwiftApp()`, generated export

The SDK generates `swiftdotnet_watch_bootstrap` into the **app** assembly (only the app knows how to build its
`SwiftDotNetApp`). It calls `$(SwiftDotNetWatchAppFactory)` — default `SwiftProgram.CreateSwiftApp`, the same
composition root every other head uses — resolved inside the project's `RootNamespace`. **Rejected:** the
prototype's `[assembly: WatchApplication<T>]` + reflection, which is a trimming liability and a second
convention.

### D5 — `DigitalCrownRotation` is a Core modifier with a fixed wire contract

`.DigitalCrownRotation(value, onChange, from, through, by)` serializes
`{"type":"digitalCrown","value","from","through","by","event"}` and reports the new value as an invariant-culture
double string, registered like `OnMagnify`. It is a no-op everywhere but watchOS — and Wear OS, whose rotary
input maps to the same contract. Swift's `ModifierData.value` was a strict `String?`; it now decodes leniently
(`@LenientString`) because this is the first modifier to ship `value` as a number, and a strict decode fails the
**whole patch**. Every C# backend and the Kotlin shim already read `value` with `as string` / `as? String`, so they
ignore it safely.

### D6 — Services keep the prototype's shape, under `SwiftDotNet.Watch`

`Haptics`, `WatchDevice`, `Preferences`, `WatchHttp` (URLSession `HttpMessageHandler`) and `PhoneConnection`
(WatchConnectivity) all ride one synchronous `WatchHost.CallNative(op, argsJson)` channel plus native events.
Namespaced (`SwiftDotNet.Watch`) because `Preferences`/`Haptics` are names apps and MAUI already use. The typed
message router from the prototype's `Shared/` folder was **not** ported (it was paired with a phone-side package
that doesn't exist here).

### D7 — Live mode is the Debug default

`WatchTarget=live` for Debug: the app runs on the Mac under CoreCLR (debugger, Hot Reload), a shell on the watch
simulator renders it over loopback TCP. The protocol carries exactly what the C ABI carries (`render` {patch},
`event`, `nativeEvent`, `phase`, `call`/`result`), so the renderer can't tell the difference. A reconnecting
shell gets a full tree via `SwiftApp.Invalidate()`. **Not ported:** the Mac preview window and the lldb
formatters / `wearables` CLI — see Open questions.

## Phases

| Phase | What | Status |
|---|---|---|
| 1 | `Bridge.swift` compiles for watchOS; xcframework unaffected | ✅ done |
| 2 | Managed host + C ABI + services | ✅ done — 12 tests drive it through a fake native platform |
| 3 | `DigitalCrownRotation` in Core | ✅ done — wire-contract tests; crown turned on the simulator |
| 4 | MSBuild SDK (NativeAOT → retarget → swiftc → bundle → sign → `dotnet run`) + `sample/SampleApp.Watch` | ✅ done — 0 warnings, runs on the watchOS 27 simulator |
| 5 | Live mode | ✅ built — shell renders on the simulator; the socket protocol is covered by an end-to-end test with a fake shell; **interaction through the real shell not driven** (see below) |
| 6 | Device + TestFlight + App Store review | ❌ not started — needs an arm64 watch (Series 9 / Ultra 2 or later on watchOS 26+) and a paid account |
| 7 | Complications / Smart Stack widgets | ❌ not started — would extend [`SwiftDotNet.Widgets`](widgets-plan.md), not this package |
| 8 | Companion embedding (watch app inside the iOS head) | ❌ not started — `PhoneConnection` is written but untested |

## What was verified, and how

- **NativeAOT build (simulator):** `dotnet build sample/SampleApp.Watch -c Release` — 0 warnings.
- **On the watchOS 27.0 simulator** (Series 12 42mm and 46mm), driven by clicking the Simulator window and
  scroll-wheel events for the crown: navigation push/pop, button → C# → `State` → patch → SwiftUI, the Digital
  Crown (40 → 45), `Preferences` surviving a relaunch (including a native call from the root's `OnCreated`),
  and device info. Screenshots in [`docs/screenshots/`](../docs/screenshots).
- **Headless smoke** (`SIMCTL_CHILD_SWIFTDOTNET_WATCH_SMOKE=1`, read back from `log show`): device info, prefs, and
  an HTTPS GET over URLSession returning 200, with the `await` resuming on the watch's main thread.
- **Live mode:** `dotnet run` in Debug installs the shell, it connects, and the tree renders on the simulator.
  The GUI tap through the live shell was *not* driven (the desktop was in use); instead
  `LiveMode_RendersDispatchesProxiesCallsAndResendsOnReconnect` drives render, tap, a proxied native call and
  reconnect over a real socket.
- **Full test suite:** 405/405.

## Open questions

- **App Store acceptance** of restamped iOS objects. Unknown until someone submits one.
- **`arm64_32`.** Pre-watchOS-26 hardware uses an ILP32 ABI NativeAOT can't target. Wait for official support, or
  accept the watchOS 26+ floor for devices (current choice; the SDK enforces it).
- **Mac preview window** (prototype had one): cheap to add — `Bridge.swift` already compiles for macOS — but the
  host's services are watch-only today.
- **lldb formatters / CLI.** The prototype's `wearables_lldb.py` and `wearables` CLI would port verbatim; left out
  to keep the first cut small.
- **Packaging.** The targets compile `Bridge.swift` from the repo (`SwiftDotNetBridgeSwift`); a NuGet package
  needs to ship the source (or a prebuilt watchOS object) alongside `sdk/`.

## Risks

- **Unsupported toolchain.** The retarget works because the ABI is identical, but the runtime's authors never
  tested it on watchOS.
- **`OperatingSystem.IsWatchOS()` is false** and `RuntimeInformation` reports iOS; `WatchDevice.Current` is the
  source of truth.
- **Networking:** only `WatchHttp` works on hardware (BSD sockets are restricted); raw sockets/WebSockets aren't
  bridged. Globalization is invariant (no ICU).
