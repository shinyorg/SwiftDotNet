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

## Running

```bash
dotnet run --project sample/SampleApp.Web          # → http://localhost:5000
```

Needs the `wasm-tools` workload. [`sample/SampleApp.Web`](../../sample/SampleApp.Web)
(Sdk.BlazorWebAssembly) hosts `<SwiftDotNetView Root="new ContentView()">` via `AppRoot`, mounted at `#app`.

## Deferred

Web pull-refresh / load-more / list windowing need JS-interop `scrollTop` — not yet wired. See
[Collection View → Deferred](../collection-view.md#deferred) and the [Roadmap](../roadmap.md).

## Maps

The Web map renderer is [`src/SwiftDotNet.Maps.Web`](../../src/SwiftDotNet.Maps.Web) (MapLibre GL, built &
verified) — a stateful JS-interop component. See [Maps](../maps.md).
