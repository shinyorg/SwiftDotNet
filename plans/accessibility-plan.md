# Accessibility & screen-reader support

**Status — 2026-07-31: draft, nothing built.** There is no accessibility API in the framework today.
Every backend inherits whatever its toolkit gives it for free, and the Skia backend — which draws its own
pixels — gives nothing at all. This doc is the design; there is no `docs/` page yet because there is no
feature yet.

## Where we actually stand

| Backend | What a screen reader sees today |
|---|---|
| SwiftUI (iOS/macOS/tvOS) | VoiceOver works on the *native* controls the shim builds — a `Button` reads its title, a `Text` reads its text. No labels, hints, values, traits, or grouping can be expressed from C#. Anything drawn as a `Rectangle`+`onTapGesture` is invisible. |
| Compose (Android) | Same: TalkBack reads Compose's own semantics. `Modifier.semantics` is never called by the shim. |
| Web | Partly good by accident — `Button` is a real `<button>`, `TextField` an `<input>`, `Toggle` an `<input type=checkbox>` in a `<label>`, `Slider` an `<input type=range>`. Bad where the renderer emits `div`s: `Picker`, `Stepper`, `TabView`, `Menu`, `List`, `Sheet`/`Alert` (no `role="dialog"`, no focus trap, no Escape). |
| GTK4 | GTK's own widget roles. Nothing settable from C#. |
| WinUI 3 | Whatever the XAML control reports. Never compiled, so untested by definition. |
| **Skia** | **Nothing.** The canvas is a single opaque view. VoiceOver/TalkBack see one unlabelled rectangle. This is the largest single gap in the framework. |

Two things the framework also ignores everywhere: **user accessibility settings** (reduce motion, reduce
transparency, bold text, Dynamic Type / font scale, high contrast, screen-reader-running) and **keyboard-only
operability on Skia** (focus exists only for text fields — see `SkiaBridge.FocusedId` — so nothing else can
be reached or activated without a pointer).

## Design principles

1. **Mirror SwiftUI's accessibility API**, the same way the rest of the DSL does. `.AccessibilityLabel`,
   `.AccessibilityHint`, `.AccessibilityValue`, `.AccessibilityHidden`, `.AccessibilityElement(children:)`.
   Nothing invented where SwiftUI has a name for it.
2. **Modifiers, not node types.** Accessibility rides the existing modifier pass, so it needs no changes to
   `Node`, `TreeDiffer`, or the patch protocol, and every backend already has a loop to hang it off.
3. **Native backends get labels for free; Core must not synthesize them.** Core does *not* invent a label
   for a `Button` — SwiftUI/Compose/GTK/WinUI/DOM already derive one from the control's own content, and a
   synthesized duplicate would make VoiceOver read the title twice. Only Skia synthesizes defaults, because
   only Skia has no control underneath. This asymmetry is deliberate and must be documented.
4. **Settings fold in Core where they can.** Reduce-motion and reduce-transparency are resolved during
   serialization, exactly like the [environment cascade](../docs/global-styles.md) — the node ships already
   corrected, so all seven backends honor them with zero per-backend code.
5. **Honest degradation.** Where a backend can't express something (WinUI can't override a role without a
   custom `AutomationPeer`; GTK can't detect Orca), it's a documented no-op, not a silent lie.

## Decision 1 — wire encoding

Ten modifier types, all reusing fields that already exist on the wire.

