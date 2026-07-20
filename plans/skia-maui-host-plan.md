# Skia MAUI host — iOS enablement and the repaint defect

**Status — 2026-07-20: iOS runs; state-driven repaint is broken.** The host renders and navigates
correctly on an iOS simulator, but **no C# state change ever reaches the screen**. Reference:
[Skia backend](../docs/backends/skia.md).

## What landed

`SwiftDotNet.Skia.Maui` and `sample/SampleApp.Skia.Maui` now multi-target `net10.0-ios;net10.0-maccatalyst`
(iOS was previously deferred). Three things were needed:

1. **`Platforms/iOS/`** — `AppDelegate`, `Program`, `Info.plist`, mirroring the MacCatalyst set.
2. **`<Import Project="SwiftDotNetBridge.targets" />` in the app.** The old comment said iOS was blocked
   because it "pulls the SwiftUI Swift bridge". Half right: nothing in the Skia chain multi-targets into
   `net10.0-ios`, but the *app's* ProjectReference graph still resolves `SwiftDotNet` to its iOS slice,
   whose P/Invokes (`swiftdotnet_render`, …) the native linker must resolve. `NativeReference` does not
   flow transitively, so the app declares the xcframework — the rule CLAUDE.md already states.
3. **`SafeAreaEdges`** on the page. .NET 10's `ContentPage` defaults to edge-to-edge, and the Skia canvas
   draws the entire UI, so the scene painted under the status bar and Dynamic Island.

`SwiftDotNetSkiaView` also gained an `IServiceProvider` overload so the Skia UI and the MAUI container
share one provider, matching the AppKit host.

## The defect

On the simulator: the tree paints, and **engine-local** interactions work — tapping a row pushes a nav
page, "‹ Back" pops it. But anything that round-trips through C# state produces no visual change. A
double-tap on the sample's gesture page, a drag, a long-press: none update the bound `_gesture` text.

What instrumentation established (temporary logging, since removed):

| Observation | Conclusion |
|---|---|
| `SKTouchAction.Pressed/Moved/Released` all arrive, correct DIP coordinates | Host touch wiring is fine |
| The frame clock advances (`_clock` 20.9 → 25.2) | The dispatcher timer runs |
| `Emit` fires with `handler=set`, `ctx=UIKitSynchronizationContext` | The event reaches Core's `OnEvent` with a real UI pump |
| The same paths pass in `tests/SwiftDotNet.Tests` on macOS | Core's event → state → diff → patch chain is not at fault |

So the break is **after** `Emit` and **before** the canvas — either the render is never produced, or it
is produced against a bridge that isn't the one on screen.

### Leading hypothesis — `SwiftApp` is a static singleton

`SwiftApp` holds `_bridge`, `_lastTree`, `_uiContext` in statics, and `SwiftApp.Run` overwrites all of
them. `SwiftDotNetSkiaView`'s constructor calls `Run`. If MAUI constructs `MainPage` (and therefore a
second `SwiftDotNetSkiaView`) more than once — a window re-create, a hot restart, a second `CreateWindow`
— then `_bridge` points at the **newest** view while the **first** view is the one on screen. Renders
would be applied to an off-screen bridge, which matches the symptoms exactly: first paint fine,
engine-local input fine, state-driven updates invisible.

This is the same root problem [`windows-plan.md`](windows-plan.md) already names as its Step 0
("de-singletoning `SwiftApp`"), which makes this defect a concrete, cheap forcing function for that work.

Competing hypotheses not yet excluded: `Dispatcher.Dispatch(InvalidateSurface)` no-opping for this view;
`SKCanvasView` not repainting on an already-scheduled surface; the diff emitting `updateProps` the Skia
bridge fails to apply for this tree shape.

## Next steps, cheapest first

1. **Log once in `SwiftApp.Run`** (instance count + `bridge.GetHashCode()`) and once in
   `SkiaBridge.Render`. One simulator run distinguishes "Run called twice" from "render never arrives"
   from "render arrives at the wrong bridge". Do this before anything else — it is decisive.
2. If `Run` is called twice → make `SwiftDotNetSkiaView` own its `SwiftApp` state rather than mutating
   statics; that is the first slice of the de-singletoning milestone.
3. If the render arrives at the right bridge but nothing paints → the fault is in
   `OnInvalidate`/`InvalidateSurface`; compare against the AppKit host, which repaints correctly.
4. **Add a Skia MAUI smoke test** once fixed. There is currently no automated coverage of any MAUI host,
   which is why this shipped unnoticed.

## Why the Skia unit tests missed it

They drive `SkiaBridge` directly and never construct a MAUI view, so the host's `SwiftApp.Run` lifetime
is not exercised. The general lesson matches the one in
[`controls-library.md`](../docs/controls-library.md): a control that *renders* is not a control that
*works*, and a headless harness proves only the former.

## Related

- [Windows / Scenes](windows-plan.md) — owns the de-singletoning milestone this likely blocks on.
- [Skia backend](../docs/backends/skia.md) — host table and the pointer-router contract.
