# Plan: Animations for SwiftDotNet

**Status:** Draft for review
**Date:** 2026-07-18
**Scope:** Add SwiftUI-style animations — implicit (`.Animation(...)`), explicit
(`withAnimation`-style transactions), and view transitions (insert/remove) — across all backends
(SwiftUI, Compose, GTK, WinUI, Web), reusing the existing modifier + diff/patch pipeline.

---

## 1. Goal

Let a state change animate instead of snapping:

```csharp
// implicit — this view animates whenever _expanded flips
new VStack(...)
    .Frame(height: _expanded.Value ? 300 : 80)
    .Animation(Anim.EaseInOut(0.25), on: _expanded.Value);

// explicit — everything this mutation touches animates
new Button("Toggle", () => Animate.Run(Anim.Spring(), () => _expanded.Value = !_expanded.Value));

// transition — how a view enters/leaves when inserted/removed
if (_showDetail.Value)
    new DetailCard().Transition(Transition.Slide + Transition.Opacity);
```

…and have it map to **real native animation** on each platform: SwiftUI's `withAnimation`/`.animation`,
Compose's `animate*AsState`/`AnimatedVisibility`, WinUI Storyboards, GTK tick/CSS, and CSS transitions on
the Web.

## 2. How SwiftDotNet renders today (what animation must hook into)

The pipeline is already a perfect seam for animation, because animation *is* the transition between two
rendered trees — and we already compute that transition explicitly:

```
State.Value = x  ──►  SwiftApp.Render()  ──►  root.ToNode()  ──►  TreeDiffer.Diff(prev, next)
                                                                        │  ops: replace / updateProps / setChildren
                                                                        ▼
                                                              Patch.ToJson()  ──►  bridge  ──►  native applies to VNode tree
```

Relevant facts (from `Core/`):

- **Modifiers** (`Modifier.cs`) serialize to `Dictionary<string,object>` bags like
  `{ "type":"opacity", "amount":0.5 }`; each node carries an ordered `Modifiers` list (`Node.cs`). Adding
  an animation modifier is just another entry — no protocol surgery.
- **The diff** (`TreeDiffer.cs`) already knows *exactly what changed*: `updateProps` when props/modifiers
  changed, `setChildren` when a child list changed shape, `replace` for a root/type swap. Native animation
  hangs off precisely these ops.
- **`State<T>.Value`** calls `SwiftApp.RequestRender()` **synchronously and immediately** on every set.
  This is the one thing that must change for explicit transactions (§6): a `withAnimation { a=1; b=2; }`
  block must coalesce into **one** render that carries the animation spec, not three unrelated renders.
- Node identity is a **structural path** ("0.2.1"). Insert/remove of list items currently shows up as a
  whole-subtree `setChildren` with no notion of "this row is new" — so **transitions depend on keyed
  reconciliation** (the existing "keyed `ForEach`" milestone). Implicit/explicit animation of *existing*
  views does **not**; it can ship first.

## 3. Design overview

Three independent capabilities, in increasing cost:

| Capability | Trigger | What animates | Backend cost | Depends on |
|-----------|---------|---------------|--------------|------------|
| **A. Animatable modifiers** | — | The set of properties that *can* be interpolated (opacity, offset, scale, rotation, color, frame size) | Low–med | — |
| **B. Implicit `.Animation(spec, on:)`** | a value changes | That view's animatable props/layout | Low (SwiftUI/Web), med (Compose/WinUI/GTK) | A |
| **C. Explicit `Animate.Run(spec, ()=>…)`** | a state mutation | Everything the mutation changed | Low (SwiftUI), med (others) | A + render batching (§6) |
| **D. Transitions `.Transition(...)`** | insert / remove | How a view enters/leaves | Med–high | A + **keyed reconciliation** |

### 3.1 The animation spec (shared value object)

```csharp
public enum AnimationCurve { Linear, EaseIn, EaseOut, EaseInOut, Spring }

public readonly record struct AnimationSpec(
    AnimationCurve Curve,
    double Duration = 0.3,     // seconds; ignored for pure spring
    double Delay = 0,
    double? SpringStiffness = null,
    double? SpringDamping = null);

public static class Anim   // ergonomic factories
{
    public static AnimationSpec Linear(double d = 0.3) => new(AnimationCurve.Linear, d);
    public static AnimationSpec EaseInOut(double d = 0.3) => new(AnimationCurve.EaseInOut, d);
    public static AnimationSpec Spring(double stiffness = 170, double damping = 26) =>
        new(AnimationCurve.Spring, SpringStiffness: stiffness, SpringDamping: damping);
    // …EaseIn, EaseOut
}
```

