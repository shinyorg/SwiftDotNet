# Views & Controls

Every view is a C# object that lowers to a `Node`. Container views take their children as constructor
arguments; leaf views take their content. The same vocabulary renders on **every** backend — see
[Backends](backends/README.md) for how each maps to native controls.

The full control set is defined under [`src/SwiftDotNet/Core/Views`](../src/SwiftDotNet/Core/Views), and the
sample [`ContentView`](../sample/SharedUI/ContentView.cs) exercises all of it across a 5-tab tour.

## Layout

| View | Notes |
|------|-------|
| `VStack` / `HStack` / `ZStack` | Stacks. `.Spacing(n)`; cross-axis `.Alignment(…)` (see below). |
| `ScrollView` | Scrollable region. |
| `Grid` | Row/column grid. |
| `List` | Rows — expands into a full **[Collection View](collection-view.md)** (keyed identity, grid, selection, refresh, load-more). `List.ForEach` for keyed data. |
| `Form` / `Section` | Grouped settings-style layout. |
| `Group` | Transparent grouping. |
| `Spacer` / `Divider` | Flexible gap / separator line. |

**Cross-axis alignment:**

```csharp
new VStack(…).Alignment(HorizontalAlignment.Leading);
new HStack(…).Alignment(VerticalAlignment.Top);
new ZStack(…).Alignment(Alignment.TopTrailing);
```

## Navigation & presentation

| View | Notes |
|------|-------|
| `NavigationStack` + `NavigationLink` | Push navigation. |
| `TabView` + `Tab` | Tabbed UI. `.Paged()` turns it into a swipeable carousel with page dots; `.SelectedIndex(State<int>)` binds the selection two-way; `.HidePageIndicator()`. |
| `Sheet` | Modal presentation bound to a `State<bool>`. |
| `Alert` | Modal alert bound to a `State<bool>`. |
| `DisclosureGroup` | Expand/collapse section. |
| `Menu` | Popover menu of actions. |

## Inputs (two-way bound)

Every input binds to a `State<T>` and round-trips through the [event channel](architecture.md#diff-engine):

| View | Bound type |
|------|-----------|
| `TextField` | `State<string>` |
| `SecureField` | `State<string>` (masked) |
| `TextEditor` | `State<string>` (multi-line) |
| `Toggle` | `State<bool>` |
| `Slider` | `State<double>` |
| `Stepper` | `State<int>` (or numeric) |
| `Picker` | `State<T>` over options |
| `DatePicker` | `State<DateTime>` (crosses the bridge as Unix epoch seconds) |
| `ColorPicker` | `State<Color>` (crosses as a hex string) |

See **[State & Data Binding](state-and-binding.md)** for how the two-way sync works.

## Display

| View | Notes |
|------|-------|
| `Text` | Text run; `.Font(…)`, `.ForegroundColor(…)`. |
| `Label` | Text + SF Symbol icon. |
| `Image` | SF Symbols (mapped to emoji on backends without SF Symbols). |
| `ProgressView` | Determinate/indeterminate progress. |
| `Gauge` | Value gauge. |
| `Link` | Hyperlink. |
| Shapes | `Rectangle`, `Circle`, `Capsule`, `RoundedRectangle` — greedy (fill offered space unless `.Frame` overrides). |

## Colors & fonts

- Semantic colors: `Color.Primary`, `Color.Secondary`, …
- Hex: `Color.Hex("#7C4DFF")`
- Fonts: `Font.LargeTitle`, `Font.Body`, `Font.Caption`, …

## What's next

- Apply **[modifiers, gestures, and animation](modifiers-gestures-animation.md)** to any view.
- Set **[global styles](global-styles.md)** that cascade to descendants.
- Add your **[own controls](custom-controls.md)**.