| C# | SwiftUI analog | Wire |
|---|---|---|
| `.AccessibilityLabel("Close")` | `.accessibilityLabel` | `{"type":"a11yLabel","value":"Close"}` |
| `.AccessibilityHint("Dismisses the sheet")` | `.accessibilityHint` | `{"type":"a11yHint","value":"…"}` |
| `.AccessibilityValue("60 percent")` | `.accessibilityValue` | `{"type":"a11yValue","value":"…"}` |
| `.AccessibilityHidden()` | `.accessibilityHidden` | `{"type":"a11yHidden","value":"true"}` |
| `.AccessibilityIdentifier("submit-btn")` | `.accessibilityIdentifier` | `{"type":"a11yId","value":"…"}` |
| `.AccessibilityTraits(Traits.Button \| Traits.Header)` | `.accessibilityAddTraits` | `{"type":"a11yTraits","value":"button,header"}` |
| `.AccessibilityElement(AccessibilityChildren.Combine)` | `.accessibilityElement(children:)` | `{"type":"a11yGroup","value":"combine"}` |
| `.AccessibilitySortPriority(1)` | `.accessibilitySortPriority` | `{"type":"a11ySort","amount":1}` |
| `.AccessibilityAction("Delete", () => …)` | `.accessibilityAction(named:)` | `{"type":"a11yAction","value":"Delete","event":"0.2$3"}` |
| `.AccessibilityLiveRegion(Live.Polite)` | (no direct SwiftUI analog) | `{"type":"a11yLive","value":"polite"}` |

**Why this shape.** The Swift shim decodes modifiers into one flat `ModifierData` struct
([`Bridge.swift:34`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift)); every new wire
*field* is a new optional property on it. Splitting accessibility across ten *types* that each carry only
`value` / `amount` / `event` means **zero new fields** on `ModifierData` and zero churn in the Kotlin
`numOf`/cast helpers. It also keeps each concern independently diffable.

**Unknown modifiers are already safely ignored** on every backend — the Swift switch has `default: break`,
Kotlin's `when` is a non-exhaustive statement, and the four C# backends fall through their `switch`. So
Phase 1 can ship on its own without breaking any backend, and each backend can land in any order.

`a11yTraits` tokens (comma-joined, ordered by declaration): `button`, `link`, `header`, `image`,
`selected`, `search`, `summary`, `keyboardKey`, `staticText`, `updatesFrequently`, `playsSound`,
`startsMediaSession`, `allowsDirectInteraction`, `modal`, `toggle`, `adjustable`, `tab`. Not every backend
maps every token — see the mapping table below.

## Decision 2 — the settings channel

`SafeArea` already establishes the pattern for a platform→Core signal: a reserved `$`-prefixed event id
whose payload is parsed in Core and, when it changes, calls `SwiftApp.RequestRender()`
([`SafeArea.cs:92`](../src/SwiftDotNet/Core/SafeArea.cs)). Accessibility settings use the same channel.

```
event id  "$a11y"
payload   "screenReader;reduceMotion;reduceTransparency;boldText;highContrast;textScale"
example   "true;false;false;true;false;1.35"
```

```csharp
public static class Accessibility
{
    public static bool   IsScreenReaderRunning { get; }
    public static bool   ReduceMotion          { get; }
    public static bool   ReduceTransparency    { get; }
    public static bool   BoldText              { get; }
    public static bool   HighContrast          { get; }
    public static double TextScale             { get; }   // 1.0 = unscaled
    public static void   Announce(string message, Live priority = Live.Polite);
}
```

**Difference from `SafeArea`: no `[SupportedOSPlatform]` gating.** Safe area is a device-window concept that
must not exist on desktop, so it's annotation-gated. Accessibility settings are meaningful on *every*
backend, at differing fidelity. The contract is: **unknown reads as "off" / `1.0`**, never as an exception
and never as a `#if`. A backend that can't detect a screen reader reports `false`, which is the safe answer —
code that branches on it degrades to the sighted-user path.

Per-backend detection, with the honest gaps called out:

