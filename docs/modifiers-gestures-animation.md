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
| `.SafeAreaPadding` / `.IgnoresSafeArea` | System-chrome insets — **iOS/Android only**, see [below](#safe-area-ios--android-only). |

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

## Safe area (iOS & Android only)

The safe area is the part of the window not covered by system chrome — the status bar, the display
cutout/notch, the home indicator or navigation bar, and (optionally) the soft keyboard. It's a
device-window concept, so unlike every other modifier this one exists **only on iOS and Android**;
the API is annotated `[SupportedOSPlatform("ios")] [SupportedOSPlatform("android")]`.

```csharp
// Guard from a platform-neutral project (SharedUI is net10.0) — this is what silences CA1416.
if (SafeArea.IsSupported)
{
    header = header.IgnoresSafeArea(Edge.Top);        // full-bleed banner under the status bar
    content = content.SafeAreaPadding(Edge.All);      // keep body content clear of the chrome
    input   = input.SafeAreaPadding(Edge.Bottom, SafeAreaRegions.Keyboard);   // lift above the keyboard

    var topInset = SafeArea.Current.Top;              // live values, in points/dp
}
```

| API | What it does |
|-----|--------------|
| `.SafeAreaPadding(edges, regions)` | Insets the view by the safe area. SwiftUI's `.safeAreaPadding(_:)`. |
| `.IgnoresSafeArea(edges, regions)` | Lets the view bleed under the chrome. SwiftUI's `.ignoresSafeArea(_:edges:)`. |
| `SafeArea.Current` | Live `SafeAreaInsets` (`Top`/`Leading`/`Bottom`/`Trailing`/`Keyboard`). |
| `SafeArea.IsSupported` | The platform guard. Carries `[SupportedOSPlatformGuard]`, so the analyzer trusts it. |

`edges` reuses the existing `[Flags] Edge` enum (`Edge.Top`, `Edge.Horizontal`, `Edge.All`, …).
`regions` is `Container` (chrome only — the default for padding), `Keyboard`, or `All`.

`SafeArea.Current` participates in the render loop exactly like `State<T>`: the host pushes new insets
on rotation, keyboard show/hide, and cutout changes, which schedules a re-render, so a `Body` that
reads it recomputes automatically.

| Backend | Behavior |
|---------|----------|
| **iOS** (SwiftUI) | `.safeAreaPadding` / `.ignoresSafeArea` directly. Insets come from the key window; keyboard height from UIKit's keyboard-frame notifications. |
| **Android** (Compose) | `Modifier.windowInsetsPadding` / `consumeWindowInsets` over `WindowInsets.safeDrawing` (unioned with `WindowInsets.ime` for the keyboard region). `SwiftDotNetActivity` calls `EdgeToEdge.Enable` so the insets are non-zero. |
| macOS / tvOS / GTK / WinUI / Web / Skia | **Not available** — CA1416 at the call site; the wire modifier is ignored if one reaches them anyway. |

> **Gotchas.**
> - **Compose is edge-to-edge by default**, so "ignoring" the safe area is the *absence* of padding.
>   `.IgnoresSafeArea` therefore *consumes* the insets, which stops a descendant's `.SafeAreaPadding`
>   from re-insetting a region you deliberately bled into.
> - **`SafeArea.Current` is zero on the first render.** Both hosts report after their first layout
>   pass; the report then triggers a re-render with the real values. Lay out so zeros are harmless.
> - **Reports are de-duplicated.** Both hosts emit on every layout pass; an unchanged report is dropped
>   without scheduling a render, so reading the insets doesn't spin the render loop.
> - **Mac Catalyst is explicitly excluded** (`[UnsupportedOSPlatform("maccatalyst")]`). The analyzer
>   treats Catalyst as a subset of iOS, but nothing in the Catalyst chain reaches the SwiftUI shim —
>   including [`SwiftDotNet.Skia.Maui`](backends/skia.md), which binds only Core's neutral `net10.0` TFM
>   and so cannot see this API at all.
> - **Android IME insets need `adjustResize`** (the default) — a host activity that sets
>   `windowSoftInputMode="adjustPan"` will report a keyboard height of 0.
>
> **Status:** 🧩 Scaffolded. The wire contract, inset plumbing and de-duplication are covered by
> [`SafeAreaTests.cs`](../tests/SwiftDotNet.Tests/SafeAreaTests.cs) (21 tests), and both shims compile
> (Swift for iOS/macOS/tvOS, Kotlin via `assembleRelease`). **Not yet verified on a device or
> simulator** — the real inset values, rotation, and keyboard behavior are unconfirmed.

Source: [`SafeArea.cs`](../src/SwiftDotNet/Core/SafeArea.cs),
[`ViewModifiers.cs`](../src/SwiftDotNet/Core/ViewModifiers.cs).

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
