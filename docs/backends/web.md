# Web (Blazor WebAssembly → HTML/DOM)

The Web backend is the third **pure-C# "translate to controls"** interpreter — here the "control" is an HTML
element. The whole framework and your C# UI execute **in the browser** via Blazor WebAssembly.

- **Verified** in Chrome: the shared [`ContentView`](../../sample/SharedUI/ContentView.cs) renders as real
  HTML/DOM, including the full event → `State` → re-render round-trip.

## How it works

[`src/SwiftDotNet.Web`](../../src/SwiftDotNet.Web) is a **separate Razor class library** (Sdk.Razor, net10.0,
references the combined `SwiftDotNet` + `Microsoft.AspNetCore.Components.Web`). It's separate because Blazor
has no distinct TFM (plain `net10.0`, like GTK).

`SwiftDotNetView : ComponentBase` walks the node tree in `BuildRenderTree`, emitting HTML/CSS through Blazor's
`RenderTreeBuilder` — so **Blazor's own render-tree → DOM diff is the write layer** (no manual DOM
manipulation).

| File | Role |
|------|------|
| `WebBridge.cs` | `IBridge`; parses patches into a `WebNode` model, raises `Changed` → `StateHasChanged`. |
| `WebNode.cs` | Plain data + `Find` by path. |
| `SwiftDotNetView.cs` | `BuildRenderTree` walks the model → HTML; holds running sequence numbers + local `_tab`/`_nav` state. |
| `WebStyle.cs` | Token → CSS. |
| `WebRenderers.cs` | Custom-renderer registry (`delegate void WebRenderer(RenderTreeBuilder, WebRenderContext, ref int seq)`). |

## Node → HTML

`VStack`/`HStack`→flex `div`, `ZStack`→grid overlap, `ScrollView`→flex+overflow, `Grid`→CSS grid, `List`→
bordered rows, `DisclosureGroup`→button + conditional, `TabView`→tab-bar buttons + content (local selection),
`Menu`→`details`/`summary`, `TextField`/`SecureField`→`<input type=text/password>`, `TextEditor`→`textarea`,
`Toggle`→`<input type=checkbox>`, `Slider`→`<input type=range>`, `Stepper`→`<input type=number>`,
`Picker`→`<select>`, `DatePicker`/`ColorPicker`→native inputs, `NavigationStack`/`Link`→header + content +
local stack, `Sheet`/`Alert`→fixed overlay `div`, `Image`/`Label`→emoji `span`, `ProgressView`/`Gauge`→
`<progress>`, `Link`→`<a>`, shapes→`div` + border-radius + bg.

**Modifiers → inline CSS** (padding/background/border/cornerRadius/shadow/opacity/foregroundColor/font/frame/
align); `onTapGesture`→`onclick`. Events via `EventCallback.Factory.Create` / `Create<ChangeEventArgs>`.

`ZStack` is a `display:grid` container whose layers all sit in `grid-area:1/1`; the node's `alignment` prop
maps to each layer's `justify-content`/`align-items`. The layers **stretch** rather than shrink-wrap, which
matters: `OverlayHost` lowers to a ZStack nested inside another ZStack, so a shrink-wrapped inner layer
would have no cell to align within and `OverlayPosition.Bottom`/`Top` would silently render centred (which
is what used to happen). Because `BuildRenderTree` re-reads props every render, a changing alignment
repositions with no extra patch path.

### Gestures

`onTapGesture`, `onLongPress`, `onSwipe`, `onDrag` and `onMagnify` are all resolved from one pointer-event
wiring. They must be wired **together in a single pass** — Blazor's `AddAttribute` replaces an earlier
attribute of the same name, so registering `onpointerdown` twice (once for drag, once for long-press)
silently dropped the first. That's what let `ImageViewer` pan and pinch coexist.

- **Pinch** comes from two live pointers (distance ratio vs. the spread at gesture start), plus a
  `wheel`+`ctrlKey` path for desktop trackpads. `touch-action:none` is set only when an `onMagnify`
  modifier is present, so drag-only nodes keep their previous behaviour.
- **Gotcha:** the touch path re-baselines every gesture (always starts at 1.0), but the ctrl+wheel path has
  no begin/end boundary the browser exposes, so its factor accumulates for the component's lifetime —
  effectively an absolute zoom level. `ImageViewer` clamps, so it behaves; a handler that assumes each
  gesture starts at 1.0 will see the two paths differently. There is no backend-neutral fix.
- **A repeating `.Repeating()` animation maps to a CSS `@keyframes` loop** (`-1`→`infinite`, autoreverse→
  `animation-direction:alternate`) using the shared `sdn-pulse` keyframes, which fade `opacity` 1 → 0.4.
  Because CSS keyframes must be declared ahead of time, **every** repeating animation renders as that same
  opacity pulse regardless of which property the C# nominally animates. Skia, GTK and Compose deliberately
  match this so the effect reads identically everywhere.

## Running

```bash
dotnet run --project sample/SampleApp.Web          # → http://localhost:5000
```

Needs the `wasm-tools` workload. [`sample/SampleApp.Web`](../../sample/SampleApp.Web)
(Sdk.BlazorWebAssembly) hosts `<SwiftDotNetView Root=...>` via the `AppRoot` component, mounted at `#app`.
Web **reuses Blazor's own service container** rather than building a second one: `Program.cs` calls
`SwiftProgram.AddSharedServices(builder.Services)` and then assigns `SwiftHost.Services = host.Services`, so a
single registration list serves both Blazor and SwiftDotNet — see
[Hosting & Dependency Injection](../hosting-and-di.md).

> **Gotcha:** Blazor also defines an `[Inject]` attribute, so a file importing both `SwiftDotNet` and
> `Microsoft.AspNetCore.Components` must qualify which one it means.

## Deferred

Web pull-refresh / load-more / list windowing need JS-interop `scrollTop` — not yet wired. See
[Collection View → Deferred](../collection-view.md#deferred) and the [Roadmap](../roadmap.md).

## Maps

The Web map renderer is [`src/SwiftDotNet.Maps.Web`](../../src/SwiftDotNet.Maps.Web) (MapLibre GL, built &
verified) — a stateful JS-interop component. See [Maps](../maps.md).

## Hot reload

🧩 **Partly verified.** `dotnet watch --project sample/SampleApp.Web` compiled and applied a `Body` edit
("C# and Razor changes applied in 225 ms"), but no browser was attached, so the DOM re-render itself has
not been observed. Blazor WebAssembly supports metadata updates natively and `WebBridge` already handles a
mid-session `replace`, so this is expected to work. See [Hot Reload](../hot-reload.md).
