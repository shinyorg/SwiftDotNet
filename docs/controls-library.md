# The Controls Library (`SwiftDotNet.Controls`)

A companion package of higher-level controls — a port of the Shiny controls set — built entirely on the
core DSL. See [Plan 2](../plans/controls-library-plan.md) for the port's scope and history.

## What it is

**Every control is a pure composite.** Each one is an ordinary `View` whose `Body` lowers to the core views
every backend already draws — there is not a single `ctx.NewNode` call in
[`src/SwiftDotNet.Controls`](../src/SwiftDotNet.Controls). It adds **zero new node kinds** and requires
**zero new backend code**.

That has a direct consequence worth internalising: *backend support for the Controls library is not a
question about the controls at all.* It is entirely a question of **core modifier parity**. A control works
on a backend exactly to the extent that backend implements the modifiers the control composes from. When
`ImageViewer` doesn't zoom somewhere, the cause is always a missing `.OnMagnify`, never a missing
`ImageViewer`.

The one exception is [`CameraView`](../src/SwiftDotNet.Controls.Camera) — a genuine native primitive
(`CustomView`), not a composite. It needs a registered per-backend renderer.

```csharp
using SwiftDotNet.Controls;

new VStack(
    new PillView("Active", PillType.Success),
    new SkeletonView(height: 20),
    new Slider(_volume),
    new Fab("plus", () => Toast.Show("Added")))
```

## What each control depends on

| Control(s) | Core primitives they need |
|---|---|
| `Slider`, `RangeSlider`, `ColorPicker`, `FloatingPanel`, `SwipeContainer`, `ReorderableList` | `.OnDrag`, `.Offset`, `.Shadow` |
| `ImageViewer` | `.OnDrag`, `.OnMagnify`, `.ScaleEffect`, raster + URL `Image` |
| `Toast`, `Dialog`, `LoadingOverlay`, `DurationPicker` | `Overlay`/`OverlayHost` → **`ZStack` + `alignment`**, `.Shadow` |
| `SkeletonView` | gradient `.Background`, repeating `.Animation` |
| `BadgeView` | `.Offset`, `.ScaleEffect`, repeating `.Animation` |
| `FabMenu` | `.Rotation`, `.Shadow` |
| `FrostedGlassView`, `LoadingOverlay` | `.Material` (blur) |
| `ChatView` | raster + URL `Image`, keyboard config |
| `SecurityPin`, `AutoCompleteEntry` | `KeyboardType` / `ReturnKey` / `MaxLength` |
| everything | `.CornerRadius`, `.Border`, `.Frame`, `.Align`, `.Opacity`, `.Padding`, `.Font`, `.ForegroundColor`, `.OnTapGesture` |

## Per-backend reality

All 28 controls **render** on all seven backends. What differs is interaction and effects. Last audited
against the code on **2026-07-20**.

| | Apple | Web | Compose | Skia | GTK | WinUI |
|---|---|---|---|---|---|---|
| Continuous drag (7 controls) | ✅ | ✅ | ✅ | ✅¹ | ✅ | ✅² |
| Pinch (`ImageViewer`) | ✅ | ✅³ | ✅ | ✅¹ | ✅ | ✅² |
| Overlay positioning (`Bottom`/`Top`) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅² |
| `.Shadow` (8 controls) | ✅ | ✅ | ✅⁴ | ✅ | ✅ | ✅²˒⁵ |
| Shimmer / pulse loop | ✅ | ✅⁶ | ✅⁶ | ✅⁶ | ✅⁶˒⁷ | ❌⁸ |
| `.ScaleEffect` / `.Rotation` | ✅ | ✅ | ✅ | ✅ | ✅⁹ | ✅² |
| Real backdrop blur | ✅ | ✅ | tint | tint | tint | tint |
| URL images | ✅ | ✅ | ✅ | ✅ | ✅ | ✅² |
| `CameraView` | ✅ | ❌ | ❌¹⁰ | placeholder | ❌ | ❌ |

¹ Only if the host wires [`SkiaPointerRouter`](../src/SwiftDotNet.Skia/SkiaPointerRouter.cs). A
self-drawing backend has no gesture recognizers, so a host that forwards only taps leaves all seven
drag-driven controls **inert while looking perfectly correct**. All three in-repo hosts are wired.
² WinUI is **uncompiled and untested** — see [Windows](backends/windows.md).
³ Touch pinch re-baselines per gesture; the ctrl+wheel trackpad path accumulates for the component's
lifetime (no gesture boundary exists in the browser). `ImageViewer` clamps, so it behaves.
⁴ Compose maps shadow to elevation, so the offset is ignored.
⁵ Composition `DropShadow` with no alpha mask → a rounded element casts a rectangular shadow.
⁶ Loops **opacity 1 → 0.4** on every backend — the wire carries no from/to pair. So `BadgeView.Pulse` reads
as an opacity pulse rather than a size pulse, and `SkeletonView`'s shimmer fades rather than travelling.
⁷ GTK cannot loop a transform (its CSS has no `transform`); opacity only.
⁸ WinUI has no looping animation — shimmer and pulse are static.
⁹ Via a `Gtk.Fixed` + `GskTransform` wrapper. Not visually verified.
¹⁰ The Compose `CameraRenderer.kt` exists but is **not compiled into the bridge AAR** — see
[Android](backends/android.md).

## Gotchas

- **Shapes fill via `.ForegroundColor`, not `.Background`.** A `Rectangle().Background(gradient)` paints
  nothing useful; that's why `SkeletonView` composes a plain `ZStack` with a background rather than a shape.
- **A control that renders is not a control that works.** The Skia gesture gap above went unnoticed for
  exactly this reason: screenshot review passes, interaction doesn't. When verifying a backend, drive the
  gestures.
- **`Overlay` positioning rides on `ZStack`'s `alignment` prop.** A backend that ignores that prop silently
  centres every toast and floating panel — which is what GTK, Web and WinUI all did until 2026-07-20. If you
  add a backend, this is easy to miss because nothing errors.

## Related

- [Views & Controls](views-and-controls.md) — the core views these compose from.
- [Modifiers, Gestures & Animation](modifiers-gestures-animation.md) — the modifiers in the table above.
- [Custom Controls](custom-controls.md) — the renderer-registry seam `CameraView` uses.
- [Backends](backends/README.md) — per-backend detail and status.
- [Plan 1 — framework features](../plans/controls-missing-features-plan.md) ·
  [Plan 2 — the controls port](../plans/controls-library-plan.md).