`Spring` is native on SwiftUI/Compose/WinUI-Composition; on Web it approximates via `cubic-bezier`, on GTK
it degrades to `EaseInOut`. **Graceful degradation is a first-class rule:** an unsupported spec animates
with the nearest supported curve, never throws, never no-ops silently (log once).

## 4. Public API

### 4.1 Implicit — an `.Animation(...)` modifier

```csharp
public static T Animation<T>(this T view, AnimationSpec spec, object? on = null) where T : View;
```

- `on:` is the SwiftUI `value:` trigger — an `Equatable`-ish token (we already box `string/double/bool`)
  whose change arms the animation. Passing `_expanded.Value` means "animate when the expanded flag flips."
- Serializes to a modifier bag: `{ "type":"animation", "curve":"easeInOut", "duration":0.25, "trigger":"<hash>" }`.
  The interpreter attaches the platform animation and keys it on `trigger`.

### 4.2 Explicit — a `withAnimation` transaction

```csharp
public static class Animate
{
    public static void Run(AnimationSpec spec, Action mutations);
}
// Animate.Run(Anim.Spring(), () => { _expanded.Value = true; _offset.Value = 0; });
```

Sets a render-scoped "current animation", batches the mutations into one render, and tags the resulting
`Patch` with the spec. Native side wraps patch application so *all* resulting changes animate together
(§5, §6).

### 4.3 Transitions — `.Transition(...)`

```csharp
public readonly record struct Transition { /* Opacity | Scale | Slide | Move(edge) | combos via + */ }
public static T Transition<T>(this T view, Transition t) where T : View;
```

Serializes as a modifier the interpreter reads when the node is **inserted or removed** (requires keyed
identity to detect that — §7).

## 5. Wire protocol changes

Additive; the existing shape is untouched.

1. **New modifier type** `animation` (and `transition`) in the node `Modifiers` list — handled by
   `TreeDiffer.ModifiersEqual` for free (it's just another dict).
2. **Patch-level animation tag** for explicit transactions. `Patch.ToJson` currently emits
   `{"ops":[…]}`; extend to `{"anim":{"curve":…,"duration":…},"ops":[…]}` when the render happened inside
   `Animate.Run`. Native hosts that see `anim` wrap the whole op-application in a platform animation
   transaction. This is the elegant part: **the diff already isolates the transition; we just annotate it.**
3. `NodeJson` gains an `AppendAnimation` helper (hand-rolled, AOT-safe, consistent with the existing
   zero-reflection serializer).

## 6. Render batching (prerequisite for explicit animation — and a general win)

Today three `State` sets = three renders. `Animate.Run` (and good behavior generally) needs coalescing:

```csharp
// SwiftApp
static int _txnDepth;
static AnimationSpec? _txnAnim;
static bool _renderQueued;

internal static void RequestRender()
{
    if (_txnDepth > 0) { _renderQueued = true; return; }   // defer during a transaction
    RenderNow(_txnAnim);
}

public static void Transaction(AnimationSpec? anim, Action body)
{
    _txnDepth++; _txnAnim ??= anim;
    try { body(); }
    finally { if (--_txnDepth == 0) { var a = _txnAnim; _txnAnim = null;
                                      if (_renderQueued) { _renderQueued = false; RenderNow(a); } } }
}
```

`Animate.Run(spec, body)` == `SwiftApp.Transaction(spec, body)`. This also lays the groundwork for
batching multi-state updates in general and composes with the DI proposal's `ISwiftDispatcher` (marshal
the coalesced render to the UI thread).

## 7. Per-backend implementation

