# Linux / GTK (GTK4)

GTK is a C/GObject library — **not** a compiler-plugin framework like SwiftUI/Compose — so it's fully
C#-bindable via **[Gir.Core](https://gircore.github.io/)** (`GirCore.Gtk-4.0`). That means the GTK backend is
**pure C#, no native shim**: the "translate to controls" route. A retained-mode interpreter maps the node
tree to real `Gtk.Widget`s keyed by node path and applies the same `replace`/`updateProps`/`setChildren`
patches; GTK signals call back into C#.

- **Verified** live on macOS-GTK and functionally headless; the full [`ContentView`](../../sample/SharedUI/ContentView.cs)
  builds **325 real GTK4 widgets, zero exceptions**.

## Project layout

[`src/SwiftDotNet.Gtk`](../../src/SwiftDotNet.Gtk) (net10.0, references the combined `SwiftDotNet`) — it stays
a **separate** project because Linux/GTK shares the `net10.0` TFM with Core, so folding it in would force
Gir.Core/GTK onto every neutral consumer.

| File | Role |
|------|------|
| `GtkBridge.cs` | `IBridge`; parses patch JSON, applies to the widget tree by path id; `Emit(id,value)`. |
| `GtkNode.cs` | Holds a `Gtk.Widget`; `CreateWidget` / `UpdateProps` / `SetChildren`; the full vocabulary map. |
| `GtkStyle.cs` | Color/font tokens + CSS. |
| `GtkRenderers.cs` | Custom-renderer registry (see [Custom Controls](../custom-controls.md)). |
| `SwiftDotNetHost.cs` | `Gtk.Application` + `ApplicationWindow`. |

## Widget map (selection)

`Text`→`Label`, `Button`→`Button`, `V/HStack`→`Box`, `ZStack`→`Overlay`, `ScrollView`→`ScrolledWindow`,
`Grid`→`Grid`, `List`→`ListBox`, `TabView`→`Notebook`, `DisclosureGroup`→`Expander`, `Menu`→`MenuButton`+
`Popover`, `TextField`/`SecureField`→`Entry`, `TextEditor`→`TextView`, `Toggle`→`Switch`, `Slider`→`Scale`,
`Stepper`→`SpinButton`, `Picker`→`DropDown`, `DatePicker`→`Calendar` (in a popover), `ColorPicker`→
`ColorDialogButton`, `ProgressView`→`ProgressBar`/`Spinner`, `Gauge`→`LevelBar`, `Link`→`LinkButton`,
shapes→`Box` + size + CSS.

**Modifiers via a GTK CSS provider:** each node gets a per-node `Gtk.CssProvider` with a unique class
(`sdn-N`, via `AddProviderForDisplay`) for padding/background/border/corner-radius/shadow/foregroundColor/font.
Widget props: `frame`→`SetSizeRequest`, `align`→`halign`+`hexpand`, `opacity`→`Opacity`,
`onTapGesture`→`GestureClick`.

**Two-way controls** emit on signal (`OnNotify "text"/"active"/…`, `OnValueChanged`, buffer `OnChanged`,
`OnDaySelected`); `UpdateProps` re-syncs the value with an equality guard to avoid loops.

## Running

```bash
dotnet run --project sample/SampleApp.Gtk
```

Needs GTK4 native libs: macOS `brew install gtk4` (libs in `/opt/homebrew/lib`), Linux
`apt install libgtk-4-1`. On non-Linux, set `DYLD_FALLBACK_LIBRARY_PATH` / `LD_LIBRARY_PATH` to the GTK libs.

## Gotchas

- **macOS `DYLD_*` + SIP:** run with the env var **inline** (`DYLD_FALLBACK_LIBRARY_PATH=/opt/homebrew/lib
  dotnet …`), *not* via `export` + `nohup` — macOS SIP strips `DYLD_*` on the nohup/re-exec →
  `DllNotFound libgtk`.
- **GTK4's macOS backend doesn't expose windows to the Accessibility API**, and screenshotting it is
  unreliable — a macOS-GTK quirk, irrelevant on Linux. Verify functionally (headless, `SDN_TEST=1`) on macOS;
  verify visually on real Linux.
- `GtkBridge.Host` must **not** center (`Halign`/`Valign`) or content collapses to natural size; set the root
  widget `Hexpand`/`Vexpand=true` in the `replace` path to fill.
- `Alert` is a modal `Gtk.Window` — `Gtk.AlertDialog.New` is a variadic constructor that Gir.Core skips.
- Gir.Core specifics: optional modifier keys need `GetValueOrDefault` (not the indexer);
  `grid.RowSpacing`/`ColumnSpacing` are `int`; `TextBuffer.GetStartIter`/`GetEndIter` are `out` params;
  `Gtk.Calendar.GetDate()` returns a `GLib.DateTime` (`.ToUnix()`).
- **`.ScaleEffect` / `.Rotation` wrap the widget in a `Gtk.Fixed`.** GTK4 widgets have no transform
  property, but `Gtk.Fixed` can apply an arbitrary `GskTransform` to a child, so a transformed node's
  `Widget` (what its *parent* sees) becomes that wrapper while everything internal keeps talking to
  `_content`. That's why the prop-sync casts in `GtkNode.UpdateProps` target `_content`, not `Widget` — a
  new sync path that casts `Widget` will throw the moment a transform is applied. The pivot comes from the
  modifier's `value` anchor token and depends on the allocated size, so it is recomputed on map and on
  size-request changes. 🧩 Not visually verified (needs a real Linux session).
- **`.Material` is a translucent tint, not a real backdrop blur** — GTK4 exposes no backdrop-filter
  equivalent.
- **A repeating animation loops opacity only.** GTK's CSS engine does implement `@keyframes` and the
  `animation` shorthand, so `.Repeating()` emits a per-node keyframes block — but only over the properties
  GTK exposes to CSS. Like the Web backend's shared `sdn-pulse`, it loops `opacity` 1 → 0.4. A looping
  *scale* or *rotate* is not expressible (GTK CSS has no `transform`), even though one-shot transforms now
  work via the `Gtk.Fixed` route above.
- **`Image.FromUrl` is fetched asynchronously** and cached by URL string, so the widget renders empty for a
  frame and then fills in. Any failure (DNS, HTTP status, timeout, undecodable payload) is swallowed and the
  placeholder stays — it never throws.

## Custom controls

The renderer registry (`GtkRenderers.Register(type, ctx => Gtk.Widget)`) is hooked into the interpreter's
default case, so custom native primitives need **no interpreter fork**. See
[Custom Controls](../custom-controls.md).

## Hot reload

🧩 **Expected, not run** (needs Linux). `dotnet watch run --project sample/SampleApp.Gtk` should work with
no extra setup: it is a plain `net10.0` app, GTK supplies a real `SynchronizationContext`, and `GtkBridge`
already applies a mid-session `replace`. See [Hot Reload](../hot-reload.md).
