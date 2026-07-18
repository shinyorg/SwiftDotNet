# Plan: Gestures & Transforms (scale / pinch-to-zoom, pan, rotate)

**Status:** Draft. **Part 1 (`.ScaleEffect`) is being implemented now**; Parts 2–3 are future revisions.
**Date:** 2026-07-18
**Scope:** A `.ScaleEffect` (and sibling transform) modifier now, and a `Transformable` **pinch-to-zoom /
pan / rotate gesture** later, across all backends. Establishes the **continuous-gesture event model** the
framework doesn't have yet.

---

## 1. The two halves

Pinch-to-zoom is **two separable primitives**, and only the first is "opacity-like":

| Part | What | Cost | When |
|------|------|------|------|
| **1. Transform modifiers** (`.ScaleEffect`, later `.Rotation`, `.Offset`) | The *visual output* — apply a GPU transform to any view | Low (universal modifier pass, like `.Opacity`) — except GTK | **Now** |
| **2. `Transformable` gesture** (pinch/pan/rotate → live values) | *Recognize* the gesture and feed scale/offset/rotation back to C# | High — needs a **continuous event channel** the framework lacks | Future revision |

## 2. Part 1 — transform modifiers (shipping now)

`.ScaleEffect` rides the same generic modifier pass as `.Opacity`/`.Disabled` — one apply loop per backend
that every node flows through, so it works on any control.

```csharp
public static T ScaleEffect<T>(this T view, double scale, Alignment anchor = Alignment.Center) where T : View;
public static T ScaleEffect<T>(this T view, double x, double y, Alignment anchor = Alignment.Center) where T : View;
```

Reuses the existing `Alignment` enum (9 named points) as the anchor — no new type. Serializes to
`{ "type":"scaleEffect", "x":sx, "y":sy, "value":<anchorToken> }` (reusing the `x`/`y`/`value` wire fields
already in `ModifierData`).

| Backend | Maps to |
|---|---|
| SwiftUI | `.scaleEffect(x:y:anchor:)` (anchor → `UnitPoint`) |
| Compose | `Modifier.graphicsLayer { scaleX; scaleY; transformOrigin = TransformOrigin(fx, fy) }` |
| WinUI | `RenderTransform = ScaleTransform`; `RenderTransformOrigin = Point(fx, fy)` |
| Web | CSS `transform: scale(x, y); transform-origin: <anchor>` |
| **GTK** | ⚠️ **no-op initially** — GTK4 has no generic per-widget scale transform (would need snapshot/`Gtk.Picture` rendering or a zoomable container). Documented limitation; the weak backend again, as with animations/maps. |

**Later transform siblings** (same shape, defer): `.Rotation(angle, anchor)` → `rotationEffect` /
`graphicsLayer{rotationZ}` / `RotateTransform` / CSS `rotate`; `.Offset(x, y)` → `.offset` /
`graphicsLayer{translation}` / `TranslateTransform` / CSS `translate`. Ship these when the gesture needs
them.

`.ScaleEffect` is independently useful **today**: programmatic zoom, press-to-shrink buttons, and — because
scale/offset/rotation are animatable — it's a natural partner to the [animations plan](animations-plan.md)
(`.ScaleEffect(x).Animation(Anim.Spring(), on: x)`).

## 2a. Gesture vocabulary (requested set)

The target gesture set — **tap, long-press, swipe/pan, pinch** — splits cleanly by *event shape*, which is
what determines difficulty. **One-shot** gestures fit the existing `(nodeId, value)` event channel and are
easy; **continuous** gestures need the new channel in §3.

| Gesture | Shape | Recognizers (SwiftUI / Compose / WinUI / Web / GTK) | Payload | Status / effort |
|---------|-------|------------------------------------------------------|---------|-----------------|
| **Tap** | one-shot | already shipped: `.OnTapGesture` | none (or tap count) | ✅ **Done** — extend with `count:` for double-tap |
| **Long-press** | one-shot | `.onLongPressGesture` / `detectTapGestures(onLongPress)` / `Holding`/`RightTapped` / pointerdown+timer / `GestureLongPress` | none (or press duration) | Easy — mirrors tap; **next after `.ScaleEffect`** |
| **Swipe** | one-shot (direction) | `DragGesture` w/ threshold / `detectHorizontalDragGestures` / `ManipulationCompleted` / pointer delta / `GestureSwipe` | direction (`left/right/up/down`) | Easy–med — a directional drag committed on release |
| **Pan (drag)** | **continuous** | `DragGesture` / `detectDragGestures` / `ManipulationDelta` / pointermove / `GestureDrag` | live `dx,dy` | Needs §3 continuous channel |
| **Pinch** | **continuous** | `MagnifyGesture` / `transformable` / `ManipulationDelta.Scale` / two-pointer distance · ctrl+wheel / `GestureZoom` | live `scale` (+ focal point) | Needs §3 continuous channel |

**Delivery split:** tap (done) → **long-press + swipe** as one-shot gestures on the existing channel
(cheap, no framework change) → **pan + pinch** together once the continuous channel (§3) lands, since they
share it and usually combine into one `Transformable`. API shape mirrors `.OnTapGesture`:

```csharp
new Card()
    .OnLongPress(() => _menu.Value = true)
    .OnSwipe(SwipeDirection.Left, () => Delete())        // one-shot
// pan/pinch arrive via Transformable (§3), not a bare modifier, because they're stateful+continuous
```

Each one-shot gesture is another `Modifier` (like `OnTapGestureModifier`) that registers a callback via
`RenderContext.RegisterAction` and serializes an `event` path — no protocol change. Swipe carries its
direction in the event `value` string; long-press optionally carries duration.

## 3. Part 2 — the pan/pinch gestures (the real work)

Recognizing the pinch is *not* the problem — every backend has a recognizer:

| Backend | Recognizer |
|---|---|
| SwiftUI | `MagnifyGesture` (+ `DragGesture`, `RotationGesture`), or a combined `SimultaneousGesture` |
| Compose | `Modifier.transformable(rememberTransformableState { zoom, pan, rotation -> })` |
| WinUI | `ManipulationModes.Scale|TranslateX|TranslateY|Rotate` → `ManipulationDelta` |
| Web | two-pointer distance via Pointer Events; trackpad pinch = `wheel` + `ctrlKey` |
| GTK | `Gtk.GestureZoom` (+ `GestureDrag`, `GestureRotate`) |

The problem is the **event channel**. Today an event is a one-shot `(nodeId, string? value)` callback that
triggers a full C# → `TreeDiffer` → JSON → bridge re-render. A pinch emits **~60 continuous updates/sec**;
routing each through that round-trip won't be smooth. So the decision is **who owns the live transform**:

- **A. Controlled (C# owns scale).** Every delta → `State<double>` → re-render applies `.ScaleEffect`.
  Pure and consistent, but a full round-trip per frame janks over JSON/JNI.
- **B. Native-owned live, C# syncs throttled / on-end.** ✅ The native recognizer applies the transform
  **locally on the GPU** (smooth, zero round-trip) and emits back to C# only at gesture end (or throttled,
  e.g. 10 Hz) to update the bound `State`. This is exactly the pattern SwiftDotNet already uses for
  `TextField`/`Toggle` — "controlled components whose local state syncs via `onChange`" — just at gesture
  frequency.

**Recommendation: model B.** The framework changes it needs:

1. **A continuous / committed event mode.** Extend the event convention so a gesture can emit a `changed`
   stream (throttled) plus a terminal `ended` value — payload encodes scale/offset/rotation (e.g. small
   JSON, same structured-payload need the [maps plan](maps-plan.md) raised for camera changes).
2. **A `Transformable` container view** binding `State<double> scale` (+ optional offset/rotation), with
   `MinScale`/`MaxScale` clamps and a `.ZoomAnchor` (pinch focal point). The native side runs the gesture;
   the binding reflects the committed value.
3. **No per-frame re-render** — the native transform is authoritative during the gesture; the C# tree only
   updates on commit.

```csharp
// Future API sketch
new Transformable(_scale, minScale: 1, maxScale: 4)
    .OnScaleChanged(s => _scale.Value = s)      // throttled / on-end
    .Content(new AsyncImage(url));
```

## 4. Cross-cutting

- **Animations:** scale/offset/rotation are animatable — Part 1 modifiers plug straight into the animations
  plan; a "zoom to fit" is an animated `.ScaleEffect`.
- **Maps:** map pinch-zoom is the *same* continuous-gesture + committed-sync model; the map's own SDK
  handles the gesture, emitting camera changes on the identical channel — validating model B twice.
- **Continuous events** land once here and are reused by maps, sliders-with-live-drag, and any future
  gesture.

## 5. Phases

| Phase | Deliverable | Backends | Risk |
|-------|-------------|----------|------|
| **1 (now)** | `.ScaleEffect(scale/x,y, anchor)` modifier + wire + apply | SwiftUI/Compose/WinUI/Web; GTK no-op | Low |
| **2** | One-shot gestures: `.OnLongPress`, `.OnSwipe(dir)` (+ `.OnTapGesture(count:)`) on the existing event channel | all | Low |
| **3** | `.Rotation` / `.Offset` transform siblings | same as 1 | Low |
| **4** | Continuous/committed event mode; `Transformable` **pinch + pan** (model B) | SwiftUI, Compose, Web first; WinUI; GTK last | High |
| **5** | Rotate combined; focal-point anchor; GTK scale via snapshot/zoom container | all | High |

## 6. Decisions needed

1. **Anchor now or center-only?** Ship `.ScaleEffect` with the full `Alignment` anchor (forward-compatible
   with focal-point zoom) or center-only for simplicity? *Rec: full anchor now — it's cheap on the
   backends that matter and the wire fields already exist.*
2. **GTK scale:** accept a documented no-op for Part 1, or block on a snapshot/zoom-container solution?
   *Rec: no-op now, revisit in Phase 4.*
3. **Gesture ownership (Part 2):** confirm **model B** (native-owned live, throttled sync). *Rec: yes.*
