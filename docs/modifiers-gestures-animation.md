# Modifiers, Gestures & Animation

Modifiers, gestures, and animation are applied to *any* view with a fluent call. They're implemented as a
**universal wrapper** — a single generic pass per backend — so they work on every control, not a hand-picked
subset. Modifiers are **order-preserving**: the chain applies in the order you write it.

## Modifiers

```csharp
new Text("Hi")
    .Font(Font.Headline)
    .ForegroundColor(Color.Hex("#7C4DFF"))
    .Padding()                       // uniform
    .Padding(Edge.Top, 8)            // per-edge
    .Background(Color.Secondary)
    .CornerRadius(12)
    .Border(Color.Primary, 1)
    .Shadow(radius: 4, color: Color.Black, x: 0, y: 2)
    .Frame(width: 200, height: 44, alignment: Alignment.Center)
    .Opacity(0.9)                    // clamped 0–1
    .Disabled(isBusy)                // dim + block interaction
    .ScaleEffect(1.1, anchor: Alignment.Center)
    .Align(Alignment.Leading)        // fill width + align
    .NavigationTitle("Home");
```

| Modifier | Effect |
|----------|--------|
| `.Font` / `.ForegroundColor` / `.Background` | Text/appearance. |
| `.Padding` | Uniform or per-`Edge`. |
| `.Frame` | Size (+ alignment). |
| `.CornerRadius` / `.Border` / `.Shadow` | Box decoration (`.Shadow` takes color/offset). |
| `.Opacity` | Clamped 0–1. |
| `.Disabled` | Dim + block interaction. |
| `.ScaleEffect` | Native scale transform, around an anchor. |
| `.Align` | Fill width + align. |
| `.NavigationTitle` | Nav bar title. |

Because modifiers are a universal wrapper, `.Opacity` / `.Disabled` / `.ScaleEffect` work on **every** view.

> **Backend notes.**
> - `.ScaleEffect` / `.Rotation` on **GTK** wrap the widget in a `Gtk.Fixed` carrying a `GskTransform`
>   (they were silent no-ops until 2026-07-20). Not visually verified — see
>   [GTK](backends/linux-gtk.md).
> - `.Shadow` offset is ignored on **Android** (Compose maps shadow to elevation). On **WinUI** it's a
>   Composition `DropShadow` with no alpha mask, so a rounded element casts a rectangular shadow.
> - `.Material` is a real backdrop blur only on **SwiftUI** and **Web**; GTK, Skia, Compose and WinUI
>   render a translucent tint. On Compose that's deliberate — `Modifier.blur` blurs the node's own content,
>   not the backdrop.

## Gestures

One-shot gestures, attachable to any view. Each maps to the platform's native recognizer and fires back
through the same event channel as taps.

```csharp
new Text("Tap / hold / swipe me")
    .OnTapGesture(count: 2, () => …)                 // single or double tap
    .OnLongPress(minimumDuration: 0.5, () => …)      // press-and-hold
    .OnSwipe(SwipeDirection.Left, () => …);          // one call per direction
```

| Gesture | Maps to |
|---------|---------|
| `.OnTapGesture(count:)` | SwiftUI `onTapGesture`, Compose `detectTapGestures`, WinUI `Tapped`, GTK `GestureClick`, Web `onclick` |
| `.OnLongPress(minimumDuration:)` | SwiftUI `onLongPressGesture`, Compose `detectTapGestures`, WinUI `Holding`, GTK `GestureLongPress`, Web pointer events |
| `.OnSwipe(direction, …)` | SwiftUI drag, Compose `detectDragGestures`, WinUI `ManipulationCompleted`, GTK `GestureSwipe`, Web pointer events |

### Continuous drag and pinch

`.OnDrag` and `.OnMagnify` are continuous: `.OnDrag` delivers a `DragInfo` (phase, cumulative translation,
location, release velocity) on every move, and `.OnMagnify` a cumulative scale factor. They're what the
Controls library's `Slider`, `RangeSlider`, `ColorPicker`, `FloatingPanel`, `SwipeContainer`,
`ReorderableList` and `ImageViewer` are built on.

```csharp
new Rectangle()
    .OnDrag(info => _offset.Value = info.Translation)   // Began → Changed… → Ended
    .OnMagnify(scale => _zoom.Value = Math.Clamp(scale, 1, 5));
```

| Backend | Source |
|---------|--------|
| SwiftUI | `DragGesture` / `MagnificationGesture` |
| Compose | `detectDragGestures` / `detectTransformGestures` |
| GTK | `GestureDrag` / `GestureZoom` (drag velocity is unavailable and sent as 0) |
| Web | pointer events; pinch = two live pointers, plus a ctrl+wheel trackpad path |
| WinUI | manipulation events (**uncompiled** — see [Windows](backends/windows.md)) |
| Skia | **nothing supplies these** — a self-drawing backend has no recognizers. Hosts must feed [`SkiaPointerRouter`](../src/SwiftDotNet.Skia/SkiaPointerRouter.cs); a host that doesn't gets tap-only. |

## Animation

Implicit animation interpolates a view's animatable modifiers (opacity, frame size, …) when a value
changes — mirroring SwiftUI's `.animation(_:value:)`:

```csharp
someView.Animation(Anim.EaseInOut(0.3), on: _expanded.Value);
```

Specs:

| Spec | |
|------|--|
| `Anim.Linear(duration)` | |
| `Anim.EaseIn/EaseOut/EaseInOut(duration)` | |
| `Anim.Spring()` | Native spring where available; degrades to a bezier (Web) or ease-in-out (GTK). |

It maps to real native animation — SwiftUI `.animation`, Compose `animateContentSize`/`animateFloatAsState`,
WinUI theme transitions, GTK/Web CSS `transition`, and Skia's own interpolation clock.

### Repeating (self-playing) animations

`.Repeating(count, autoreverse)` turns a spec into a loop that plays on its own with no `on:` trigger —
`count: -1` runs forever. It's what `SkeletonView`'s shimmer and `BadgeView`'s pulse use:

```csharp
view.Animation(Anim.EaseInOut(1.0).Repeating(autoreverse: true), on: true);
```

> **Gotcha — it always pulses opacity.** The wire carries no from/to pair, only "play forever", so every
> backend loops **opacity between the resting value and 0.4×** (Web's shared `sdn-pulse` keyframes are the
> reference; Skia, GTK and Compose match it deliberately so the effect reads identically). A repeating
> animation on a *scale* or *colour* therefore still reads as an opacity pulse. GTK additionally cannot
> loop a transform at all (its CSS has no `transform`), and **WinUI does not loop** — it gets only a
> reposition transition, so shimmer and pulse are static there.

Explicit `Animate.Run(…)` transactions and enter/leave `.Transition(…)` are **later phases** — see the
[Roadmap](roadmap.md).

## Related

- [Views & Controls](views-and-controls.md) — what you apply these to.
- [Global Styles](global-styles.md) — modifiers authored once and applied to many views.
- [Custom Controls](custom-controls.md) — the `.Tag(name)` seam for reaching a specific native view.