| Setting | iOS/tvOS | macOS | Android | Web | GTK4 | WinUI | Skia |
|---|---|---|---|---|---|---|---|
| Screen reader running | `UIAccessibility.isVoiceOverRunning` | ⚠️ no public API — best-effort via the `com.apple.universalaccess` domain, else `false` | `AccessibilityManager.isTouchExplorationEnabled` | ⚠️ undetectable by design — always `false` | ⚠️ Orca is not exposed — `false` | ⚠️ no API — `false` | from the host |
| Reduce motion | `isReduceMotionEnabled` | `NSWorkspace.accessibilityDisplayShouldReduceMotion` | `ANIMATOR_DURATION_SCALE == 0` | `prefers-reduced-motion` | `GtkSettings:gtk-enable-animations` | `UISettings.AnimationsEnabled` | from the host |
| Reduce transparency | `isReduceTransparencyEnabled` | `…ShouldReduceTransparency` | ⚠️ none — `false` | `prefers-reduced-transparency` | ⚠️ none | ⚠️ none | from the host |
| Bold text | `isBoldTextEnabled` | ⚠️ none | ⚠️ none | ⚠️ none | ⚠️ none | ⚠️ none | from the host |
| High contrast | `isDarkerSystemColorsEnabled` | `…ShouldIncreaseContrast` | `isHighTextContrastEnabled` | `prefers-contrast` | theme name heuristic | `AccessibilitySettings.HighContrast` | from the host |
| Text scale | `preferredContentSizeCategory` → multiplier | `false`/1.0 | `Configuration.fontScale` | `rem` ratio via JS interop | `Gtk-xft-dpi` ratio | `UISettings.TextScaleFactor` | from the host |

Each native host observes the relevant change notification (`UIAccessibility.voiceOverStatusDidChange…`,
`AccessibilityManager.addTouchExplorationStateChangeListener`, a `matchMedia` listener, `GtkSettings::notify`)
and re-emits `$a11y`. Same de-duplication rule as safe area: an identical payload is dropped without
scheduling a render, or the report spins the render loop.

## Decision 3 — what Core resolves itself

Three settings fold into serialization so every backend gets them for free:

- **Reduce motion → `AnimationModifier.Serialize`** emits `duration: 0, delay: 0` and drops
  `repeatCount`/`autoreverse`. This kills both the value-triggered transitions and the self-playing
  shimmer/pulse loops. Rejected alternative: omitting the modifier entirely — that changes the modifier
  *count*, which perturbs the `path + "$" + i` event-id scheme for later modifiers in the chain.
- **Reduce transparency → `MaterialModifier.Serialize`** emits an opaque style token, so the frosted-glass
  backends paint a solid fill instead of a blurred backdrop.
- **Text scale → Skia only.** SwiftUI (Dynamic Type), Compose (`sp`), the browser (`rem`), GTK and WinUI
  all scale their own text from the OS setting. Skia does not: `SkiaTheme.MakeFont` must multiply by
  `Accessibility.TextScale`. Applying the multiplier in Core would double-scale on the other five.

This is testable purely in Core, which matters — the test project is `net10.0` on macOS.

## Decision 4 — the Skia accessibility engine

The other six backends are plumbing. Skia is a build. It has no controls to inherit from, so it must
publish an accessibility tree of its own and adapt it to each host's platform API.

### The snapshot

After each layout pass, `SkiaBridge` produces a flat, immutable snapshot from the laid-out `SkiaNode` tree:

```csharp
public readonly record struct SkiaAccessibilityElement(
    string  Id,                 // node id — the same id events emit on
    string  Role,               // "button", "text", "adjustable", "image", …
    string  Label,
    string  Value,
    string  Hint,
    SKRect  Bounds,             // canvas coordinates; hosts convert to their own space
    string  Traits,
    double  SortPriority,
    IReadOnlyList<string> Actions,   // "activate", "increment", "decrement", "toggle", "scroll", "dismiss"
    string? ParentId);

public sealed class SkiaAccessibilitySnapshot
{
    public int Version { get; }                                   // bumps only on a semantic change
    public IReadOnlyList<SkiaAccessibilityElement> Elements { get; }
}
```

`SkiaBridge` exposes `Snapshot` plus an `AccessibilityChanged` event, mirroring how `FocusChanged` already
lets a host hang a soft keyboard off the engine's focus decision. `Version` matters: iOS and Android both
want to rebuild their virtual-element arrays only when semantics actually change, not on every repaint frame.

### What goes in the tree

- **Only realized, visible nodes.** Respect the existing viewport culling, the selected `TabView` index, and
  the pushed `NavigationStack` destination — a row that isn't drawn isn't in the tree.
- **Modality.** When a `Sheet` or `Alert` is up, everything behind it is excluded outright. This is the
  single highest-value correctness rule; without it a screen reader wanders behind the dialog.
