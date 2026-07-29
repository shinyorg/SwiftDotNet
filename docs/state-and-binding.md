# State & Data Binding

SwiftDotNet's state model **mirrors SwiftUI**. A view holds `State<T>`; assigning `.Value` invalidates the
view and schedules a re-render. The [diff engine](architecture.md#diff-engine) turns that render into a
minimal patch that reaches only the changed nodes.

`State<T>` is defined in [`src/SwiftDotNet/Core/State.cs`](../src/SwiftDotNet/Core/State.cs).

## Declaring state

Declare state as a field, created with the `State(...)` factory (a static helper on `View`):

```csharp
public sealed class ContentView : View
{
    readonly State<int> _count = State(0);          // mirrors @State private var count = 0
    readonly State<string> _name = State("");
    readonly State<bool> _isOn = State(false);

    public override View Body =>
        new VStack(
            new Text($"Count: {_count.Value}"),
            new Button("Increment", () => _count.Value++)
        );
}
```

Reading `.Value` inside `Body` reads the current value; writing `.Value` anywhere (a button action, a bound
control, an async callback) marks the view dirty.

## One-way vs. two-way binding

- **One-way (read):** `new Text($"Count: {_count.Value}")` — the text re-computes on the next render.
- **Two-way (read + write):** input controls take the `State<T>` itself and write back to it:

```csharp
new TextField("Name", _name),      // typing updates _name.Value
new Toggle("Enabled", _isOn),      // flipping updates _isOn.Value
new Slider(_volume, 0, 1),         // dragging updates _volume.Value
```

Two-way controls are **controlled components** on the backend: the native control keeps its own local state,
synced both directions. On SwiftUI/Compose that's an observable `@State`/`mutableStateOf` synced via
`onChange`; on the pure-C# backends the interpreter re-syncs the value on `updateProps` with an equality
guard to avoid feedback loops.

## The round-trip

```
State.Value = …
   │  invalidate → re-render Body
   ▼
ToNode() → TreeDiffer → Patch ──► backend applies patch ──► native UI updates
   ▲                                                          │  user interacts
   └── SwiftApp.OnEvent(nodeId, value) ◄──────────────────────┘  (tap / edit / toggle)
```

An event carries a **node id + optional value payload** (the TextField's text, `"true"`/`"false"` for a
Toggle, `null` for a Button). `SwiftApp.OnEvent` looks up the registered `Action`, runs it (which typically
writes a `State.Value`), and the cycle repeats. This channel is identical on every backend.

## Values across the bridge

Some types have a wire encoding worth knowing:

| Type | Wire form |
|------|-----------|
| `DateTime` (`DatePicker`) | Unix epoch **seconds** |
| `Color` (`ColorPicker`) | hex string (`"#RRGGBB"`) |
| `bool` (`Toggle`) | `"true"` / `"false"` |
| Button tap | `null` value |

## Host-pushed ambient state

Not every re-render starts from a `State<T>` you own. `SafeArea.Current` (iOS/Android) is pushed *by the
host* on a reserved event id and drives the same loop: a change stores the new insets and calls
`RequestRender`, so a `Body` reading it recomputes exactly as if you'd assigned to a state cell. An
unchanged report is dropped without scheduling anything. See
[Safe area](modifiers-gestures-animation.md#safe-area-ios--android-only).

## Collections

For list data, `List.ForEach(items, id, row)` provides **keyed identity** so reorders/insert/remove diff
cheaply instead of looking like N in-place updates. See **[Collection View](collection-view.md)**.

## Gotcha: a view that owns state must be *held*, not rebuilt

`Body` runs on **every render pass**, and a `View` written inline inside it is a **new object each pass** —
so its `State<T>` fields are new too, back at their initial values. A parent must therefore *hold* any child
that owns state:

```csharp
public sealed class RootView : View
{
    readonly ContentView _content = new();               // ✅ held — its State survives a render

    public override View Body => new OverlayHost(_content);
}

// ❌ Every render builds a fresh ContentView, so every State<T> on it resets to its initial value.
public override View Body => new OverlayHost(new ContentView());
```

The failure is **silent, and it looks like a host bug**: the event reaches C#, the handler runs and assigns
the state, a render is scheduled and runs — but it builds a fresh child, so the tree it produces is identical
to the previous one, the diff is empty, and *nothing at all* reaches the screen. Engine-local interactions
(a nav push, a tab switch) keep working, which makes it read as "the backend stopped repainting".

This is exactly how it was first mis-diagnosed — as a repaint defect in the
[Skia MAUI host](backends/skia.md#hosts) — when in fact it reproduced on every backend, including the
headless Skia harness. Views built inline are also why `OnCreated`/`OnAppearing` only fire for retained
views; see [Custom controls](custom-controls.md).

Stateless composites (`OverlayHost`, layout wrappers) are fine to rebuild. Pinned by
[`RetainedChildStateTests`](../tests/SwiftDotNet.Tests/RetainedChildStateTests.cs).

## Current limitations

- **Per-view local state:** child composite views don't retain local state across renders — a view that owns
  state must be held by whoever builds it (above). Lifting that restriction is the **view-instance
  reconciliation** milestone; see [`plans/README.md`](../plans/README.md).

Tracked in the **[Roadmap](roadmap.md)**.
