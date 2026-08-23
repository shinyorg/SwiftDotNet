# Windows / WPF

WPF is a bindable, retained-mode toolkit — **not** a compiler-plugin framework like SwiftUI/Compose — so
Windows Presentation Foundation uses the same **pure-C# "translate to controls"** route as
[GTK](linux-gtk.md) and [WinUI 3](windows.md): **no native shim**. A retained-mode interpreter maps the node
tree to real `System.Windows.Controls` elements keyed by node path and applies the same
`replace`/`updateProps`/`setChildren` patches; WPF events call straight back into C#.

There are **two** ways to put SwiftDotNet on WPF, and they are different products:

| | What you get | Use it when |
|---|---|---|
| **This backend** (`SwiftDotNet.Wpf`) | Real WPF controls — Win32 look, WPF theming, UI Automation accessibility | You want the app to *be* a WPF app |
| **[Skia on WPF](skia.md)** (`SwiftDotNet.Skia.Wpf`) | One self-drawn canvas, pixel-identical to every other Skia head, complete feature set | You want the uniform look, or a feature this backend degrades |

> **Status: 🧩 Scaffolded — compiles clean, never run.** Every project here builds on macOS (via
> `EnableWindowsTargeting`) and on a `windows-latest` runner in CI, so unlike the [WinUI 3
> backend](windows.md) nothing on this page is un-compiled. But the repo's dev machine is macOS: **no
> window has ever been opened**. Treat runtime behaviour as *intended*, not verified. There are no WPF
> rendering tests — a WPF project targets `net10.0-windows`, which the `net10.0` test project cannot
> reference — so CI covers compilation only.

## Project layout

[`src/SwiftDotNet.Wpf`](../../src/SwiftDotNet.Wpf) (`net10.0-windows`, `UseWPF`) — a **separate** project
for the same reason GTK is: a Windows-only TFM inside the multi-target library would force its dependency
on every consumer.

| File | Role |
|------|------|
| [`WpfBridge.cs`](../../src/SwiftDotNet.Wpf/WpfBridge.cs) | `IBridge`; `Host` is a `Grid` (content + dialog overlays); applies patches to the element tree. |
| [`WpfNode.cs`](../../src/SwiftDotNet.Wpf/WpfNode.cs) | Node → WPF; an `Element`/`Inner` split so modifiers wrap in a `Border`. |
| [`WpfStyle.cs`](../../src/SwiftDotNet.Wpf/WpfStyle.cs) | Color / font / gradient / curve / emoji tokens. |
| [`WpfNavController.cs`](../../src/SwiftDotNet.Wpf/WpfNavController.cs) | Header + content stack for `NavigationStack`/`NavigationLink`. |
| [`WpfRenderers.cs`](../../src/SwiftDotNet.Wpf/WpfRenderers.cs) | Custom-renderer registry. |
| [`SwiftDotNetWpfHost.cs`](../../src/SwiftDotNet.Wpf/SwiftDotNetWpfHost.cs) | `CreateRootElement(View) → UIElement`. |
| [`SwiftDotNetWpfApplication.cs`](../../src/SwiftDotNet.Wpf/SwiftDotNetWpfApplication.cs) | Reusable `Application` base. |

## How to use it

```csharp
// Program.cs — the whole head.
static class Program
{
    [STAThread]                                   // WPF requires an STA thread
    static void Main() => new SampleApplication().Run();
}

sealed class SampleApplication : SwiftDotNetWpfApplication
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
    protected override string WindowTitle => "SwiftDotNet · WPF";
}
```

Or drop the tree into a window you already own:

```csharp
window.Content = SwiftDotNetWpfHost.CreateRootElement(new ContentView());
```

See [`sample/SampleApp.Wpf`](../../sample/SampleApp.Wpf) for the head this is taken from, including a custom
native primitive registered through `WpfRenderers`.

## Widget map

`Text`→`TextBlock`, `Button`→`Button`, `V/HStack`→`StackPanel`, `ZStack`→`Grid`, `ScrollView`→`ScrollViewer`,
`Grid`→`Grid` (Column/RowDefinitions + `SetColumnSpan`/`SetRowSpan`), `AbsoluteLayout`→`Canvas`,
`List`→`Border`+`StackPanel`, `Form`→`ScrollViewer`+`StackPanel`, `DisclosureGroup`→`Expander`,
`TabView`→`TabControl`, `Menu`→`Button`+`ContextMenu`, `TextField`→`TextBox`, `SecureField`→`PasswordBox`,
`TextEditor`→`TextBox` (`AcceptsReturn`), `Toggle`→`CheckBox`, `Slider`→`Slider`,
`Stepper`→`RepeatButton` pair, `Picker`→`ComboBox`, `DatePicker`→`DatePicker`, `ColorPicker`→swatch
`Button`+`Popup` palette, `NavigationStack`→a `WpfNavController`, `Sheet`/`Alert`/`ActionSheet`→a scrim +
card overlay layer, `ProgressView`/`Gauge`→`ProgressBar`, `Link`→`Hyperlink` in a `TextBlock`,
`WebView`→`WebView2`, `Image`→`Image`+`BitmapImage`, shapes→`Rectangle`/`Ellipse`.