- **Traversal order.** Top-to-bottom, then leading-to-trailing by `Bounds`, with `a11ySort` (descending)
  overriding within a parent. Not tree order — a `ZStack` would read back-to-front, which is wrong.
- **`a11yGroup`:** `combine` collapses the subtree into one element whose label is the joined descendant
  labels; `ignore` drops descendants but keeps the container; `contain` marks the subtree as a container
  boundary for host-side grouping.

### Default label synthesis (Skia only)

| Node type | Role | Default label / value |
|---|---|---|
| `Text`, `Label` | `text` | the `text` prop |
| `Button`, `NavigationLink`, `Link` | `button` / `link` | the `title` prop; actions `activate` |
| `Toggle` | `toggle` | label = `label` prop, value = "on"/"off"; action `toggle` |
| `Slider` | `adjustable` | value = current, with min/max; actions `increment`/`decrement` |
| `Stepper` | `adjustable` | same |
| `TextField`, `SecureField`, `TextEditor` | `textField` | label = `placeholder`, value = text (`SecureField` reports "secure", never the characters) |
| `Picker`, `DatePicker`, `ColorPicker`, `Menu` | `button` | current selection as value |
| `ProgressView`, `Gauge` | `progress` | value as percent |
| `Image` | `image` | **hidden unless `.AccessibilityLabel` is set** — decorative by default |
| `Rectangle`, `Circle`, `Capsule`, `RoundedRectangle`, `Divider`, `Spacer` | — | hidden, *unless* the node carries a tap gesture or an explicit label, in which case `button` |
| `ScrollView`, `List`, `Form`, `Grid` | `scrollable` | action `scroll` |
| `Section` | `header` | the header slot's text |
| `Sheet`, `Alert` | `dialog` (modal trait) | title; action `dismiss` |
| `TabView` / `Tab` | `tabBar` / `tab` | tab title, `selected` trait on the active one |
| Stacks, `Group`, `ZStack` | — | transparent — not elements themselves, children promote |

An explicit modifier always wins over the synthesized default.

### Actions

Every action routes back through the *same* `SkiaBridge.Emit` path a pointer would take, so C# handlers stay
oblivious to how they were invoked:

- `activate` → the node's tap/primary event id
- `increment` / `decrement` → the same emit a scrub produces, stepped by one increment
- `toggle` → emit the negated bool
- `scroll` → adjust `ScrollOffset` by a page and invalidate
- `dismiss` → the sheet/alert's dismiss binding

### Host adapters

| Host | Adapter |
|---|---|
| **MAUI iOS** (`SwiftDotNet.Skia.Maui`) | Implement `UIAccessibilityContainer` on the `SKCanvasView`'s platform view: return `UIAccessibilityElement`s with `accessibilityFrameInContainerSpace` (converted from canvas px), label/value/hint/traits, `accessibilityActivate()` and `accessibilityIncrement()`/`Decrement()` routed to the snapshot's actions. Rebuild on `Version` change and post `UIAccessibility.post(.layoutChanged)`. |
| **MAUI Android** | `ExploreByTouchHelper` (androidx.customview) set via `ViewCompat.setAccessibilityDelegate` — `getVirtualViewAt`, `getVisibleVirtualViews`, `onPopulateNodeForVirtualView`, `onPerformActionForVirtualView`. This is the standard canvas-accessibility pattern; the snapshot maps onto it almost field for field. |
| **Silk.NET / headless desktop** | **Deferred.** Would need AT-SPI on Linux and a UIA provider on Windows — both are large, standalone efforts with no host to verify against today. Documented as unsupported, not scaffolded. |

### Custom renderers

`ISkiaRenderer` gets an **optional** accessibility contribution so third-party primitives registered through
`SkiaRenderers` can describe themselves. Default behavior with no implementation: explicit modifiers only, no
synthesized label — a custom control that says nothing stays silent rather than reading its type name.

## Decision 5 — per-backend modifier mapping

