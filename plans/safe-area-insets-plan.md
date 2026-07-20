# Safe area insets (iOS & Android)

**Status — 2026-07-20: implemented, not device-verified.** The user-facing reference is
[Safe area](../docs/modifiers-gestures-animation.md#safe-area-ios--android-only); this doc records the
decisions and what's still open.

## Why

Nothing in the framework knew about safe areas. On the two backends that render into a real device
window — SwiftUI on iOS, Compose on Android — content ran under the status bar, the cutout, the home
indicator, and the soft keyboard. Compose is edge-to-edge by default on Android 15+, so it was actively
broken there.

## Decision 1 — how to restrict the API to iOS/Android

Safe area is a device-window concept and must not exist on GTK/WinUI/Web/Skia-desktop. Three options:

| Option | Verdict |
|---|---|
| Hard TFM gate (`Platforms/Mobile/**`, compiled only for `net10.0-ios`/`-android`) | **Rejected.** The shared UI project is plain `net10.0` — that's the framework's whole premise — so the API would be unreachable from the code that needs it, and untestable from the `net10.0` test project. |
| Separate `SwiftDotNet.Mobile` package | **Rejected.** Same multi-targeting cost for consumers, plus a package to ship. |
| **`[SupportedOSPlatform]` annotations in Core** | **Chosen.** API compiles everywhere; calling it from a neutral TFM raises **CA1416** unless guarded by `SafeArea.IsSupported`. Testable, and write-once shared UI still works. |

Two details the first attempt got wrong, both now verified by building a probe against `sample/SharedUI`:

- `SafeArea.IsSupported` needs **`[SupportedOSPlatformGuard]`**, or the analyzer doesn't trust the guard
  and warns anyway. A plain `bool` property is not a recognized guard.
- The analyzer treats **Mac Catalyst as a subset of iOS**, so a guarded call still warned "reachable on:
  'maccatalyst'". Fixed by `[UnsupportedOSPlatform("maccatalyst")]` on the API and
  `[UnsupportedOSPlatformGuard("maccatalyst")]` on `IsSupported`. This is also *correct*: nothing in the
  Catalyst chain reaches the SwiftUI shim, and `SwiftDotNet.Skia.Maui` binds only Core's neutral TFM.

Verified end-to-end: unguarded call → `CA1416 … only supported on: 'android', 'ios'`; the same call
inside `if (SafeArea.IsSupported)` → 0 warnings.

## Decision 2 — wire contract

Two modifier types plus one reserved event id, rather than new node types:

| | Encoding |
|---|---|
| `safeAreaPadding` | `{"type":"safeAreaPadding","value":"top,bottom","regions":"container"}` |
| `ignoresSafeArea` | `{"type":"ignoresSafeArea","value":"all","regions":"keyboard"}` |
| insets → C# | event id `$safeArea`, payload `"top;leading;bottom;trailing;keyboard"` |

`value` reuses the existing `[Flags] Edge` enum. The `$` prefix can't collide with a node id — those are
structural paths rooted at `"0"`. Both shims dispatch modifiers through a `switch`/`when` with a
fall-through default, so every other backend ignores these types with no defensive work.

## Decision 3 — how the hosts read insets

- **iOS:** from the **key window**, via a zero-size `SafeAreaReporter` overlay. The obvious approach —
  wrapping the root in a `GeometryReader` — was rejected mid-implementation: `GeometryReader` re-aligns
  its content to top-leading and the `.ignoresSafeArea()` it needs would have pushed *every existing
  app's* layout under the status bar. Keyboard height comes from UIKit's keyboard-frame notifications;
  SwiftUI has no first-class read of it.
- **Android:** `WindowInsets.safeDrawing` + `WindowInsets.ime` read in `RootHostView`, emitted from a
  `LaunchedEffect` keyed on the payload. `SwiftDotNetActivity` now calls `EdgeToEdge.Enable` before
  `base.OnCreate` — without it the insets are zero.

Both hosts report on every layout pass, so `SafeArea.Update` **drops an unchanged payload without
scheduling a render**. That de-duplication is load-bearing, not an optimization.

## What's left

- **Device/simulator verification** — the only reason the status is 🧩 rather than ✅. Needs a notched
  iOS simulator and an Android 15 emulator: real inset values, rotation, and keyboard show/hide.
- **RTL:** the iOS reporter maps UIKit's physical `left`/`right` to `leading`/`trailing`, which is the
  same LTR assumption the rest of the bridge makes for alignment tokens. Compose reads with the real
  `LocalLayoutDirection` and so is already correct — worth reconciling.
- **`safeAreaPadding` ignores `regions` on iOS.** SwiftUI's `.safeAreaPadding` takes edges only; keyboard
  avoidance there is automatic. The token is carried on the wire but unused on that path.
- **A sample that demonstrates it.** `sample/SharedUI/ContentView.cs` doesn't use safe area, so nothing
  exercises the modifiers at runtime yet.

## Unrelated blocker hit while building this — **root-caused 2026-07-20, resolved**

`dotnet build src/SwiftDotNet -f net10.0-android` failed in the main working copy with
`CS0246: 'Com' could not be found`: the Java binding step fed `class-parse` an empty input list
(`obj/Debug/net10.0-android/class-parse.rsp` carried only `--o=`, no jar), so no
`Com.Swiftdotnet.Bridge.*` types were generated.

Everything upstream checked out — the AAR is a valid, standard-layout Android archive; `javap` confirms
`com.swiftdotnet.bridge.SwiftDotNetBridge` and the `EventCallback` interface are `public` with the
expected members (Java 17 bytecode); `-getItem:AndroidLibrary` shows the item resolving with
`Bind="true"`; and `obj/.../library_project_jars/SwiftDotNetComposeBridge.aar/classes.jar` was even
extracted on disk. The jar simply never made it into the item feeding `class-parse`.

**Cause: stale MSBuild intermediate state, and clearing `src/SwiftDotNet/obj` is not enough.** Deleting
that project's `obj`/`bin` (even with `--no-incremental`) still reproduced the failure. A **repo-wide**
`obj`/`bin` clean fixed it immediately, and the rsp then listed the jar. The existing gotcha in
[Android](../docs/backends/android.md) said to clear `src/SwiftDotNet` *and* `sample/SampleApp`; that is
also insufficient once other heads (e.g. `SwiftDotNet.Skia.Maui`) have been built. The doc now says
repo-wide.

No source change was involved — a fresh `git worktree` with the identical AAR built cleanly throughout,
which is what proved this was environment, not regression.

## Fallout: `SafeAreaRegions` collides with MAUI

Core now declares `SwiftDotNet.SafeAreaRegions`, and **`Microsoft.Maui.SafeAreaRegions` /
`Microsoft.Maui.SafeAreaEdges` already exist** with the same simple names (.NET 10's own safe-area API).
Any MAUI-hosted consumer that has both namespaces in scope gets `CS0104: ambiguous reference` — which is
exactly what broke `sample/SampleApp.Skia.Maui`, whose page sets MAUI's `SafeAreaEdges`.

Fixed there by fully qualifying, matching the file's existing `Font`/`Color` alias pattern. Worth
deciding deliberately: this collision will hit every MAUI-hosted app, and the two APIs mean *nearly* the
same thing on the two platforms where both exist. Options: live with qualification (status quo), rename
Core's enum (e.g. `SafeAreaRegion`), or fold the MAUI host's page-level inset handling into the Skia view
so consumers never touch MAUI's version.
