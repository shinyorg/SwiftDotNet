# Plan: Native-view access ("escape hatch")

Status: **planned — nothing built** (verified 2026-07-19: no `.Tag` modifier or `Customize` registry in
`src/`) · Author: design notes · Target: SwiftDotNet all backends

Let a developer reach the real native control behind a SwiftDotNet view to customize it directly —
the equivalent of SwiftUI's `.introspect()` / MAUI's `Handler.PlatformView`. Keep the shared DSL clean;
put the platform-specific customization in each platform head.

---

## 1. Guiding decisions

- **Tag + registry, not a shared native API.** The shared DSL only tags a control (`.Tag("emailField")`).
  The native customization is registered per backend (typed to that backend's widget). A single cross-platform
  "give me the native view" API is impossible because the native type differs (`Gtk.Entry` vs `UITextField` vs
  `TextBox`), so we don't pretend otherwise.
- **Mirror the existing seams.** This is a third registry alongside `*Renderers` (custom controls) and the
  proposed `*Modifiers` (custom modifiers). Same shape: a static `Register`/`Customize` + a lookup in the
  interpreter. Unregistered tags are ignored (graceful).
- **View-based vs declarative split is load-bearing:**
  - **View-based backends — GTK, WinUI:** the control *is* an addressable object → native access is clean and
    **pure C#, no interpreter fork**. This plan fully covers these.
  - **Declarative backends — SwiftUI (iOS/macOS), Compose (Android):** there is **no stable per-control native
    view** (SwiftUI hides its `UIView`s; Compose isn't view-based). The escape hatch there is the existing
    **custom-renderer seam** — supply your own SwiftUI view / composable (e.g. a `UIViewRepresentable` wrapping
    a real `UITextField`). This plan documents that path but adds no per-control native accessor for them.

---

## 2. Public API

### Core (`src/SwiftDotNet.Core`)
- New `TagModifier : Modifier` → serializes `{ "type":"tag", "value":<name> }`.
- New fluent extension on `View`:
  ```csharp
  public static T Tag<T>(this T view, string name) where T : View;
  ```
- Tags flow to every backend as a modifier; backends without a native-access registry ignore them.

### GTK (`src/SwiftDotNet.Gtk`) — new `GtkNative.cs`
```csharp
public static class GtkNative
{
    public static void Customize(string tag, Action<Gtk.Widget> configure);
    public static void Customize<T>(string tag, Action<T> configure) where T : Gtk.Widget; // typed convenience
    internal static Action<Gtk.Widget>? Get(string tag);
}
```
Usage:
```csharp
GtkNative.Customize<Gtk.Entry>("emailField", e => {
    e.SetInputPurpose(Gtk.InputPurpose.Email);
    e.SetTooltipText("your@email.com");
});
```

### WinUI (`src/SwiftDotNet.Windows`) — new `WinNative.cs` (parallel; unbuilt on macOS)
```csharp
public static class WinNative
{
    public static void Customize(string tag, Action<FrameworkElement> configure);
    public static void Customize<T>(string tag, Action<T> configure) where T : FrameworkElement;
    internal static Action<FrameworkElement>? Get(string tag);
}
```

### SwiftUI / Compose — no per-control native accessor
Documented path = the custom-renderer seam already shipped:
```swift
// iOS/macOS: own the real UIKit control end-to-end
swiftDotNetRegisterRenderer("NativeEmailField") { props in
    AnyView(UIKitEmailField(text: props.string("text") ?? "") { props.emit($0) }) // UIViewRepresentable
}
```
Compose: register a composable (there is nothing lower to reach than a composable).

---

## 3. Interpreter wiring

### GTK (`GtkNode.ApplyModifiers`)
Add a case in the modifier loop:
```csharp
case "tag":
    if (m.GetValueOrDefault("value") is string tag && GtkNative.Get(tag) is { } configure)
        configure(Widget);   // Widget is the base control (Inner), realized at build time
    break;
```
- Runs once at create (in `ApplyModifiers`). The customizer receives the base `Gtk.Widget` (== `Inner`),
  before any modifier-Border wrapping, so casts to the concrete type (`Gtk.Entry`, etc.) work.
- Do **not** re-run on `UpdateProps` (customizers are set-up hooks, not per-render). If a subtree is rebuilt
  via `setChildren`, the new widget re-runs `ApplyModifiers` → customizer re-applies. Correct.

### WinUI (`WinNode.ApplyModifiers`)
Same: on a `tag` modifier, `WinNative.Get(tag)?.Invoke(Inner)`.

### SwiftUI / Compose
No change for native access (renderer seam covers it). Optionally: their `applyModifiers` can ignore the
`tag` modifier explicitly (already the default — unknown modifier types fall through).

---

## 4. Files

| File | Change |
|---|---|
| `src/SwiftDotNet.Core/Modifier.cs` | add `TagModifier` |
| `src/SwiftDotNet.Core/ViewModifiers.cs` | add `.Tag(name)` |
| `src/SwiftDotNet.Gtk/GtkNative.cs` | **new** registry |
| `src/SwiftDotNet.Gtk/GtkNode.cs` | handle `"tag"` in `ApplyModifiers` |
| `src/SwiftDotNet.Windows/WinNative.cs` | **new** registry (parallel) |
| `src/SwiftDotNet.Windows/WinNode.cs` | handle `"tag"` in `ApplyModifiers` |
| `native/.../Bridge.swift`, `Bridge.kt` | no change (renderer seam already covers native access) |
| `README.md` | "Native-view access" subsection under Custom controls |

No changes to the wire protocol, `TreeDiffer`, or the bridges — `tag` is just another modifier.

---

## 5. Verification

- **GTK headless (`SampleApp.Gtk`, `SDN_TEST`):** register a `GtkNative.Customize<Gtk.Entry>("emailField", …)`,
  build a `TextField(...).Tag("emailField")`, render, then assert the entry's tooltip / input-purpose were set
  (read them back off the live `Gtk.Entry`). Proves the widget reached the customizer.
- **GTK visual:** tag the ContentView email/name field, register a customizer that sets a tooltip + input purpose,
  run the GUI, hover to confirm the native tooltip.
- **Cross-backend safety:** confirm the same tagged `ContentView` still renders unchanged on iOS/macOS/Compose
  (tag modifier ignored there).

---

## 6. Edge cases / limitations

- **One customizer per tag** (last registration wins). Fine; tags are app-authored.
- **Lifetime:** customizers run at widget creation; store no long-lived references to widgets that a later
  `setChildren` will replace. If a user needs to react to rebuilds, they re-tag (the customizer re-runs).
- **Realization:** GTK/WinUI customizers run before the widget is realized/added to a window. Setting properties
  (tooltip, input purpose, CSS classes) is fine; anything needing a realized widget/`XamlRoot` must defer
  (`OnRealize` / `Loaded`). Document this.
- **SwiftUI/Compose have no per-control native handle** — this is intrinsic, not a gap to fill. The renderer seam
  is the answer; note it prominently so users don't expect `.Tag()` to reach a `UIView`.
- **Handle-marshaling variant (deferred):** returning a native handle to C# for a node id (trivial on GTK/WinUI;
  `NSObject`-pointer + `Runtime.GetNSObject` on iOS/macOS) is a possible future addition for platform-specific
  app code, but only useful outside the shared DSL and still hits SwiftUI's hidden-view problem. Not in scope.

---

## 7. Phases

1. Core `.Tag()` + `TagModifier`.
2. GTK `GtkNative` + `ApplyModifiers` hook + headless test + visual tooltip demo.
3. WinUI `WinNative` + hook (parallel; build on Windows).
4. Docs: README subsection + a worked `UIViewRepresentable` example for the SwiftUI native-access path.

Effort: ~small. Phases 1–2 are the deliverable; 3 is mechanical; 4 is prose.