| Wire | SwiftUI shim | Compose shim | Web | GTK4 | WinUI 3 | Skia |
|---|---|---|---|---|---|---|
| `a11yLabel` | `.accessibilityLabel(Text(v))` | `semantics { contentDescription = v }` | `aria-label` | `UpdateProperty(AccessibleProperty.Label)` | `AutomationProperties.SetName` | snapshot `Label` |
| `a11yHint` | `.accessibilityHint(Text(v))` | `semantics { …Description }` | `aria-describedby` → a visually-hidden `<span>` | `AccessibleProperty.Description` | `AutomationProperties.SetHelpText` | snapshot `Hint` |
| `a11yValue` | `.accessibilityValue(Text(v))` | `stateDescription` | `aria-valuetext` | `AccessibleProperty.ValueText` | ⚠️ needs a custom `AutomationPeer` — no-op | snapshot `Value` |
| `a11yHidden` | `.accessibilityHidden(true)` | `clearAndSetSemantics {}` | `aria-hidden="true"` | `AccessibleState.Hidden` | `AccessibilityView.Raw` | excluded from snapshot |
| `a11yId` | `.accessibilityIdentifier(v)` | `testTag(v)` | `id` | `AccessibleProperty.Label`-adjacent; ⚠️ verify | `AutomationProperties.SetAutomationId` | snapshot `Id` override |
| `a11yTraits` | `.accessibilityAddTraits(…)` | `role = Role.X`, `heading()`, `selected` | `role=` / `aria-selected` | ⚠️ **role is construct-only on GTK4** — see gotcha | ⚠️ only `SetHeadingLevel`/`SetLandmarkType` | snapshot `Role`/`Traits` |
| `a11yGroup` | `.accessibilityElement(children:)` | `mergeDescendants = true` / `clearAndSetSemantics` | `role="group"` + label | `AccessibleRelation.LabelledBy` | ⚠️ partial | snapshot flattening |
| `a11ySort` | `.accessibilitySortPriority(n)` | ⚠️ no analog — traversal order only | DOM order + `aria-flowto`; ⚠️ weak | ⚠️ none | ⚠️ none | snapshot ordering |
| `a11yAction` | `.accessibilityAction(named:)` | `customActions` | ⚠️ no true analog — a visually-hidden button | ⚠️ none | ⚠️ none | snapshot `Actions` |
| `a11yLive` | ⚠️ no analog — shim posts `.announcement` on a subtree text change | `liveRegion = LiveRegionMode.X` | `aria-live` | `AccessibleProperty.Live`; ⚠️ verify | `AutomationProperties.SetLiveSetting` | host-posted announcement |

### Gotchas worth knowing now

- **GTK4 roles are construct-only.** `AccessibleRole` is a construct property on `GtkWidget` — it cannot be
  changed in `ApplyModifiers`, which runs *after* the widget exists. `GtkNode` must read the `a11yTraits`
  modifier **before** building the widget, in the construction switch, not the modifier loop. This inverts
  the usual "build then modify" order and is the one structural change GTK needs. Verify Gir.Core actually
  exposes the construct-time role parameter; if it doesn't, roles are a documented GTK no-op.
- **WinUI can't override a role without a custom `AutomationPeer`.** `AutomationProperties` covers name,
  help text, automation id, live setting and heading level — not the control type. So `a11yTraits` on WinUI
  is limited, and honestly so. WinUI has never been compiled, so treat all of it as scaffold.
- **A hint needs a real element on the Web.** `aria-description` support is patchy; the correct mapping is
  `aria-describedby` pointing at a visually-hidden `<span>`. The Web renderer already emits a `<style>`
  block ([`SwiftDotNetView.cs:44`](../src/SwiftDotNet.Web/SwiftDotNetView.cs)) — add an `.sdn-sr-only` class
  there and a sibling span next to any node carrying `a11yHint`.
- **`SecureField` must never expose its text.** Skia's synthesizer reports a placeholder value; the native
  backends already handle this.
- **Double-labelling.** Setting `.AccessibilityLabel` on a `Button` *replaces* the title on SwiftUI and
  Compose but is *additive* to the DOM's own accessible-name computation unless `aria-label` wins — it does,
  which is why `aria-label` (not `title`) is the mapping.

