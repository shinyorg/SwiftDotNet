# Windows / Windows Forms

Windows Forms is the one target with **no native-control backend**. It gets the
[Skia self-drawing engine](skia.md) hosted in a single control instead — and that is a deliberate design
decision, not a gap waiting to be filled.

> **Status: 🧩 Scaffolded — compiles clean, never run.** The host builds on macOS (via
> `EnableWindowsTargeting`) and on a `windows-latest` runner in CI. The *engine* it hosts is the
> [Skia backend](skia.md), which is the most thoroughly test-verified one in the repo — so the unverified
> surface is only the ~200 lines of host glue below, not the rendering. No window has been opened.

## Why there is no native-control backend

Translating the view tree to real WinForms controls was considered and rejected. GDI controls have no
render transforms, no per-element opacity, no rounded-corner clipping, no vector shapes and no animation
system. That is not a handful of rough edges — it is roughly half the modifier vocabulary:

| DSL feature | Native WinForms |
|---|---|
| `.Rotation`, `.ScaleEffect` | ❌ no transform property on `Control` |
| `.Opacity` | ❌ per-form only (`Form.Opacity`), never per control |
| `.CornerRadius`, `.Border` radius | ❌ needs a custom `Region` per control |
| `Rectangle` / `Circle` / `Capsule` | ❌ no shape controls |
| `.Background(gradient)` | ❌ owner-draw only |
| `.Shadow` | ❌ none |
| `.Keyframes(…)`, `.Animation` | ❌ no animation system |
| `.Material` | ❌ none |

A native backend would have had to make all of those **silent no-ops**, and the per-backend status tables
throughout these docs would be mostly ❌. Painting the surface ourselves gives WinForms the complete
feature set for the cost of one control — so that is what it gets, and the docs stay honest.

If you want real Win32 controls with UI Automation accessibility on Windows, that is what
**[WPF](wpf.md)** and **[WinUI 3](windows.md)** are for.

## Project layout

[`src/SwiftDotNet.Skia.WindowsForms`](../../src/SwiftDotNet.Skia.WindowsForms) (`net10.0-windows`,
`UseWindowsForms`) — references [`SwiftDotNet.Skia`](../../src/SwiftDotNet.Skia) only.

| File | Role |
|------|------|
| [`SwiftDotNetSkiaControl.cs`](../../src/SwiftDotNet.Skia.WindowsForms/SwiftDotNetSkiaControl.cs) | The canvas `Control`: paint, input, animation clock. |
| [`SwiftDotNetSkiaForm.cs`](../../src/SwiftDotNet.Skia.WindowsForms/SwiftDotNetSkiaForm.cs) | A ready-made `Form` wrapping the control. |
| [`WindowsTheme.cs`](../../src/SwiftDotNet.Skia.WindowsForms/WindowsTheme.cs) | Reads the shell's light/dark setting. |

## How to use it

```csharp
static class Program
{
    [STAThread]                                   // WinForms requires an STA thread
    static void Main()
    {
        ApplicationConfiguration.Initialize();    // per-monitor DPI awareness — see the gotcha below
        var app = SwiftProgram.CreateSwiftApp();
        Application.Run(new SwiftDotNetSkiaForm(app) { Text = "SwiftDotNet · Skia (WinForms)" });
    }
}
```

Or drop the canvas onto a form you already own — it is an ordinary `Control`, so it composes with the rest
of an existing WinForms app:

```csharp
Controls.Add(new SwiftDotNetSkiaControl(new ContentView()) { Dock = DockStyle.Fill });
```

See [`sample/SampleApp.Skia.WinForms`](../../sample/SampleApp.Skia.WinForms).

## How the surface works

A 32-bit premultiplied GDI+ `Bitmap` whose locked bits an `SKSurface` draws straight into, blitted with
`DrawImageUnscaled` in `OnPaint`. That is the same thing SkiaSharp's own `SKControl` does — done directly
here so the project stays on a plain `net10.0` SkiaSharp reference. **`SkiaSharp.Views.WindowsForms` is
deliberately not referenced:** it targets .NET Framework only (NU1701 on a `net10.0` TFM) and drags in
OpenTK 3.x for its GL variant.

