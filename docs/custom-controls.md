# Custom Controls

There are two ways to add your own control. Pick by whether it's a *composition of existing views* (almost
always) or a *genuinely new native primitive*.

## 1. Composite — the common case

Subclass `View` and compose existing views in `Body`. Pure C#, **no native code**, renders on **every**
backend automatically.

```csharp
public sealed class Rating : View
{
    readonly State<int> _value;
    public Rating(State<int> value) => _value = value;

    public override View Body =>
        new HStack(
            Enumerable.Range(1, 5).Select(i =>
                new Button(i <= _value.Value ? "★" : "☆", () => _value.Value = i)
            ).ToArray()
        );
}
```

Worked example: [`sample/SharedUI/Rating.cs`](../sample/SharedUI/Rating.cs) — a ★/☆ rating built from
`HStack` + `Button`, visible in the sample's **Inputs** tab.

Because it's just views, a composite control also participates in [global styles](global-styles.md) — read
the ambient `Theme` via `EnvironmentValues.Current` in its `Body`.

## 2. Custom native primitive

For a control that *isn't* a composition — a native map, a gauge, a platform-specific widget — subclass
`CustomView` ([`Core/CustomView.cs`](../src/SwiftDotNet/Core/CustomView.cs)), emit props under a `TypeName`,
then register a **per-backend renderer**.

```csharp
public sealed class NativeRating : CustomView
{
    readonly State<int> _value;
    public NativeRating(State<int> value) => _value = value;

    protected override string TypeName => "NativeRating";
    protected override void Configure(CustomNode n)
    {
        n.Number("value", _value.Value);
        n.OnEvent(v => _value.Value = int.Parse(v));
    }
}
```

### Registering renderers

On the **pure-C# backends this needs no interpreter fork** — the registry is hooked into each interpreter's
default case:

```csharp
// GTK
GtkRenderers.Register("NativeRating", ctx => {
    var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, 0, 5, 1);
    scale.SetValue(ctx.Number("value") ?? 0);
    scale.OnValueChanged += (_, _) => ctx.Emit(((int)scale.GetValue()).ToString());
    return scale;
});

// WinUI — WinRenderers.Register(type, ctx => FrameworkElement)
// Web   — WebRenderers.Register(type, WebRenderer delegate)
// TUI   — TuiRenderers.Register(type, ctx => Visual)

// Self-drawing (Skia / WebGPU / Unity) — one registry for all three:
//   VisualRenderers.Register(type, IVisualRenderer)   (Measure + Paint)
```

### Self-drawing backends: `IVisualRenderer` vs `ISkiaRenderer`

Because a self-drawing backend owns the pixels, a renderer must both **measure** itself (there is no native
control with an intrinsic size) and **paint** itself — the self-drawing analog of GTK's `Create`/`Update`
pair.

Prefer [`IVisualRenderer`](../src/SwiftDotNet.Graphics/VisualRenderers.cs) for new code. It draws through
the rasterizer-neutral [`ICanvas`](../src/SwiftDotNet.Graphics/ICanvas.cs), so the same renderer works on
Skia, WebGPU and Unity:

```csharp
using SwiftDotNet.Graphics;

sealed class RatingRenderer : IVisualRenderer
{
    public Size Measure(VisualRenderContext ctx, Size available) => new(available.Width, 44);

    public void Paint(VisualRenderContext ctx, ICanvas canvas, Rect rect)
    {
        var filled = (int)(ctx.Number("value") ?? 0);
        for (var i = 0; i < 5; i++)
            canvas.DrawCircle(rect.Left + 22 + i * 32, rect.MidY, 10,
                Paint.Fill(i < filled ? Theme.Accent : Theme.Separator(dark: false)));
    }
}

VisualRenderers.Register("NativeRating", new RatingRenderer());
```

[`ISkiaRenderer`](../src/SwiftDotNet.Skia/SkiaRenderers.cs) remains fully supported — `SkiaRenderers.Register`
bridges it onto the same registry — but it hands you a raw `SKCanvas`, so it is **inherently Skia-only**: a
renderer registered through it draws nothing on a non-Skia canvas rather than guessing. Use it when you
genuinely need Skia-specific drawing (a `SKPath`, a shader), and accept that those nodes go blank elsewhere.

On the [terminal backend](backends/tui.md) this seam does double duty: it is also how you reach the
Terminal.UI controls the DSL has no node type for — `Table`, `DataGridControl`, `TreeView`, `CodeEditor`,
`MarkdownControl`, the chart family. Import the narrowest namespace, never `XenoAtom.Terminal.UI` itself,
which declares its own `State<T>` and would make every `State<int>` in your views ambiguous:

```csharp
using XenoAtom.Terminal.UI.Controls;   // NOT XenoAtom.Terminal.UI

TuiRenderers.Register("NativeRating", ctx =>
{
    // Range and step before value: Slider<T> clamps on every write to Value.
    var slider = new Slider<double> { SnapToStep = true, ShowValueLabel = true };
    slider.Minimum = 0;
    slider.Maximum = 5;
    slider.Step = 1;
    slider.Value = ctx.Number("value") ?? 0;
    slider.ValueChanged(() => ctx.Emit(((int)slider.Value).ToString()));
    return slider;
});
```

For the **native-shim backends** (SwiftUI/Compose), register from the native side, since those toolkits have
no per-control C# view:

```swift
swiftDotNetRegisterRenderer("NativeRating") { props in AnyView(/* SwiftUI */) }
```
```kotlin
registerRenderer("NativeRating") { props -> /* @Composable */ }
```

> **Prop value types differ:** Kotlin VNode props are `Any?` (cast with `as?`); Swift VNode props are
> `PropValue` (use `.string` / `.number` / `.bool`).

### Graceful fallback

An **unregistered type renders a `⚠️` placeholder, not a crash** (`⚠` on the terminal backend). So you can ship a `CustomView` and add
backend renderers incrementally.

## Reaching a specific native view (`.Tag`)

A proposed (partly planned) seam lets you reach the underlying native view of an *existing* control by tag —
`.Tag(name)` in Core plus a per-backend `GtkNative.Customize<T>(tag, w => …)` / `WinNative.Customize`
registry. On GTK/WinUI this is pure C# with no fork; SwiftUI/Compose have no per-control native view, so use
the custom-renderer seam above instead. See the [Roadmap](roadmap.md).

## The `Map` control as a real example

The opt-in [Maps](maps.md) companion is a `CustomView` shipped as separate packages, with a real renderer per
backend (MapKit on Apple, MapLibre on Web/Android). It's the canonical demonstration of this seam.