## Decision 6 — semantic defaults on the Web

Separate from the modifier work, and worth its own pass: the DOM the Web backend emits should be correct
before anyone reaches for a modifier.

- `Sheet` / `Alert` → `role="dialog" aria-modal="true"`, focus moved in on open, focus trapped, Escape
  dismisses, focus restored on close. **This is the biggest single Web defect** — a modal that doesn't trap
  focus is unusable with a screen reader.
- `TabView` → `role="tablist"` / `role="tab"` (`aria-selected`, `aria-controls`) / `role="tabpanel"`.
- `List` → `role="list"` with `role="listitem"` rows; selection via `aria-selected`.
- `Section` → `<section aria-labelledby>` with a real heading element for the header slot.
- `DisclosureGroup` → `aria-expanded` on the existing toggle button.
- `Picker` → a real `<select>` where the options are static; `role="combobox"` otherwise.
- `Stepper` → two real `<button>`s with labels, not divs.
- `Menu` → `role="menu"` / `role="menuitem"` with arrow-key navigation.

## Decision 7 — keyboard operability on Skia

Screen-reader support and keyboard support are different problems, and Skia fails both. Today
`SkiaBridge.FocusedId` only ever points at a text control. The accessibility snapshot already computes the
right traversal order, so keyboard focus should reuse it:

- Tab / Shift-Tab walk the snapshot's focusable elements; Enter/Space activate; arrows adjust an
  `adjustable`; Escape dismisses a modal.
- A visible focus ring painted by `SkiaNodePaint`, respecting `Accessibility.HighContrast`.
- Focus is confined to the modal subtree when a `Sheet`/`Alert` is up — the same modality rule as the tree.

This benefits every Skia host including the desktop ones, which will never get a screen-reader adapter.

## Phasing

Each phase is independently shippable, because unknown modifier types are ignored by every backend.

| Phase | Scope | Files |
|---|---|---|
| **1 — Core** | The ten modifiers, `Accessibility` statics + `$a11y` channel, reduce-motion / reduce-transparency folding, `AccessibilityTraits`/`AccessibilityChildren`/`Live` enums, tests | [`Modifier.cs`](../src/SwiftDotNet/Core/Modifier.cs), [`ViewModifiers.cs`](../src/SwiftDotNet/Core/ViewModifiers.cs), new `Core/Accessibility.cs` |
| **2 — Native-fidelity backends** | Modifier mapping + settings emitters for SwiftUI, Compose, Web, GTK, WinUI(scaffold) | [`Bridge.swift`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift), [`Bridge.kt`](../native/SwiftDotNetComposeBridge/src/main/kotlin/com/swiftdotnet/bridge/Bridge.kt), [`SwiftDotNetView.cs`](../src/SwiftDotNet.Web/SwiftDotNetView.cs), [`GtkNode.cs`](../src/SwiftDotNet.Gtk/GtkNode.cs), [`WinNode.cs`](../src/SwiftDotNet/Platforms/Windows/WinNode.cs) |
| **3 — Skia engine** | Snapshot, roles, default labels, actions, ordering, modality; Text scale in `SkiaTheme` | [`SkiaBridge.cs`](../src/SwiftDotNet.Skia/SkiaBridge.cs), [`SkiaNode.cs`](../src/SwiftDotNet.Skia/SkiaNode.cs), new `SkiaAccessibility.cs` |
| **4 — Skia hosts** | `UIAccessibilityContainer` (MAUI iOS), `ExploreByTouchHelper` (MAUI Android) | [`SwiftDotNetSkiaView.cs`](../src/SwiftDotNet.Skia.Maui/SwiftDotNetSkiaView.cs) |
| **5 — Semantics & focus** | Web DOM semantics + modal focus trap; Skia keyboard traversal + focus ring; live regions; programmatic focus | Web renderer, `SkiaNodePaint.cs`, `SkiaPointerRouter.cs` |
| **6 — Deferred** | AT-SPI (Linux) and UIA (Windows) providers for desktop Skia; macOS screen-reader detection; a contrast-ratio lint for `Theme` | — |

