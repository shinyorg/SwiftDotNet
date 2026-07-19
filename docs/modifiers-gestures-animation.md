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

> **Backend note:** `.ScaleEffect` is a documented **no-op on GTK**, which has no per-widget scale transform.
> `.Shadow` offset is ignored on Android (Compose maps shadow to elevation).

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

Continuous pan/pinch (a `Transformable` binding) is a **later phase** — see the [Roadmap](roadmap.md).

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
WinUI theme transitions, GTK/Web CSS `transition`.

Explicit `Animate.Run(…)` transactions and enter/leave `.Transition(…)` are **later phases** — see the
[Roadmap](roadmap.md).

## Related

- [Views & Controls](views-and-controls.md) — what you apply these to.
- [Global Styles](global-styles.md) — modifiers authored once and applied to many views.
- [Custom Controls](custom-controls.md) — the `.Tag(name)` seam for reaching a specific native view.
