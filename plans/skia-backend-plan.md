# Plan: SkiaSharp self-drawing backend (`SwiftDotNet.Skia`)

**Status:** Draft for review · **Date:** 2026-07-18
**Save to (repo convention):** `plans/skia-backend-plan.md` when work begins.

## Context

SwiftDotNet today has **seven native-fidelity backends** (iOS/macOS/tvOS via SwiftUI, Android
via Compose, Linux/GTK, Windows/WinUI, Web/Blazor DOM) — each maps the C# node tree onto *native
controls*. This plan adds an **eighth, fundamentally different family**: a **self-drawing renderer**
built on SkiaSharp that paints the UI itself onto a GPU canvas, producing a **pixel-identical look on
every platform** (Flutter/Avalonia model) and unlocking **new targets** the native backends can't
reach (dependency-free cross-platform desktop, embedded/framebuffer Linux). Goal per review:
*universal renderer* — same engine for a uniform look on existing platforms **and** new embedded/desktop
targets; hosted through **SkiaSharp.Views.\*** native canvas views; **broad control set** in milestone one.

The architecture is a clean fit. `IBridge` is two methods; props are scalar-only; the **GTK backend
already proves the exact model we copy** — a retained node tree keyed by structural-path id, granular
`replace`/`updateProps`/`setChildren` patches, and hit-test → `Emit(id,value)`. The user's mental model —
"a controls layer + a thin canvas layer per platform" — maps directly onto the repo's structure
(`SwiftDotNet.Skia` engine + per-platform hosts, mirroring `SwiftDotNet.Gtk`).

**The honest catch:** because Skia gives us *pixels, not widgets*, the "controls layer" is **not thin** —
it is effectively a from-scratch UI toolkit. The platform layer genuinely *is* thin (SkiaSharp.Views ships
a canvas view for every OS), but the engine must supply everything the OS gave the native backends for
free (see §"What Skia forces us to build"). This is the single largest piece of work in the project.

## The contract we implement (unchanged, verified)

- `Core/IBridge.cs` — `void Render(string json)` + `void SetEventHandler(Action<string,string?> handler)`.
- `Core/SwiftApp.cs` — full re-render into a `Node` tree rooted at `"0"`; `TreeDiffer` emits a patch;
  `_bridge.Render(patch.ToJson())`. Events dispatched by id via `_actions[nodeId](value)`.
- Wire = `{"ops":[…]}`, three ops, structural-path ids (`"0.2.1"`):
  `replace {node}` · `updateProps {id,props,modifiers}` · `setChildren {id,children[]}`.
  `setChildren` re-sends the **whole subtree** — a retained backend rebuilds that subtree.