| Backend | Implicit `.Animation` | Explicit transaction | Transitions | Notes |
|---------|----------------------|----------------------|-------------|-------|
| **SwiftUI** (iOS/tvOS/macOS) | `.animation(spec, value: trigger)` on `NodeView` | wrap the observed-store mutation in `withAnimation(spec) { apply patch }` | `.transition(...)` + insert/remove inside `withAnimation` | Cleanest fit; SwiftUI does the interpolation. Springs native. |
| **Compose** (Android) | animate the modifier values via `animate*AsState` keyed on `trigger`; `Modifier.animateContentSize()` for layout | mark changed nodes animated for one recomposition; per-property `animate*AsState` | `AnimatedVisibility` around inserted/removed nodes | No global `withAnimation`; must animate per animatable property. Most work. Springs native. |
| **WinUI** (Windows) | implicit `Storyboard`/`DoubleAnimation` on the changed control; `ThemeTransition`s | apply patch, then run a storyboard over the deltas | `EntranceThemeTransition` / `RepositionThemeTransition` | Composition API for springs. Medium. |
| **Web/Blazor** | inline `transition: <props> <dur> <curve>` in `WebStyle` | add the transition CSS before applying the patch's inline-style changes | enter/leave CSS classes (FLIP for move) | Opacity/transform/color/size free via CSS. Enter/leave needs class toggling because Blazor removes DOM immediately. Spring→`cubic-bezier`. |
| **GTK** (Linux) | CSS `transition` (opacity/background/some size) via the existing CSS provider; else a `GLib` tick tween | tween changed animatable props over a frame callback | fade/scale via tick; layout move limited | No declarative animation engine. Start with opacity/color/CSS; expand later. Spring→easeInOut. |

**Initial support target (Phase 1–2):** the animatable set = **opacity, color, frame size, offset,
scale**. These interpolate cleanly on every backend. Rotation and complex path transitions come later.

## 8. Interplay with other milestones

- **Transitions (D) depend on keyed reconciliation** — without stable identity, an inserted list row is
  indistinguishable from "the child list changed", so we can't attach an enter transition. Sequence D
  after the keyed-`ForEach` work (already a listed next step). A/B/C do **not** depend on it.
- **Render batching (§6) composes with the DI proposal's dispatcher** — both funnel through
  `RequestRender`; do them consistently (coalesce, then marshal to the UI thread).
- **AOT/trim:** `AnimationSpec` is a `readonly record struct`; serialization stays in hand-rolled
  `NodeJson`. No reflection added.

## 9. Phased delivery

| Phase | Deliverable | Backends | Risk |
|-------|-------------|----------|------|
| **1** | `AnimationSpec`/`Anim`, animatable-modifier plumbing, **implicit `.Animation(spec, on:)`** for opacity/color/frame/offset/scale; wire `animation` modifier + `NodeJson` support. | SwiftUI + Web first (cheapest), then Compose/WinUI/GTK | Low |
| **2** | **Render batching** (§6) + **explicit `Animate.Run`** with the `Patch` `anim` tag; `withAnimation` on SwiftUI, per-property on the rest. | SwiftUI, Compose, Web; then WinUI/GTK | Med (batching touches `SwiftApp`/`State`) |
| **3** | **Transitions** `.Transition(...)` for insert/remove, on top of keyed reconciliation. | SwiftUI/Compose/Web, then WinUI/GTK | Higher; sequenced after keyed `ForEach` |

Phase 1 alone delivers "things animate." Phase 2 makes it idiomatic (one `withAnimation`, many changes).
Phase 3 covers enter/leave.

## 10. Worked example (end state)

```csharp
public sealed class Expander : View
{
    readonly State<bool> _open = State(false);

    public override View Body =>
        new VStack(
            new Button(_open.Value ? "Collapse" : "Expand",
                       () => Animate.Run(Anim.Spring(), () => _open.Value = !_open.Value)),

            new VStack(new Text("Hidden details…"))
                .Frame(height: _open.Value ? 200 : 0)
                .Opacity(_open.Value ? 1 : 0)
                .Animation(Anim.EaseInOut(0.25), on: _open.Value)     // implicit fallback for direct binders
        ).Spacing(12);
}
```

On iOS this animates via a real SwiftUI spring; on Web via CSS transitions; on Compose via
`animateContentSize` + `animateFloatAsState` — from the same C#.

## 11. Decisions needed

1. **Primary trigger model:** lead with **explicit `Animate.Run`** (best fit for the patch pipeline — one
   annotated diff animates everything) and offer `.Animation(on:)` as the implicit convenience? *Rec: yes —
   explicit is the natural match; implicit is sugar for people binding a single value.*
2. **Initial animatable set:** ship opacity/color/frame/offset/scale in Phase 1, defer rotation/blur/path?
   *Rec: yes, that set interpolates on every backend.*
3. **Spring fidelity:** require true springs (drops GTK/Web to approximation) or standardize on duration
   curves first and add springs per-backend later? *Rec: expose `Anim.Spring` now with documented
   degradation; don't block the curve-based path on it.*
4. **GTK depth:** ship CSS-transition-only animation for GTK initially (opacity/color/size), or invest in a
   `GLib` tick tweener up front? *Rec: CSS-only first; tweener is a later enhancement.*
```