`SetStyle(UserPaint | AllPaintingInWmPaint | OptimizedDoubleBuffer | ResizeRedraw | Selectable)` plus an
empty `OnPaintBackground` is what makes a hand-painted WinForms control not flicker, and `Selectable` is
what lets it take focus at all.

## Input

Raw down/move/up go into the shared `SkiaPointerRouter`, which resolves tap / long-press / swipe /
continuous drag / slider scrub — the same state machine every other Skia head uses. Feeding it a
synthesized click instead would leave `.OnDrag` and `.OnMagnify` dead and sliders inert.

| Input | Mapping |
|---|---|
| Left button | `PointerRouter.Down` / `Move` / `Up`; `MouseLeave` cancels |
| Wheel | `bridge.Scroll` (40px per notch, matching the other desktop heads) |
| **Ctrl + wheel** | `PointerRouter.PinchDelta` — the desktop zoom convention |
| Typing | `OnKeyPress` → `bridge.InsertText` |
| <kbd>Backspace</kbd> / <kbd>Esc</kbd> | `DeleteBackward` / `ClearFocus` |

## Gotchas

- **Call `ApplicationConfiguration.Initialize()`.** Without per-monitor DPI awareness the OS bitmap-stretches
  the whole window on a HiDPI display, and every glyph is blurry — the engine has already rendered at device
  scale, so the stretch is pure loss.
- **WinForms measures in physical pixels, the engine in DIPs.** The control divides every mouse coordinate
  by `DeviceDpi / 96` and scales the canvas by the same factor. (The property is named `DipScale`, not
  `Scale`, because `Control.Scale(float)` already owns that name.)
- **Core's type names shadow the WinForms ones.** This assembly compiles into the `SwiftDotNet` namespace,
  where Core declares `Form` — and Core's `Form` is `sealed`, so `class MyForm : Form` fails with **CS0509
  ("cannot derive from sealed type")** rather than an ambiguity error. `System.Windows.Forms.Form` needs a
  renamed alias (`WinFormsForm`); so does `Timer`, which is otherwise ambiguous with
  `System.Threading.Timer` under `ImplicitUsings`.
- **Every public property on a `Control` needs `[Browsable(false)]` +
  `[DesignerSerializationVisibility(Hidden)]`**, or analyzer **WFO1000** fails the build. None of this
  surface is designer-authored.
- **Do not raise the target platform version to `10.0.19041`.** That TFM is also compatible with
  SwiftDotNet's `net10.0-windows10.0.19041.0` asset, which carries the WinUI 3 backend — the project would
  pull a second backend in for no reason.
- **No native accessibility.** The whole UI is one control as far as UI Automation is concerned. Shared with
  every Skia head; it is the standing trade-off of the self-drawing route.
- **`WebView` and `Map` cannot be painted onto the canvas** — they need a native-view overlay. Shared with
  every Skia head; see [Skia](skia.md).

## Running

```bash
dotnet run --project sample/SampleApp.Skia.WinForms     # Windows only
```

It **builds** anywhere (`EnableWindowsTargeting`), and CI's `windows-desktop` job compiles it on a real
Windows runner.

## Custom controls

Custom native primitives use the **Skia** renderer registry, not a WinForms one — the canvas has no native
controls to plug into. `SkiaRenderers.Register(type, …)` draws the primitive with the engine's `ICanvas`;
see [Custom Controls](../custom-controls.md) and [Skia](skia.md).

## Hot reload

🧩 **Expected, not run.** WinForms installs a `WindowsFormsSynchronizationContext` on the UI thread once a
control handle exists, which `SwiftApp.Run` captures, and the bridge repaints on `Invalidate`. See
[Hot Reload](../hot-reload.md).

## See also

- **[Windows / WPF](wpf.md)** — the native-control backend, plus the same Skia canvas hosted on WPF.
- **[Skia](skia.md)** — the engine this hosts, its feature set and its trade-offs.