### Where WPF has no equivalent, and what it got instead

WinUI ships several controls WPF does not. None of them are dropped; each is built from WPF parts.

| DSL view | WinUI | WPF | Consequence |
|---|---|---|---|
| `Toggle` | `ToggleSwitch` | `CheckBox` | Different chrome; identical behaviour. |
| `Stepper` | `NumberBox` | `TextBlock` + two `RepeatButton`s | No typed entry — hold to repeat, as a spin box does. |
| `ProgressView` (indeterminate) | `ProgressRing` | indeterminate `ProgressBar` | A bar, not a ring. |
| `Link` | `HyperlinkButton` | `Hyperlink` | WPF does **not** launch the browser for you; the backend calls `Process.Start(… UseShellExecute = true)`. |
| `ColorPicker` | in-box `ColorPicker` | swatch + `Popup` palette | **Palette only.** Deliberate: the wire vocabulary is named tokens and `#rrggbb`, so a full HSV wheel would offer colours the DSL cannot round-trip. |
| `Sheet` / `Alert` / `ActionSheet` | `ContentDialog` | scrim + card overlay in the host `Grid` | See the gotcha below — and note this backend has **no three-button limit**, unlike the WinUI one. |
| `WebView` | in-box `WebView2` | `Microsoft.Web.WebView2` package | Needs the Evergreen WebView2 runtime (present on Windows 11 and current Windows 10). |

## Modifiers

The universal modifier pass maps almost completely, and two modifiers land *better* here than on WinUI:

- **`.Shadow` uses a real `DropShadowEffect`**, which takes blur/offset/colour directly and derives the
  silhouette from the content's alpha. A rounded or non-rectangular element casts the **right shape**,
  matching Web and Skia — where the [WinUI backend](windows.md)'s Composition sprite always casts a
  rectangle. No wrapper element is needed at all. The offset is polar in WPF (`Direction` counter-clockwise
  from +x with y pointing *up*), so the wire's `dy` is negated.
- **`.Disabled` is `UIElement.IsEnabled`**, which disables the whole subtree and greys native controls —
  no `Control`/non-`Control` split like the WinUI port needs.
- **`.Keyframes(…)`** runs as WPF `DoubleAnimationUsingKeyFrames` clocks applied straight to properties via
  `IAnimatable.BeginAnimation`, with `SplineDoubleKeyFrame` per stop so per-segment easing survives. There
  is deliberately **no `Storyboard`**: a storyboard would have to find the transform objects through a name
  scope, and `BeginAnimation` does not. `Width`/`Height` need no opt-in here either (WinUI's
  `EnableDependentAnimation`) — WPF has no compositor/dependent-animation split.
- **`.Material` is a translucent tint, not real acrylic.** Same fallback as GTK and WinUI; real acrylic
  needs DWM composition interop.
