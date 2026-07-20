# Windows (WinUI 3)

WinUI 3 controls are fully C#-bindable (not a compiler-plugin framework), so Windows uses the same **pure-C#
"translate to controls"** retained-mode route as [GTK](linux-gtk.md) — **no native shim**.

> **Status: scaffolded, never compiled.** WinUI 3 / Windows App SDK require **Windows** to build, and the
> primary dev machine is macOS. Nothing on this page has been through a compiler, let alone run. Treat every
> claim below as *intended* behaviour, not verified behaviour — and expect **more than minor** fixes on the
> first Windows build. There are no Windows rendering tests in the suite, so CI does not cover this backend
> at all.

### Known blocker: Core type names shadow the WinUI ones

Core (`Core/**`) and `Platforms/Windows/**` compile into the **same assembly and the same namespace**
`SwiftDotNet`. Core declares public types whose simple names collide with the WinUI types this backend uses:
`Grid`, `Button`, `Image`, `Slider`, `ColorPicker`, `TabView`, `Rectangle`, `Menu`, `Picker`, `List`,
`Label`, `Toggle`, `Sheet`, `Alert`, … plus the enums `HorizontalAlignment` and `VerticalAlignment`.

C# resolves a simple name against members of the enclosing namespace **before** `using`-imported namespaces,
so inside `namespace SwiftDotNet` a bare `new Grid()` binds to `SwiftDotNet.Grid : View` (no parameterless
constructor) and `HorizontalAlignment.Stretch` binds to Core's enum → CS0117. This is structural: it would
have failed the very first Windows build regardless of any WinUI API details.

It is now worked around with distinct-name aliases (`WinGrid`, `WinHorizontalAlignment`, …) declared per
file. Note a plain `using Grid = Microsoft.UI.Xaml.Controls.Grid;` does **not** work — that alias itself
conflicts with the namespace member (CS0576), which is why the aliases are renamed rather than plain. Like
everything else here, the fix is **unverified**.

## Why not `microsoft-ui-reactor`?

That project is a WinUI-only toolkit with the *same* architecture (virtual tree + reconciler + hooks +
`VStack`/`Button` DSL) — a **sibling, not a sublayer**. Using it would stack two reconcilers, so this backend
keeps SwiftDotNet's own reconciler.

## Project layout

The Windows platform lives in [`src/SwiftDotNet/Platforms/Windows`](../../src/SwiftDotNet/Platforms/Windows)
(net10.0-windows10.0.19041.0, `UseWinUI`, `Microsoft.WindowsAppSDK`):

| File | Role |
|------|------|
| `WinBridge.cs` | `IBridge`; Host is a `Border`; applies patches to the element tree. |
| `WinNode.cs` | Node → WinUI; an Element/Inner split so modifiers wrap in a `Border`. |
| `WinStyle.cs` | Color/font/emoji tokens. |
| `WinRenderers.cs` | Custom-renderer registry. |
| `SwiftDotNetHost.cs` | `CreateRootElement(View) → UIElement`. |

Reusable host base: `SwiftDotNetApplication : Application`.

## Widget map (selection)

`Text`→`TextBlock`, `Button`→`Button`, `V/HStack`→`StackPanel`, `ZStack`→`Grid`, `ScrollView`→`ScrollViewer`,
`Grid`→`Grid` (star columns), `List`→`Border`+`StackPanel`, `DisclosureGroup`→`Expander`, `TabView`→`TabView`,
`Menu`→`Button`+`MenuFlyout`, `TextField`→`TextBox`, `SecureField`→`PasswordBox`, `TextEditor`→`TextBox`
(AcceptsReturn), `Toggle`→`ToggleSwitch`, `Slider`→`Slider`, `Stepper`→`NumberBox`, `Picker`→`ComboBox`,
`DatePicker`→`CalendarDatePicker`, `ColorPicker`→`Button`+Flyout(`ColorPicker`), `NavigationStack`→a
`WinNavController`, `Sheet`/`Alert`→`ContentDialog`, `ProgressView`→`ProgressBar`/`ProgressRing`,
`Link`→`HyperlinkButton`, shapes→`Rectangle`/`Ellipse`.

**Modifiers** wrap the Inner control in a `Border` (padding/background/border/cornerRadius); foreground/font on
`TextBlock`; opacity on Inner; `frame`→Width/Height + alignment; `onTapGesture`→`Tapped`;
`shadow`→a Composition `DropShadow`.

`ZStack`'s `alignment` prop is pushed onto each child's `HorizontalAlignment`/`VerticalAlignment` (a WinUI
`Grid` has no container-level alignment). This is what makes `OverlayPosition.Bottom`/`Top` work — the
Controls library's `Overlay`/`OverlayHost`, and therefore `Toast`, `Dialog`, `LoadingOverlay`,
`FloatingPanel`, `ImageViewer` and `DurationPicker`, lower to a ZStack carrying that token. It
unconditionally overrides a child's own alignment, which is deliberate: VStack/HStack children default to
`Center`, so a conditional override would leave the toast case still centred.

### Modifier gaps and degradations

- **`shadow` uses a Composition `DropShadow`, not `ThemeShadow`.** `ThemeShadow` derives everything from Z
  depth and exposes no radius/colour/offset, so it cannot honour the wire props at all. The sprite has no
  alpha mask, so a rounded element still casts a **rectangular** shadow (Web and Skia follow the corner
  radius). Before this existed there was no `shadow` case at all, and `Toast`, `Dialog`, `Fab`, `FabMenu`,
  `FloatingPanel`, `LoadingOverlay`, `RangeSlider` and `Slider` all rendered flat.
- **`returnKey` has no WinUI equivalent** — the Windows touch keyboard's Enter label isn't settable from
  XAML (no analog to SwiftUI's `submitLabel` or Compose's `ImeAction`). It is explicitly ignored rather
  than faked. `keyboard`→`InputScope` and `maxLength`→`MaxLength` do work.
- **A repeating `.Repeating()` animation does not loop.** WinUI gets only a `RepositionThemeTransition`, so
  `SkeletonView`'s shimmer and `BadgeView`'s pulse are static.
- **`.Material` is a translucent tint**, not a real backdrop blur.
- **Modifiers are applied at build time only** — `UpdateProps` does not re-run `ApplyModifiers`, so a
  modifier whose value changes after the first render will not repaint. ZStack alignment is the exception;
  it *is* re-applied on update.

## Running (on Windows)

The sample is **unpackaged + fully self-contained** (`WindowsPackageType=None`, `SelfContained=true`,
`WindowsAppSDKSelfContained=true`, `RuntimeIdentifier=win-x64`), so it runs with no prerequisites beyond the
.NET SDK:

```powershell
dotnet run --project sample/SampleApp -f net10.0-windows10.0.19041.0
```

## Likely first-build fixes (untested)

- `ContentDialog.Content` string-vs-object; **`XamlRoot` is null until the window is Activated** — `SyncDialog`
  may need to defer or use the loaded `XamlRoot`.
- `TabView` is document-style; `NavigationView` may look better for app tabs.
- `ColorPicker` `ColorChangedEventArgs.NewColor` shape.

Bootstrap/runtime is handled by the self-contained props, so the "missing WinAppSDK runtime" class of error
should be avoided.

## Hot reload

🧩 **Expected, not run** (needs Windows). `dotnet watch run -f net10.0-windows10.0.19041.0` should work with
no extra setup: WinUI supplies a `SynchronizationContext` and `WinNode` already applies a mid-session
`replace`. See [Hot Reload](../hot-reload.md).