- Props/modifiers are **scalar only** (`string`/`double`/`bool`). No structured values; composite payloads
  ride as JSON-encoded string props (Maps' trick) — not needed here.
- **38 built-in types** (Text/Label/Image/Link · VStack/HStack/ZStack/ScrollView/Grid/Group/Form/Section/
  DisclosureGroup/TabView/Tab · List · Button/TextField/SecureField/TextEditor/Toggle/Slider/Stepper/Picker/
  DatePicker/ColorPicker/Menu · NavigationStack/NavigationLink/Sheet/Alert · ProgressView/Gauge ·
  Rectangle/Circle/Capsule/RoundedRectangle · WebView · Spacer/Divider) + open-ended `CustomView.TypeName`.
- **17 modifiers** (padding, font, foregroundColor, background, frame, border, align, cornerRadius, shadow,
  opacity, scaleEffect, animation, disabled, navigationTitle, onTapGesture, onLongPress, onSwipe) — order-preserving.
- Token vocabularies to map: alignment (9), font (largeTitle/title/headline/body/caption), color
  (primary/secondary/red/green/blue/accentColor + `#hex`), animation curve (linear/easeIn/easeOut/easeInOut/spring).
- **Event value conventions:** `null` (Button), `"true"/"false"` (Toggle/DisclosureGroup/Sheet/Alert),
  invariant-culture numeric string (Slider/Stepper/Picker/DatePicker), raw text (TextField/SecureField/TextEditor/ColorPicker).

**Reference implementations to mirror line-for-line:** `src/SwiftDotNet.Gtk/{GtkBridge,GtkNode,GtkRenderers,GtkStyle}.cs`
(retained tree + `Update` hook — the right model) and `Platforms/Windows/Win*.cs`.

## What Skia forces us to build (the parts easy to under-scope)

These are free on native backends and **must be written from scratch** in the engine:

1. **Two-pass layout engine** (measure/arrange) — flex for stacks, grid, scroll content, z-overlay, plus
   intrinsic content sizing, Spacer, `frame`/`padding`/`spacing`/`align`. *Biggest subsystem.*
2. **Text shaping & rendering** — SkiaSharp.HarfBuzz (bidi/emoji/non-Latin), font-token→typeface table,
   wrapping/truncation, multi-line for TextEditor.
3. **Text editing / IME** — caret, selection, cursor movement, clipboard, focus, **soft-keyboard + IME
   composition** on mobile. *Highest-risk item — do not underestimate.*
4. **Input & focus system** — hit-testing (reverse z-order), pointer capture, focus traversal, gesture
   recognition (tap/longpress/swipe/pan/pinch) from raw pointer streams.
5. **Scrolling** — offset state, inertial/momentum scroll, clipping, scrollbars, visible-only culling for List.
6. **Overlays / z-order** — Sheet (bottom sheet), Alert (modal + scrim), Menu/Picker/DatePicker/ColorPicker
   popovers built **entirely from scratch** (no native pickers).
7. **Per-node retained local state** — scroll offset, tab index, nav stack, presentation flags, caret —
   preserved across `updateProps`/`setChildren` (GTK's model; keyed by structural id).
8. **Theme + dark mode** — semantic color tokens resolved light/dark; per-host dark-mode detection; system accent.
9. **DPI / density scaling** — canvas scaled by device pixel ratio (retina, per-monitor DPI), safe-area insets.
10. **Animation clock** — engine-owned frame ticker interpolating animatable props (opacity/color/frame/offset/scale) —
    realizes `plans/animations-plan.md` **uniformly** since we own drawing (opportunity, not just cost).
11. **Icon font** — no SF Symbols; bundle a glyph font and map a subset of `systemImage`/`system` names.

**Known hard limits (call out, don't hide):**
- `WebView` (and Map) **cannot be painted into a canvas** — they need a native-view overlay punched
  through the surface. Out of scope for the engine; Map already excluded per your note.
- **Accessibility** (VoiceOver/TalkBack/UIA/AT-SPI/ARIA) — a self-drawn canvas is an a11y black hole
  unless bridged per platform. Large, separate effort — **deferred milestone**, flagged now.

## Project structure

Mirror the `SwiftDotNet.Gtk`/`SwiftDotNet.Web` split — separate projects, `RootNamespace=SwiftDotNet`,
ProjectReference to `..\SwiftDotNet`:

- **`src/SwiftDotNet.Skia`** (net10.0) — the **engine**, platform-neutral, pure `SkiaSharp` +
  `SkiaSharp.HarfBuzz`. Contains:
  - `SkiaBridge : IBridge` (retained `SkiaNode? _root`; `Render` applies the 3 ops via `Find(dotted-id)`;
    `Emit(id,value)`) — model on `GtkBridge`.
  - `SkiaNode` — retained scene node (Id/Type/Props/Modifiers/Children + computed layout rect + cached
    paints/text + per-node local state); `Build`/`UpdateProps`/`SetChildren` — model on `GtkNode`.
  - `Layout/` — measure+arrange per type. `Paint/` — the draw walk. `Text/` — HarfBuzz shaping + font table.
  - `Input/` — hit-test, focus manager, gesture recognizers, text-edit controller, scroll controller.
  - `Overlays/` — sheet/alert/menu/picker/date/color popovers.
  - `SkiaTheme` (color/font tokens, light/dark) — model on `GtkStyle`.
  - `SkiaRenderers` + `ISkiaRenderer {Create; Update}` + `SkiaRenderContext` (unknown type→`⚠️ {Type}`) —
    model on `GtkRenderers` (the `Create`/`Update` pair, **not** Web's stateless delegate).
  - `ISkiaHost` — host abstraction: per-frame `Paint(SKCanvas, size, density)`, input feed
    (pointer/key/text/resize), `Invalidate()`, soft-keyboard show/hide, a11y hook (stub).
- **`src/SwiftDotNet.Skia.Views`** (multi-target, mirrors core TFMs: `net10.0-android` always;
  `+ios;-macos;-tvos` on OSX; `+windows` on Windows) — thin **SkiaSharp.Views.\*** adapters under
  `Platforms/{iOS,tvOS,macOS,Android,Windows}/` wrapping `SKCanvasView`/`SKXamlCanvas`, translating
  paint + touch/key events into `ISkiaHost`. Each provides a host entry usable from the existing
  `SwiftDotNetAppDelegate/Activity/Application` bases (root = the Skia canvas view instead of the native tree).
- **`src/SwiftDotNet.Skia.Web`** (net10.0, separate like `SwiftDotNet.Web`) — `SkiaSharp.Views.Blazor`
  `SKCanvasView` host. *(Later phase.)*
- **Desktop** — reuse `SwiftDotNet.Skia.Views` macOS adapter for the milestone-1 host; add a
  dependency-free desktop host (Silk.NET/GLFW or SkiaSharp.Views.Desktop/GTK) in a later phase for the
  "no-GTK/WinUI/AppKit" and embedded targets.
- **`sample/SampleApp.Skia`** (net10.0, macOS first) — boots `SkiaHost.Run(new ContentView())`,
  references `SwiftDotNet.Skia` + `SwiftDotNet.Skia.Views` + `sample/SharedUI` (the shared `ContentView`).
- Add all new projects to `SwiftDotNet.slnx`.

## Milestone 1 = broad control set, phased (each phase ends renderable + verifiable)

- **P0 — Scaffold + loop.** `SwiftDotNet.Skia` skeleton; `SkiaBridge`; SkiaSharp.Views.Mac `SKCanvasView`
  host; prove end-to-end: `ContentView` → patch JSON → paint a single `Text` on the canvas; tap a `Button`
  → `Emit` → re-render.
- **P1 — Layout + static paint + tap.** Layout engine (VStack/HStack/ZStack/Grid/Group/Spacer/Divider +
  padding/frame/spacing/align) and paint (Text/Label/Image-as-icon/Button/shapes + background/border/
  cornerRadius/shadow/opacity/foregroundColor) + `onTapGesture`. Renders the **Layout tab**.
- **P2 — Text + theme.** HarfBuzz shaping, wrapping/truncation, font-token table, bundled icon font;
  `SkiaTheme` colors + dark mode.
- **P3 — Scrolling.** ScrollView/List/Form/Section/Grid scroll + clip + inertia + visible-culling.
  Renders the **Lists tab**.
- **P4 — Inputs + editing.** Focus system + text-edit controller (TextField/SecureField/TextEditor,
  caret/selection/soft-keyboard/IME) + Toggle/Slider/Stepper/Picker/DatePicker/ColorPicker. Renders the
  **Inputs tab**. *(Highest risk — budget accordingly.)*
- **P5 — Nav + overlays.** TabView/Tab, NavigationStack/NavigationLink, Sheet, Alert, Menu, DisclosureGroup,
  ProgressView/Gauge, Link. Renders the **Nav + Carousel tabs** → **whole `ContentView`**.
- **P6 — Extensibility + gestures + animation.** `SkiaRenderers` registry + placeholder; longpress/swipe/
  pan/pinch; engine animation clock (`animation`/`scaleEffect`).

## Later milestones (post-1)

- **Host fan-out:** iOS/tvOS/Android/Windows via `SwiftDotNet.Skia.Views`; Blazor via `SwiftDotNet.Skia.Web`;
  dependency-free desktop (Silk.NET) + embedded/framebuffer Linux (DRM/KMS) — the "new targets" goal.
- **Accessibility** bridge per platform.
- `WebView`/native-overlay punch-through (if needed).

## Critical files

- **New:** `src/SwiftDotNet.Skia/**` (engine), `src/SwiftDotNet.Skia.Views/**` (host adapters),
  `sample/SampleApp.Skia/{Program.cs,*.csproj}`, `SwiftDotNet.slnx` (add projects), `README.md` (add backend row).
- **Read/copy patterns from (unchanged):** `src/SwiftDotNet.Gtk/{GtkBridge,GtkNode,GtkRenderers,GtkStyle}.cs`,
  `Platforms/Windows/Win*.cs`, `Core/{IBridge,SwiftApp,TreeDiffer,Node,NodeJson,View,State,CustomView}.cs`,
  `sample/SampleApp.Gtk/Program.cs`, `sample/SharedUI/ContentView.cs`.
- **Reuse:** `SwiftApp.Run(root, bridge)`, `TreeDiffer`/`Patch` (no change), the scalar prop accessors
  (`String/Number/Bool`), the `GtkRenderers` registry shape, the existing `SwiftDotNet*` host bases.

## Verification

- **Build:** `dotnet build src/SwiftDotNet.Skia` and `sample/SampleApp.Skia` on macOS (net10.0).
- **Run + screenshot (per phase):** launch the macOS Skia host, screenshot via `osascript` window bounds +
  `screencapture -R`, and **diff the self-drawn `ContentView` against the existing SwiftUI/AppKit render**
  of the same view — the two must show the same structure/content (Skia is uniform-look, not native-look).
- **Interaction:** Button tap updates state (derived text changes); TextField binding round-trips (P4);
  Toggle/Slider/Picker emit correct value strings; Sheet/Alert present/dismiss (P5).
- **Loop/diff sanity:** confirm no-op renders send nothing; `updateProps` mutates one scene node + redraws
  its dirty rect; `setChildren` rebuilds only that subtree (log patch ops during a state change).
- Reuse the Gtk sample's `SDN_TEST` headless pattern for a custom-renderer round-trip test (P6).
