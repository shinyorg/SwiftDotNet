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

## Current limitations

- **Render batching:** writing `State.Value` currently renders immediately per set (a batching pass is
  planned — it's a prerequisite for explicit animation transactions).
- **Per-view local state:** child composite views don't yet retain local state across renders — that waits on
  the per-view reconciliation milestone.

Both are tracked in the **[Roadmap](roadmap.md)**.
