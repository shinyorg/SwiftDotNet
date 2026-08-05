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
| `Grid` | Row/column grid with per-track sizing and cell spans — see [Grid](#grid) below. |
| `AbsoluteLayout` | Children placed at coordinates you give them — see [AbsoluteLayout](#absolutelayout) below. |
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

## Grid

A two-dimensional grid. At its simplest it is *n* equal columns filled in reading order — SwiftUI's
`LazyVGrid` with flexible columns:

```csharp
new Grid(3, new Text("a"), new Text("b"), new Text("c")).Spacing(8);
```

Beyond that, columns and rows can be **sized individually** and children can **span** or be **pinned** to
a cell:

```csharp
new Grid(
        new Text("Profile").Font(Font.Headline).GridSpan(columns: 3),
        new Text("Name"), new TextField(_name), new Button("Save", Save),
        new Text("Bio"),  new TextEditor(_bio).GridSpan(rows: 2))
    .Columns(GridTrack.Fixed(80), GridTrack.Star(), GridTrack.Auto)
    .Rows(GridTrack.Auto, GridTrack.Fixed(44))
    .ColumnSpacing(12)
    .RowSpacing(6)
    .Alignment(Alignment.Leading);
```

### Track sizing

[`GridTrack`](../src/SwiftDotNet/Core/GridLayout.cs) mirrors SwiftUI's `GridItem` cases with WPF/MAUI's
star weights spelled out:

| Factory | Sizes the track to |
|---|---|
| `GridTrack.Auto` | its largest child |
| `GridTrack.Fixed(80)` | exactly 80 points |
| `GridTrack.Star()` / `GridTrack.Star(2)` | 1 (or 2) shares of what's left after Fixed/Auto |
| `GridTrack.Flexible(min: 40, max: 120)` | its content, clamped into `[40, 120]` |

`.Columns(...)` replaces the constructor's column count. Rows you don't declare are `Auto`, so a grid is
as tall as it needs to be rather than as tall as it's offered.

### Placement

Children flow left-to-right, top-to-bottom into the first free cell their whole span fits in.
`.GridSpan(columns:, rows:)` widens a child; `.GridCell(column:, row:)` pins one, and the rest flow
around it. Both are documented with the other
[modifiers](modifiers-gestures-animation.md#grid-and-absolute-placement).

### Per-backend behavior

| Backend | Lowers to | Notes |
|---|---|---|
| Apple (SwiftUI) | custom `Layout` (`SDNGridLayout`) | Full support. Not `LazyVGrid` — it has no span or explicit-cell concept. |
| Android (Compose) | custom `Layout` | Full support, same two-pass algorithm. |
| Skia | `MeasureGrid`/`ArrangeGrid` | Full support; the reference implementation. |
| Windows (WinUI 3) | `Grid` + Column/RowDefinitions | Near-native fit. `Flexible`'s max lands on `MaxWidth`/`MaxHeight`. |
| Terminal | `XenoAtom.Terminal.UI.Grid` | Full support, but sizes are rounded to whole **character cells**. |
| Web | CSS Grid | Full support. Placement is still resolved in C# rather than left to CSS auto-placement, so a pin lands in the same cell everywhere. |
| Linux (GTK4) | `Gtk.Grid` | Spans and pins map directly. **Tracks are approximated:** GTK has no column definitions, so `Fixed`/`Flexible` become width requests, `Star` becomes `Hexpand`, and `Flexible`'s **maximum is dropped**. An all-equal-star grid additionally goes `ColumnHomogeneous`. |

### Gotchas

- **Track sizing runs in two measure passes.** A child is measured naturally to size the content-driven
  columns, then re-measured at its final cell width — otherwise wrapping `Text` reports a height for the
  wrong width.
- **A spanning child doesn't drive `Auto` sizing directly.** If it doesn't fit its span, the shortfall is
  added to the *last* content-sized track it covers, so a wide header stretches the end of its span rather
  than only column 0. A span that crosses a `Star` track is skipped entirely — the star pass already hands
  it the leftover, and growing a content-sized track instead would *steal* it. That distinction matters
  because shapes and raster images are greedy (they measure as the full width offered), so a spanning one
  would otherwise collapse every star column in its span to nothing.
- `Grid` and `List.Columns(n)` are different things. `List`'s grid mode
  ([Collection View](collection-view.md#grid--horizontal)) is uniform but **virtualized** — prefer it for
  large data sets; `Grid` lays out all its children.

**Status:** ✅ Verified on Skia (`SkiaGridTrackTests`) and the terminal (`TuiGridAbsoluteTests`); the
backend-independent engine is covered by `GridEngineTests`. Apple and Android typecheck/compile but the
new layouts are not yet visually verified; Windows has never been compiled.

## AbsoluteLayout

Positions each child at coordinates you give it instead of flowing them — MAUI's `AbsoluteLayout`.
SwiftUI has no equivalent, so on Apple it lowers to a custom `Layout`.

```csharp
new AbsoluteLayout(
        new Rectangle().ForegroundColor(Color.Blue)
            .LayoutBounds(0, 0, 1, 1, LayoutFlags.SizeProportional),   // fills the layout
        new Text("12").LayoutBounds(12, 12),                           // 12pt in from the top-left
        new Button("Close", Close)
            .LayoutBounds(1, 0, 80, 32, LayoutFlags.XProportional))     // flush right, 80×32
    .Frame(height: 200);
```

- **Points by default.** `.LayoutBounds(x, y)` places the top-left corner and lets the child size itself;
  the four-argument overload adds an explicit size. Pass `AbsoluteLayout.AutoSize` for an axis the child
  should still decide.
- **`LayoutFlags` makes any of x/y/width/height a fraction** of the layout's own size. A proportional
  *size* is a straight fraction. A proportional *position* is an anchor across the free space — `0` is
  flush leading, `1` flush trailing, `0.5` centered — which is MAUI's rule and the only one where `1`
  stays on screen.

### Per-backend support

| Backend | Lowers to | Notes |
|---|---|---|
| Apple (SwiftUI) | custom `Layout` (`SDNAbsoluteLayout`) | Full support including proportional bounds. |
| Android (Compose) | custom `Layout` | Full support. |
| Skia | `MeasureAbsolute`/`ArrangeAbsolute` | Full support; the reference implementation. |
| Terminal | `TuiAbsolute` (a real `Visual`) | Full support, rounded to whole character cells. |
| Web | `position:absolute` in a `position:relative` box | Full support. A proportional position is `left:x%` + `translateX(-x*100%)`, which works out to the same anchor rule. |
| Linux (GTK4) | `Gtk.Fixed` | Point bounds are exact. Proportional bounds are re-resolved from a frame tick callback (GTK4 has no size-allocate signal), installed only when a child actually asks for one. |
| Windows (WinUI 3) | `Canvas` + `Canvas.SetLeft/SetTop` | Proportional bounds recompute on `SizeChanged`. |

### Gotchas

- **Give it a height.** An `AbsoluteLayout` claims the box it's offered, because a fraction needs
  something to be a fraction of. Inside a scrolling stack that's unbounded vertically, so add
  `.Frame(height: …)` — on Web a percentage height needs a definite parent height for the same reason.
- **A child with no `.LayoutBounds` sits at the origin** at its natural size, so a forgotten call shows up
  instead of vanishing.
- `.LayoutBounds` is ignored by every other container; it only means something to an `AbsoluteLayout`.

**Status:** ✅ Verified on Skia (`SkiaAbsoluteLayoutTests`) and the terminal (`TuiGridAbsoluteTests`);
bounds math covered by `GridEngineTests`. Apple and Android typecheck/compile but are not yet visually
verified; GTK is unverified (no GTK runtime in CI) and Windows has never been compiled.

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

**Skia note.** The self-drawing backend has no OS pickers, so it draws its own. `Slider` scrubs under a
finger; `ColorPicker` opens a swatch popover over a fixed 7-colour palette (the system pickers on the
native backends give you the full spectrum). `Stepper`, `Picker` and `DatePicker` are engine-drawn and
tap-driven. See [Skia backend](backends/skia.md#what-a-finger-needs-that-a-mouse-got-for-free).

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