Recommended order if only part of this gets built: **1 → 3 → 4**. Phase 2 raises the ceiling on backends
that are already partly usable; Phases 3–4 take Skia from *nothing* to usable, which is where the real
accessibility risk lives — and it's also where the framework has no fallback story at all.

## Testing

**Automated** (runs on macOS, which is what the test project targets):

- `AccessibilityWireTests.cs` — each modifier's serialized shape; traits token joining; reduce-motion
  collapsing an `AnimationModifier` (including the repeating form); reduce-transparency on `Material`;
  `$a11y` payload parsing, de-duplication, and malformed-payload handling (mirror `SafeAreaTests.cs`).
- `SkiaAccessibilityTreeTests.cs` — role and default label per node type; decorative `Image`/shape exclusion;
  offscreen rows culled; **everything behind a `Sheet` excluded**; bounds equal the node's `Frame`;
  traversal order for a `ZStack` and for `a11ySort`; each action emitting the same event id a pointer does;
  `SecureField` never leaking text; `Version` not bumping on a repaint-only frame.
- Skia keyboard traversal tests once Phase 5 lands, in the style of `SkiaPointerRouterTests.cs`.

**Manual**, because no test harness substitutes for it — record the result in the docs status table with
*where* it was verified:

| Screen reader | Target |
|---|---|
| VoiceOver | iOS simulator (SwiftUI **and** Skia-MAUI), macOS desktop |
| TalkBack | Android emulator (Compose **and** Skia-MAUI) |
| VoiceOver / NVDA | the Blazor sample in Safari / Chrome |
| Orca | the GTK sample |
| Narrator | WinUI — after it compiles for the first time |

The sample [`ContentView`](../sample/SharedUI/ContentView.cs) should grow an accessibility section that
exercises labels, hints, grouping, a custom action, and a reduce-motion-sensitive animation, so every manual
pass has the same script.

## Documentation (part of "done")

- **New `docs/accessibility.md`** — the feature page, in house style: what it is, the fluent snippet, the
  per-backend table, the gotchas, an honest status column. Linked from
  [`docs/README.md`](../docs/README.md) under "Authoring UI".
- **[`docs/modifiers-gestures-animation.md`](../docs/modifiers-gestures-animation.md)** — the modifiers
  themselves, and reduce-motion's effect on `.Animation`.
- **[`docs/backends/*.md`](../docs/backends/README.md)** — a per-backend accessibility section; delete the
  "no native accessibility" claim from [`skia.md`](../docs/backends/skia.md) and the
  [README](../README.md) only once Phase 4 is verified on a device, not before.
- **[`docs/global-styles.md`](../docs/global-styles.md)** — `Accessibility.TextScale` / `HighContrast` as
  inputs a `Theme` should respect.

## Open questions

1. **`AccessibilityTraits` as `[Flags]` enum vs. a params list.** Flags read closer to SwiftUI's
   `AccessibilityTraits` option set and serialize cleanly to a comma-joined token; a params list is easier to
   extend without a breaking numeric layout. Leaning flags, matching `Edge`.
2. **Should `.AccessibilityLabel` on a container imply `children: .combine`?** SwiftUI requires both. Being
   stricter than SwiftUI is a trap for anyone porting; being looser is a divergence to document. Leaning
   "match SwiftUI exactly".
3. **Does Gir.Core expose GTK4's construct-time `AccessibleRole`?** Determines whether GTK roles are
   supported or a documented no-op. Needs a spike on a Linux box.
4. **Web screen-reader detection is impossible** — so `Accessibility.IsScreenReaderRunning` is permanently
   `false` there. Is a permanently-false property worse than no property? Leaning keep-it: the alternative is
   a `#if`-shaped API, and consumers should be writing UI that works either way regardless.
5. **Does the accessibility snapshot belong in `SwiftDotNet.Skia`, or in Core** as a backend-agnostic
   description a *future* backend could reuse? Skia is the only backend that will ever need it, and Core is
   meant to stay dependency-free — but `SKRect` in the snapshot is what ties it to Skia. Leaning Skia, with
   a plain `(x,y,w,h)` struct if it ever needs to move.
