# Skia MAUI host — iOS/Android enablement and the "repaint defect"

**Status — 2026-07-29: resolved and verified.** The host renders, navigates and repaints on state change on
both an **iOS simulator** and an **Android emulator**. Reference: [Skia backend](../docs/backends/skia.md).

## What the defect actually was

The symptom was filed as a host bug — "no C# state change ever reaches the screen on the MAUI host, while
engine-local input (nav push/pop) works". The leading hypothesis below was that `SwiftApp`'s static
singleton state had been rebound by a second `SwiftDotNetSkiaView`, leaving renders going to an off-screen
bridge. **That was wrong**, and the instrumentation run the plan called for is what disproved it.

One simulator run with `SwiftApp.Run` / `SwiftApp.Render` / `SkiaBridge.Render` / `OnInvalidate` /
`OnPaintSurface` logging showed:

| Observation | Conclusion |
|---|---|
| `Run #1`, one bridge hash, one view hash, for the whole session | Not a second view; not a singleton rebind |
| A trivial self-ticking `State<int>` view repainted every second on device | The host's state→render→patch→invalidate→paint chain is intact |
| With the *real* sample: `OnEvent` fires, the action is found and runs, a render happens — and reports **`HasChanges=False`** | The diff is empty; the host never gets a patch because there is nothing to send |
| The identical `HasChanges=False` reproduces in the **headless Skia harness** on macOS | Not a MAUI problem, not even a Skia problem |

The cause was in the **sample**, and it was a one-line regression that the DI work introduced:

```csharp
// sample/SharedUI/SampleRootView.cs
public override View Body => new OverlayHost(new ContentView());   // ← rebuilt every render pass
```

`Body` runs on every render. `new ContentView()` therefore produced a *fresh* instance each pass, with a
fresh set of `State<T>` fields at their initial values. A control's callback closed over the previous
instance's state, dutifully assigned it, and scheduled a render — which built yet another virgin
`ContentView`, diffed identical to the last tree, and shipped nothing. Engine-local interactions kept
working because they never leave the Skia scene tree, which is precisely why it read as a repaint bug.

The fix is to hold the child: `readonly ContentView _content = new();`. `OverlayHost` is stateless (the
overlay layer is static), so rebuilding *it* is fine.

## Why nothing caught it

The headless Skia harness *had* the same defect the whole time — its `page_text_typed.png` showed
"Hello, stranger!" and an empty Name field. The screenshots were being produced, not read. A harness that
only proves "it renders" cannot prove "it works"; the same lesson as
[`controls-library.md`](../docs/controls-library.md).

Now pinned by [`RetainedChildStateTests`](../tests/SwiftDotNet.Tests/RetainedChildStateTests.cs), which
asserts both directions — a retained stateful child produces a patch carrying the new value, a rebuilt one
produces no patch at all — plus the end-to-end Skia path (tap to focus a `TextField`, type, check the bound
text repainted).

## The two build defects found alongside it

Neither is a code bug in the engine; both are packaging, and both are now fixed in-repo (details and
rationale in [Skia backend → Gotchas](../docs/backends/skia.md#gotchas)):

1. **`TypeLoadException: VTable setup of type Microsoft.Maui.Controls.Page failed` at launch.** MAUI's two
   halves floated to different versions (`Microsoft.Maui.Core` 10.0.80 via Shiny, `Controls.Core` 10.0.20
   from the workload). Fixed by referencing `Microsoft.Maui.Controls` explicitly in both the host library
   and the sample head. The old note here — "workload gap, run `dotnet workload update`" — was wrong. A
   *stale `.app` directory* after toggling `-p:NoShiny=true` produces the identical error; clean first.
2. **Android could not build at all.** Resolved by reconciling the Compose backend's AndroidX pins with
   MAUI's (Activity.Compose ≥ 1.10.1, Compose ≥ 1.10 to avoid a D8 duplicate-class on
   `androidx.compose.runtime.Immutable`) and by breaking MAUI 10.0.x's own unsatisfiable
   `Lifecycle.LiveData` constraint with a direct reference.

## What landed for the platforms

- `SwiftDotNet.Skia.Maui` and `sample/SampleApp.Skia.Maui` target
  `net10.0-ios;net10.0-maccatalyst;net10.0-android` (+ the Windows TFM when built on Windows).
- Android head: `Platforms/Android/MainActivity.cs` + `MainApplication.cs`, `SupportedOSPlatformVersion`
  split per head (an iOS version string vs. an Android API level).
- `SafeAreaEdges` on the page — .NET 10's `ContentPage` is edge-to-edge by default and the Skia canvas draws
  the entire UI, so without it the scene paints under the status bar and Dynamic Island.
- `SwiftDotNetSkiaView` takes an `IServiceProvider` so the Skia UI and the MAUI container share one provider.

## Follow-up: the touch gaps (2026-07-29)

Driving the app by hand turned up three more things that were *shipped but dead under a finger*, none of
them MAUI-specific — they were missing from the engine, and only a touch host exposes them:

1. **Scrolling.** It only ever arrived via `Scroll(point, dy)`, which a host raises from a **wheel**. A
   touch host has none, so the flyout could not be scrolled at all. Now `BeginPan`/`PanScroll`.
2. **Slider drag.** The continuous-drag path keys off an `.OnDrag` *modifier*, which the built-in `Slider`
   does not carry — it was tap-to-set, and a drag past `TapSlop` cancelled even that. Now `BeginScrub`/
   `Scrub`, restricted to continuous controls so discrete ones don't fire once per pointer-move.
3. **Text entry.** Now a shadow `Entry` focused off `SkiaBridge.FocusChanged`. The trap: it must not be
   `InputTransparent`, which on iOS blocks first-responder and silently returns `Focus() == false`.

Plus two engine bugs found on the way: an overlay presented *inside* a pushed page was never composited
(the overlay walk only descended `Children`), and the `ColorPicker` blind-cycled its palette instead of
offering a picker. Both fixed; see [Skia backend](../docs/backends/skia.md).

**Methodology note.** Android fast deployment (`-t:Install`) silently kept serving assemblies from an
earlier build — every "still broken" observation for an hour was against stale code. If a change appears to
have no effect on Android, check `run-as <pkg> ls -la files/.__override__/<abi>/` before believing it, and
`adb uninstall` to force a full push.

## Still open

- **Caret placement and selection.** The IME hands back the whole string, so edits always land at the end;
  you cannot tap into the middle of a field. Same for keyboard avoidance — the engine does not know how much
  of its canvas the keyboard covers.
- **No pan inertia.** A flick stops dead where the finger lifts; no fling or rubber-band.
- **No automated coverage of any MAUI host.** The regression tests added here run against Core and the Skia
  engine; the MAUI adapter itself is still only verified by hand on a simulator/emulator.
- **The Windows MAUI head is unbuilt** — it needs a Windows machine.
- **De-singletoning `SwiftApp`** is still worth doing for [Windows / Scenes](windows-plan.md), but this
  defect is no longer an argument for it — it turned out to be unrelated.

## Related

- [Windows / Scenes](windows-plan.md) — owns the de-singletoning milestone.
- [State & binding](../docs/state-and-binding.md#gotcha-a-view-that-owns-state-must-be-held-not-rebuilt) —
  the retained-child rule, documented for users.
- [Skia backend](../docs/backends/skia.md) — host table and the pointer-router contract.
