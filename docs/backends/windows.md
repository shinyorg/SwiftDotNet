# Windows (WinUI 3)

WinUI 3 controls are fully C#-bindable (not a compiler-plugin framework), so Windows uses the same **pure-C#
"translate to controls"** retained-mode route as [GTK](linux-gtk.md) — **no native shim**.

> **Status: scaffolded, not yet compiled.** WinUI 3 / Windows App SDK require **Windows** to build, and the
> primary dev machine is macOS. The backend is idiomatic and pattern-matched against the other pure-C#
> interpreters; expect **minor WinUI API fixes on the first Windows build**.

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
`TextBlock`; opacity on Inner; `frame`→Width/Height + alignment; `onTapGesture`→`Tapped`.

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