- **`.Animation` is a no-op.** WPF has no implicit layout-transition system (WinUI's
  `RepositionThemeTransition`, Compose's `animateContentSize`), so an element that moves or resizes because
  the tree changed jumps rather than sliding. Explicit `.Keyframes(…)` timelines are unaffected — those are
  real animations.

## Gestures

WPF raises manipulation events for **touch only**, and only after `IsManipulationEnabled` — a mouse never
produces them. So every continuous gesture is resolved from raw mouse events instead of the
`ManipulationDelta` route the WinUI backend uses:

| Gesture | How |
|---|---|
| `.OnTapGesture` | `MouseLeftButtonUp`, matched against `e.ClickCount` for the double-tap variant. |
| `.OnLongPress` | A `DispatcherTimer` started on button-down, cancelled by an early release or by leaving the 8px tap slop. `MouseRightButtonUp` also fires it. |
| `.OnSwipe` | Captured drag; direction and a 40px threshold decided on release. |
| `.OnDrag` | Captured drag emitting the `<phase>;tx,ty;lx,ly;vx,vy` wire form. Velocity is estimated from the last two samples, since WPF reports none. |
| `.OnMagnify` | **Ctrl + mouse wheel** — the desktop zoom convention, matching the Silk and Skia desktop heads. A precision touchpad delivers ctrl+wheel for its own pinch, so real pinch hardware works through the same path. |

## Gotchas

- **Core's type names shadow the WPF ones.** `Platforms`-style aliasing is mandatory here, exactly as on
  [WinUI](windows.md#known-blocker-core-type-names-shadow-the-winui-ones): this project compiles into the
  `SwiftDotNet` namespace, where Core declares `Grid`, `Button`, `Image`, `Slider`, `Rectangle`,
  `DatePicker`, `Brush`, `Color`, `GradientStop`, `Form`, `Label`, `Menu`, `List` … plus the enums
  `HorizontalAlignment`/`VerticalAlignment`. A simple name binds to the enclosing namespace's member first,
  so every WPF type is reached through a **renamed** alias (`WpfGrid`, `WpfButton`, …). A plain
  `using Grid = System.Windows.Controls.Grid;` does **not** work — that alias itself collides with the
  namespace member (CS0576). Core's `Form` is additionally `sealed`, so `: Form` fails with CS0509 rather
  than an ambiguity.
- **Dialogs are overlays, not `Window.ShowDialog()`.** A modal `Window` blocks its caller until dismissed,
  and the caller here is `Render()`, part-way through applying a patch — the render loop would stall and
  re-enter. So `WpfBridge.Host` is a `Grid` and a presented dialog is stacked into the same cell as a
  dimmed scrim plus a card. Scrim click and <kbd>Esc</kbd> both report `"false"`, the wire's own
  dismissed-without-choosing token.
- **`StackPanel` has no `Spacing`** (WinUI's does), so `.Spacing(n)` becomes a leading `Margin` on every
  child after the first. It is assigned *absolutely*, never added — otherwise a `setChildren` patch would
  compound it on every re-lay. Nothing else in this backend writes `Margin`; padding goes on a wrapper
  `Border`. `Grid` has no `ColumnSpacing`/`RowSpacing` either, so gutters are a margin on every cell not in
  the first column/row.
- **A `Border` does not clip its child to `CornerRadius`.** WinUI's does. Without `ClipToBounds`, a rounded
  background is drawn under square content and the corners look filled in — so the modifier wrapper sets it
  whenever a corner radius is present.
- **A `Panel`, `Border` or `Control` with a null `Background` is invisible to hit-testing.** Mouse events
  pass straight through it. Every gesture modifier therefore forces a transparent background on the element
  it attaches to, and unselected `List` rows get one too or they simply would not be clickable.
- **`TabControl.SelectionChanged` is a bubbling routed event.** A `ComboBox` or `ListBox` *inside* a tab
  raises it on the `TabControl` as well, so without an `e.OriginalSource` guard, changing a `Picker` is
  reported as a tab change and knocks the app to whatever index the picker selected.
- **WPF forbids two logical parents.** A keyed row reused across a `setChildren` patch, and a
  `NavigationLink` destination pushed onto the nav stack, are both still parented where they were built —
  so each is explicitly detached before being re-adopted. Adding an already-parented element throws.
- **`PasswordBox` gets no placeholder.** `TextField`'s watermark is a hit-test-invisible `TextBlock` layered
  over the `TextBox` in a `Grid` (WPF has no placeholder property at all). That trick cannot be layered the
  same way over `PasswordBox`, whose value is a plain CLR property rather than a DependencyProperty, so the
  placeholder is dropped rather than faked.
- **`InputScope` steers the touch keyboard only.** The F9 `keyboard` prop maps to a WPF `InputScope`, which
  affects the tablet input panel and handwriting recognition — it does **not** restrict what a physical
  keyboard can type. Core's binding is what keeps a numeric field numeric. The F9 `returnKey` prop has no
  WPF equivalent at all and is ignored rather than faked.
- **`Spacer` in a `StackPanel` contributes nothing** — a StackPanel gives every child its desired size along
  the stack axis. Shared with the WinUI backend; it works inside a `ZStack` or a `Grid` cell. The
  [Skia backend](skia.md) implements the real SwiftUI semantics.
- **Do not raise the target platform version to `10.0.19041`.** That TFM is also compatible with
  SwiftDotNet's `net10.0-windows10.0.19041.0` asset, which carries the **WinUI 3** backend — so the
  compilation would contain two backends at once. This is also why the entry points are named
  `SwiftDotNetWpfHost` / `SwiftDotNetWpfApplication` rather than the `SwiftDotNetHost` /
  `SwiftDotNetApplication` the GTK and WinUI backends use: raising the TPV is a common thing for a WPF app
  to do (it is how one reaches WinRT APIs), and identical names in one namespace would be ambiguous.

## Running

```bash
dotnet run --project sample/SampleApp.Wpf     # Windows only
```

It **builds** anywhere — `EnableWindowsTargeting` pulls the Windows targeting packs on macOS and Linux — so
a break is caught on the dev machine and in CI's `windows-desktop` job without a Windows box.

## Custom controls

`WpfRenderers.Register(type, ctx => FrameworkElement)` is hooked into the interpreter's default case, so
custom native primitives need **no interpreter fork**:

```csharp
WpfRenderers.Register("NativeRating", ctx =>
{
    var slider = new System.Windows.Controls.Slider { Minimum = 0, Maximum = 5, Value = ctx.Number("value") ?? 0 };
    slider.ValueChanged += (_, e) => ctx.Emit(((int)e.NewValue).ToString());
    return slider;
});
```

See [Custom Controls](../custom-controls.md).

## Hot reload

🧩 **Expected, not run.** `dotnet watch run --project sample/SampleApp.Wpf` should work with no extra setup:
WPF installs a real `DispatcherSynchronizationContext` on the UI thread (which `SwiftApp.Run` captures), and
`WpfBridge` already applies a mid-session `replace`. See [Hot Reload](../hot-reload.md).

## See also

- **[Windows / WinForms](winforms.md)** — why WinForms gets a canvas instead of a control tree.
- **[Windows / WinUI 3](windows.md)** — the sibling native backend this one was ported from.
- **[Skia](skia.md)** — the self-drawing backend, hostable in a WPF window via `SwiftDotNet.Skia.Wpf`.
