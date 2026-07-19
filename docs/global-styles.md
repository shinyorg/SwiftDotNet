# Global Styles

SwiftUI has no stylesheet — "global styling" is three things: the **environment cascade**, **style
protocols** (`ButtonStyle` & friends), and **reusable `ViewModifier`s**. SwiftDotNet offers all three and
resolves the cascade **in C# during the render pass**, so global styles work identically on **every** backend
— including the ones (Skia/GTK/WinUI) that have no inheritance of their own — with **no per-backend code**.

The implementation lives in [`Core/EnvironmentValues.cs`](../src/SwiftDotNet/Core/EnvironmentValues.cs),
[`Core/Styles.cs`](../src/SwiftDotNet/Core/Styles.cs), and [`Core/Theme.cs`](../src/SwiftDotNet/Core/Theme.cs);
the cascade is covered by [`GlobalStyleTests`](../tests/SwiftDotNet.Tests/GlobalStyleTests.cs). The **Styles**
tab in the sample [`ContentView`](../sample/SharedUI/ContentView.cs) exercises all three.

## How it resolves

Each node inherits any ambient `font` / `foregroundColor` / control-style it didn't set itself, and ships to
the backend **fully resolved** — using only modifier types the backends already understand. An explicit local
modifier always wins over an inherited one. Nothing is injected unless you set an environment, so there's
**zero cost** otherwise.

## The three mechanisms

```csharp
new ContentView()
    // B — environment cascade (SwiftUI's `.environment`): descendants inherit unless they set their own
    .Environment(e => e.Font(Font.Body).ForegroundColor(Color.Primary))
    // C — control style (SwiftUI's `.buttonStyle`): every Button below adopts it, no call-site changes
    .ButtonStyle(new FilledButtonStyle())
    // design tokens, read by styles & bodies via EnvironmentValues.Current.Theme
    .Theme(new Theme { Accent = Color.Hex("#7C4DFF"), CornerRadius = 16 });

// A — reusable bundles (SwiftUI's ViewModifier / View extension), applied explicitly per view:
new VStack(new Text("Hi")).CardStyle();                 // built-in, reads the ambient Theme
new VStack(new Text("Hi")).Style(b => b.Padding().Background(Color.Secondary).CornerRadius(12));
```

### A — Reusable modifier bundles (`.Style` / `.CardStyle`)

Attach a reusable **`IViewStyle`** bundle. Bundles are authored with the *same* fluent modifiers you'd
otherwise chain, and resolve at render time so they can read the ambient `Theme`.

- `.Style(b => …)` — inline bundle.
- `.CardStyle()` — a built-in bundle that reads the ambient `Theme`.

### B — Environment cascade (`.Environment`)

Sets ambient `Font` / `ForegroundColor` that descendants inherit unless they set their own. Read the active
environment anywhere a view is built via `EnvironmentValues.Current`.

### C — Control-style protocols (`.ButtonStyle`)

Sets the ambient `IButtonStyle`; every `Button` below adopts it with no call-site changes.

## Built-in styles & tokens

| Kind | Built-ins |
|------|-----------|
| `IButtonStyle` | `FilledButtonStyle`, `BorderedButtonStyle` |
| `IViewStyle` | `CardStyle` |
| Tokens (`Theme`) | `Accent`, `CornerRadius`, … — read via `EnvironmentValues.Current.Theme` |

## Scoping & composition

Each of `.Environment` / `.Theme` / `.ButtonStyle` wraps the view in a **transparent `EnvironmentScope`** —
no node in the tree, no diff impact. Nested scopes **compose**: an inner scope overrides only what it sets.

## Related

- [Modifiers, Gestures & Animation](modifiers-gestures-animation.md) — the fluent modifiers that bundles and
  control styles are built from.
- [Custom Controls](custom-controls.md) — a composite control can read the ambient `Theme` in its `Body`.
