# Terminal / TUI (XenoAtom.Terminal.UI)

The same C# view tree, rendered as **characters in a terminal** — over SSH, in a container, in CI, on any
box with a TTY and no display server.

[XenoAtom.Terminal.UI](https://xenoatom.github.io/terminal/) is a retained-mode, bindable widget toolkit
(two-pass Measure/Arrange layout, routed events, themes, focus) rather than a compiler-plugin framework,
so it takes the repo's **pure-C# interpreter** route — the same shape as [GTK](linux-gtk.md), no native
shim. A retained visual tree is keyed by node path and the same `replace`/`updateProps`/`setChildren`
patches are applied to live `Visual`s; terminal input calls back into C#.

- **Verified** headlessly on macOS: the full [`ContentView`](../../sample/SharedUI/ContentView.cs) builds
  and renders through Terminal.UI's real layout and render pipeline, and **35 tests**
  ([`TuiNodeMappingTests`](../../tests/SwiftDotNet.Tests/TuiNodeMappingTests.cs) and friends) run in CI.
  Not yet driven by hand in an interactive terminal — keyboard focus order, mouse reporting and the
  alternate-screen lifecycle are exercised by the framework, not by us.

## Project layout

Two projects, because real pixel images cost a lot of payload:

| Project | Depends on | What it gives you |
|---|---|---|
| [`src/SwiftDotNet.Tui`](../../src/SwiftDotNet.Tui) | `XenoAtom.Terminal.UI` only | The backend. Images render as **character art**. |
| [`src/SwiftDotNet.Tui.Graphics`](../../src/SwiftDotNet.Tui.Graphics) | + `XenoAtom.Terminal.UI.Graphics` | Real **Sixel / Kitty / iTerm2** images, and JPEG/WebP/GIF decode. |

The split is deliberate: `XenoAtom.Terminal.UI.Graphics` pulls **SkiaSharp plus native assets for all
three desktop RIDs**, which no terminal app should pay for unless it asks. See
[Images](#images--character-art) below.

| File | Role |
|------|------|
| `TuiBridge.cs` | `IBridge`; parses patch JSON, applies to the visual tree by path id; `Emit(id,value)`. |
| `TuiNode.cs` | Holds a `Visual`; `CreateVisual` / `UpdateProps` / `SetChildren` / `Adopt`; the full vocabulary map. |
| `TuiVisuals.cs` | `TuiSurface` (the per-node modifier wrapper), `TuiSpacer`, `TuiShape`. |
| `TuiStyle.cs` | Token → `Color`/`Align`/`TextStyle`; the pixel→cell conversion. |
| `TuiRenderers.cs` | Custom-renderer registry (see [Custom Controls](../custom-controls.md)). |
| `TuiAsciiArt.cs` | RGBA → cells: half-block / quadrant / ASCII ramp. |
| `TuiPngDecoder.cs` | Pure-managed PNG → RGBA, so the core needs no image library. |
| `TuiImage.cs` | The `Image` node: source precedence, async URL fetch, the art visual. |
| `SwiftDotNetHost.cs` | `TerminalApp` + the `SynchronizationContext` adapter. |

## Control map

| Node | Terminal.UI control |
|---|---|
| `Text` | `TextBlock` (wrapping) |
| `Label` | `HStack` of glyph + `TextBlock` |
| `Button` | `Button` |
| `Link` / `WebView` | `Link` (an OSC-8 hyperlink) |
| `VStack` `HStack` `ZStack` `Group` | `VStack` / `HStack` / `ZStack` |
| `ScrollView` `Form` | `ScrollViewer` over a stack |
| `Grid` | `Grid` with real Column/RowDefinitions + `GridCell` spans |
| `AbsoluteLayout` | `TuiAbsolute` — a purpose-written `Visual` |
| `List` | `ScrollViewer` over a stack of rows (grid and horizontal variants supported) |
| `Section` | `Group` — a captioned box |
| `DisclosureGroup` | `Collapsible` |
| `TabView` / `Tab` | `TabControl` / `TabPage` |
| `Menu` | `Button` + `Popup` |
| `TextField` / `SecureField` / `TextEditor` | `TextBox` / `TextBox{IsPassword}` / `TextArea` |
| `Toggle` | `Switch` (the label rides inside it, so the whole row is one focus stop) |
| `Slider` | `Slider<double>` |
| `Stepper` | `HStack` of − / value / + buttons |
| `Picker` | `Select<string>` |
| `DatePicker` | `TextBox` accepting `yyyy-MM-dd` |
| `ColorPicker` | `ColorPicker` with its palette shown |
| `NavigationStack` / `NavigationLink` | `DockLayout` (title bar + `ContentSwitcher`); Esc or Back pops |
| `Sheet` / `Alert` | `Dialog` in the root `WindowLayer` |
| `ProgressView` | `ProgressBar`, or `Spinner` when indeterminate |
| `Gauge` | `ProgressBar`, normalised across min…max |
| `Divider` | `Rule` |
| `Spacer` | `TuiSpacer` — flex-grow 1 on both axes |
| `Rectangle` `Circle` `Capsule` `RoundedRectangle` | `TuiShape`, filled from `.ForegroundColor` |
| `Image` | Character art, or a real image with [Tui.Graphics](#images--character-art) |
| *unknown* | [`TuiRenderers`](../custom-controls.md) registry, else `⚠ {type}` |

**`Grid` is a near-complete fit.** Terminal.UI's own `Grid` already models column/row definitions and cell
spans, so tracks, `.GridSpan` and `.GridCell` map almost directly — `Auto`/`Fixed`/`Star` become
`GridLength.Auto`/`Fixed`/`Star` and `Flexible`'s bounds land on the definition's Min/Max. The lossy step is
the unit: sizes go through `TuiStyle.Cols`/`Rows` and land on whole cells.

**`AbsoluteLayout` is `TuiAbsolute`**, written as a `Visual` rather than assembled from a ZStack plus
margins: proportional bounds need the panel's *final* rect, which only `ArrangeCore` knows, and a margin
computed at build time would be stale the moment the terminal is resized. When an axis is offered unbounded
constraints the panel falls back to the far edge of its point-placed children, so it isn't measured away to
nothing inside a scrolling column.

## Modifiers

A terminal's unit is the **character cell**, not the pixel, so every geometric modifier passes through
`TuiStyle.Cols` / `TuiStyle.Rows`. The default divisor is 8px per column and 16px per row — assign
`TuiStyle.CellWidthPx` / `CellHeightPx` before the first render to make layouts denser or sparser.

| Modifier | Terminal behaviour |
|---|---|
| `.Padding` | Cells on the surface wrapper |
| `.Background` | Cell background fill |
| `.Border` | A box drawn in the theme's line glyphs, insetting content by one cell |
| `.ForegroundColor` | Text colour — or the **fill** on a shape (the convention the [Controls library](../controls-library.md) relies on) |
| `.Font` | Emphasis only: headings → bold, captions → dim. A terminal has one glyph size. |
| `.Frame` | `Min`/`MaxWidth`/`Height` in cells |
| `.Align` | `HorizontalAlignment` |
| `.Disabled` | `IsEnabled` |
| `.Opacity` | Blended into the colour itself — cells have no alpha. Below 0.05 the node is hidden. |
| `.OnTapGesture` | Pointer press (needs mouse reporting) **and** Enter when focused |
| **No-ops** | `.CornerRadius`, `.Offset`, `.ScaleEffect`, `.Rotation`, `.Shadow`, `.Material` (flat tint instead), `.Animation`, `.OnLongPress`, `.OnSwipe`, `.OnDrag`, `.OnMagnify` |

Gradients collapse to their **first stop** — a cell has one background colour.

## Images → character art

Without [`SwiftDotNet.Tui.Graphics`](../../src/SwiftDotNet.Tui.Graphics), an `Image` is drawn as
characters. Three modes, chosen from the terminal's colour support unless you pin one:

| Mode | How | Best for |
|---|---|---|
| **HalfBlock** (default with colour) | One `▀` per cell: foreground = upper half-pixel, background = lower. Doubles vertical resolution at no cost and keeps full colour. | Everything, especially photographs |
| **Quadrant** | 2×2 sub-cell sampling onto `▘▝▀▖▌▞▛▗▚▐▜▄▙▟█`, split around the cell's mean luminance. Doubles horizontal resolution but each cell still carries only two colours. | Line art, logos, diagrams |
| **Ascii** | Luminance onto the ramp `" .:-=+*#%@"`. | Monochrome terminals, `TERM=dumb`, logs |

```csharp
TuiImageOptions.Mode = TuiImageMode.Quadrant;   // default is Auto
TuiImageOptions.DefaultColumns = 48;            // width when no .Frame pins one
```

Downsampling is a **box average**, not nearest-neighbour: at 20–60 columns nearest-neighbour throws away
most of the source and aliases badly. Alpha is composited against the theme background before quantising,
since a cell cannot be partly transparent. Aspect ratio is corrected for the ~2:1 cell, so a square image
comes out square.

Decoding goes through `ITuiImageDecoder`. The core ships `TuiPngDecoder` — hand-rolled over
`System.IO.Compression`, covering bit depths 1/2/4/8/16 and all five colour types with `tRNS`, which keeps
the backend dependency-free and trim/AOT-safe (the same reasoning as
[`NodeJson.cs`](../../src/SwiftDotNet/Core/NodeJson.cs)). Adam7-interlaced PNGs are **rejected** rather
than half-decoded; the image falls back to alt text. Register your own for other formats:

```csharp
TuiImageDecoders.Register(new MyJpegDecoder());
```

### Real pixel images

Reference `SwiftDotNet.Tui.Graphics` and call it once before the host:

```csharp
TuiGraphics.Enable();
return SwiftDotNetHost.Run(new ContentView());
```

That installs the Sixel/Kitty/iTerm2 presenter and a Skia-backed decoder (adding JPEG/WebP/GIF to *both*
paths). Image nodes become a real `Image` visual whose `FallbackContent` is the character-art visual, so a
terminal without graphics support degrades on its own. Remote URLs stay on the art path — they are fetched
asynchronously by the core backend and have no synchronous source to hand the presenter.

## Running

```bash
dotnet run --project sample/SampleApp.Tui
```

| Env var | Effect |
|---|---|
| `SDN_TUI_GRAPHICS=1` | Real Sixel/Kitty images instead of art (needs iTerm2, kitty, WezTerm…) |
| `SDN_TUI_IMAGE_MODE=halfblock\|quadrant\|ascii` | Pin one art mode instead of auto-detecting |

Host options are a SwiftDotNet type, not a Terminal.UI one — see the namespace gotcha below:

```csharp
SwiftDotNetHost.Run(root, services, new TuiHostOptions
{
    Fullscreen = false,       // render inline under the shell prompt instead
    EnableMouse = false,      // leave the terminal's own text selection alone
    Configure = o => o.ExitGesture = null,   // escape hatch onto TerminalAppOptions
});
```

## Gotchas

- **The two libraries share names.** `State<T>`, `Color`, `Style`, `Theme`, `Brush`, `VStack`, `Button`,
  `Grid`, `Slider`, `Link`, `Rectangle` all exist in *both* the SwiftDotNet DSL and Terminal.UI. Never
  `using XenoAtom.Terminal.UI;` in app code — every `State<int>` in your own views becomes ambiguous.
  Import the narrowest namespace a custom renderer needs (usually `XenoAtom.Terminal.UI.Controls`), or
  fully qualify. Inside the backend, `TuiNode.cs` aliases the terminal side with a `T` prefix.
- **`Slider<T>` clamps on every write to `Value`, against whatever `Minimum`/`Maximum`/`Step` are set at
  that moment** — and its defaults are a 0–10 range with a step of 1. Set range, then step, then value.
  The three-argument constructor is unusable for this reason: it applies `Value` before `Maximum`, so
  `new Slider<double>(0, 100, 42)` yields **10**.
- **Every node is wrapped in a `TuiSurface`.** Terminal.UI's `Border` always costs a cell on all four
  edges, so it can't be the wrapper — a `.Border()` added by a later state change would need the tree
  restructured, and the wire reports that as an `updateProps`, never a `replace`. One always-present,
  fully-mutable surface per node is what makes dynamic modifiers work.
- **The surface inherits its content's alignment.** Without it, a container that asked to stretch (a
  `Form`, a `Section`, a `List`) gets shrink-wrapped by its own wrapper and every row inside is measured
  against the narrowest sibling. Covered by
  [`TuiLayoutSnapshotTests`](../../tests/SwiftDotNet.Tests/TuiLayoutSnapshotTests.cs).
- **A `Spacer` only expands if its stack is greedy.** Terminal.UI resolves size by alignment before flex
  gets a say, so `Stack()` checks for a direct `Spacer` child and stretches the stack on its layout axis —
  SwiftUI's own rule, restored explicitly.
- **`NavigationLink` rows centre their label.** Terminal.UI's `Button` centres its content regardless of
  the content's alignment, so a `Spacer` inside one collapses and the chevron sits beside the label rather
  than pinned right. Keeping `Button` is the right trade: correct focus and keyboard activation matter far
  more in a terminal than chevron placement.
- **Patches mutate plain properties, which the framework cannot observe**, so `TuiBridge.Render` ends every
  applied patch with an explicit `TerminalApp.RequestFullRender()`.
- **`SwiftApp` captures `SynchronizationContext.Current`**, and Terminal.UI schedules through its own
  `Dispatcher` instead. `SwiftDotNetHost` installs an adapter **before** calling `SwiftApp.Run`; installing
  it afterwards would leave renders posting to the console app's original context and mutating visuals off
  the dispatcher thread.
- **Two-way controls are watched, not evented.** `TextBox`/`TextArea` expose no text-changed event, so the
  backend reads their value inside `Visual.RegisterDynamicUpdate` — the framework tracks the read and
  re-runs the callback exactly when the value moves. An echo guard stops our own `updateProps` write from
  being reported back as a user edit.
- **Keyed row recycling re-stamps ids.** A recycled row that moved keeps its live control but must adopt
  its new structural path, or `Emit` routes its events to whatever item now sits where it used to be. See
  `TuiNode.Adopt` and [`TuiKeyedReconcileTests`](../../tests/SwiftDotNet.Tests/TuiKeyedReconcileTests.cs) —
  the same contract [Skia](skia.md) locks in.

## Terminal-only controls

Terminal.UI ships controls the DSL has no node type for — `Table`, `DataGridControl`, `TreeView`,
`CodeEditor`, `MarkdownControl`, `BarChart`, `LineChart`, `Sparkline`, `TextFiglet`, `CommandPalette`. Reach
them through the [custom-control seam](../custom-controls.md):

```csharp
TuiRenderers.Register("Sparkline", ctx =>
    new XenoAtom.Terminal.UI.Controls.Sparkline(ParseValues(ctx.String("values"))));
```

## Testing

Unlike GTK and Web, this backend is **genuinely testable on CI**: `TuiBridge` builds and patches its
retained tree with no `TerminalApp` and no TTY, and `VisualSnapshotRenderer.Render(visual, w, h, theme)`
runs the whole layout and render pipeline into a `CellBuffer` you can read back as markup. See
[`TuiTestHost`](../../tests/SwiftDotNet.Tests/TuiTestHost.cs) — note the manual pump, without which xUnit's
own `SynchronizationContext` would race every state change.

## Related

- [Backends overview](README.md) · [Custom controls](../custom-controls.md) ·
  [Views & controls](../views-and-controls.md) · [Modifiers](../modifiers-gestures-animation.md)
