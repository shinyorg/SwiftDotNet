# Plan: Live Activities & rich notifications (`SwiftDotNet.Live`)

**Status:** **Implemented** (both pure-C# libraries tested; platform drivers compile, never run) · **Date:** 2026-08-20
**Save to (repo convention):** `plans/live-activities-plan.md`

> **Scope note.** This plan covers three surfaces: **iOS Live Activities** (lock screen + Dynamic Island),
> **Android custom-content notifications** (`RemoteViews`), and **Android 16 Live Updates**
> (`Notification.ProgressStyle`). It deliberately excludes:
>
> - **Push delivery** — obtaining an APNs push token for an activity, FCM plumbing, server payload shaping.
>   That is transport, not rendering; it belongs with the app's push library. This plan assumes the payload
>   arrives and asks only what we do with it.
> - **Home-screen widgets, control widgets, app widgets, Wear tiles, watchOS complications.** Every one of
>   them is unlocked *cheaply* by the machinery below (they share the widget SwiftUI subset on Apple and
>   compile to `RemoteViews` on Android via Glance), but each adds its own build plumbing and its own
>   refresh-budget model. Follow-on, tracked in §Follow-on surfaces.
> - **Notification *content* extensions on iOS** (`UNNotificationContentExtension`). These are the true
>   analog of an Android custom notification and are discussed in §Crux 1 as a rejected/deferred route,
>   because they are the one place UIKit is fully available and the existing bridge might run unmodified.

## Built — 2026-08-20

Both plans were implemented end-to-end rather than phase by phase. What exists, and where it diverged from
the design above:

| Piece | Where | State |
|---|---|---|
| `LiveView` vocabulary, modifiers, compact wire + reader, validator, lowering | [`src/SwiftDotNet.Live`](../src/SwiftDotNet.Live) | ✅ 41 headless tests |
| `LiveActivity<T>` slots, combined byte budget, action collection | same | ✅ tested |
| `LiveUpdate` (Android 16 `ProgressStyle` data model) | same | ✅ built |
| `ISurfaceChannel` + `FileSurfaceChannel` + `LiveActionRouter` | same | ✅ tested |
| `RemoteViews` interpreter, notification driver, app-widget provider, `AndroidCanvas` + bitmap route | [`src/SwiftDotNet.Live.Android`](../src/SwiftDotNet.Live.Android) | 🧩 compiles; never run |
| App Group channel, ActivityKit + WidgetCenter P/Invoke | [`src/SwiftDotNet.Live.Apple`](../src/SwiftDotNet.Live.Apple) | 🧩 compiles; never run |
| `SDNLiveView`, `SDNTimelineProvider`, `SDNActivityAttributes`, `SDNLiveActionIntent`, `@_cdecl` bridge | [`native/SwiftDotNetWidgets`](../native/SwiftDotNetWidgets) | 🧩 xcframework builds (iOS device + sim) |

User-facing reference: [`docs/live-surfaces.md`](../docs/live-surfaces.md).

### Where the build diverged from this plan

1. **Open question 1 dissolved rather than being answered.** The plan worried that `View.BuildNode` is
   `internal`, so a satellite package could not build nodes. It does not need to: `Node` and
   `NodeJson` are already public, and `LiveView` is not a `View` — it emits `Node` directly. So the
   vocabulary lives in `SwiftDotNet.Live` with no new seam in Core, and this plan no longer blocks on
   [`view-construction-seam.md`](view-construction-seam.md). (The car plan's version of the question is
   unaffected — a car template genuinely wants to be a `View`.)

2. **No Kotlin on Android, and no new `.aar`.** The plan assumed a Kotlin `RemoteViewsInterpreter` in the
   Compose bridge. Building it revealed that unnecessary: a `RemoteViews` is a serialized recipe, not a
   view, and an `AppWidgetProvider` is a `BroadcastReceiver` in *our* process — so C# builds the tree
   directly against `Android.Widget.RemoteViews`, and the primitive layouts ship as `AndroidResource` from
   the .NET library. This also keeps notification/widget consumers off the Compose dependency entirely.

3. **`SwiftDotNetWire` was not extracted.** The plan called for splitting the decode layer out of
   `Bridge.swift` and sharing it. Once the compact wire existed, the two decoders shared almost nothing —
   different keys, different type names, different collection handling — so `SwiftDotNetWidgets` carries
   its own ~90-line decoder and `Bridge.swift` is untouched. Lower risk, less coupling.

4. **`LiveTimer` was not in the design and is the most valuable node in it.** A self-ticking clock costs
   *zero* activity updates and *zero* `notify()` calls for a running countdown, which is the single most
   common thing these surfaces display. It also produced the sharpest limitation of the bitmap route
   (`SDNL030`), since a still frame cannot tick.

5. **Route B turned out nearly free, via lowering rather than a new rasterizer.** Because the live
   vocabulary is a strict subset of the main DSL, `LiveLowering` rewrites type names onto core nodes and
   the existing headless engine does the rest — Android supplies only an `ICanvas`. This is the first time
   the [renderer seam](../docs/backends/skia.md) has been implemented against a platform toolkit rather
   than another Skia binding.

6. **`requestPromotedOngoing` is not bound** in Android SDK 36.1.69, so the Live Update promotion goes
   through JNI, guarded and wrapped. Without it the notification is still correct; it just gets no
   status-bar chip.

### Still unbuilt

Widget configuration (`WidgetConfigurationIntent` generated from C#), collection widgets (rejected by
design), an Apple bitmap route (rejected — a push-updated activity would show a stale image), and a
`dotnet new` template for the Xcode widget-extension target.

### Still unverified

All five Phase-0 flags remain open, because each needs a device: the widget extension memory budget,
whether a custom `Layout` survives WidgetKit archiving, whether `LiveActivityIntent.perform()` really runs
in the app process, real `RemoteViews` parcel limits, and the WidgetKit refresh budget. Nothing in either
platform driver has been run on hardware or a simulator — they compile, and that is the whole claim.

## Context — the first targets where we do not own the process

Live Activities and custom notifications look like two more backends. They aren't. Every backend the repo
has today — SwiftUI shim, Compose shim, GTK, WinUI, Web, Skia, TUI — shares one assumption: **our process
hosts the renderer, and C# drives it by pushing patches**. `IBridge.Render(json)` is called by us, when we
decide, as often as we like.

Neither surface here works that way.

| | iOS Live Activity | Android custom notification | Android 16 Live Update |
|---|---|---|---|
| Renders in | WidgetKit **extension** process; view hierarchy archived and drawn by SpringBoard | **SystemUI** process inflates our `RemoteViews` | SystemUI, from a fixed template |
| Who triggers a render | The **system**, when it decides to | The system, on `notify()` | The system, on `notify()` |
| Can .NET run there | **No** — separate binary, `@main WidgetBundle` must be Swift, tight memory budget | **No** — SystemUI inflates the views, our code is not on the stack | **No** |
| Existing shim reusable | Decode layer yes, drive loop no | Not at all (`Bridge.kt` is Compose; SystemUI cannot host Compose) | n/a — no view tree at all |
| Vocabulary | Restricted SwiftUI subset | `@RemotableViewMethod` setters on whitelisted view classes | Segments, points, tracker icon |
| Interactivity | `Link` / `widgetURL` / `Button(intent:)` only | `PendingIntent` on the whole view + action buttons | Tap + actions |

The consequence is stated once and then assumed everywhere below: **we ship a tree as data, and something
that is not our code renders it.** That is the whole plan.

## Crux 1 — the renderer must be reimplemented, twice, in the host's language

### Apple

A Live Activity's UI is declared in a WidgetKit extension:

```swift
ActivityConfiguration(for: SDNActivityAttributes.self) { context in
    SDNLiveView(context.state.tree)          // lock screen / banner
} dynamicIsland: { context in
    DynamicIsland {
        DynamicIslandExpandedRegion(.leading)  { SDNLiveView(context.state.leading) }
        // …
    } compactLeading:  { SDNLiveView(context.state.compactLeading) }
      compactTrailing: { SDNLiveView(context.state.compactTrailing) }
      minimal:         { SDNLiveView(context.state.minimal) }
}
```

`SDNLiveView` must be **pure Swift**. It is a stripped fork of the decode half of
[`Bridge.swift`](../native/SwiftDotNetBridge/Sources/SwiftDotNetBridge/Bridge.swift) — `PropValue`,
`ModifierData` and `WireNode` (`Bridge.swift:15-71`) transfer essentially verbatim, since `WireNode` is
already `Decodable` from our JSON. What does **not** transfer is everything below it: the `@Observable`
`VNode` mirror, the `Patch`/`PatchOp` applier, `swiftdotnet_render`, `swiftdotnet_set_event_callback`, the
host controller. The extension is handed a whole state, not a patch, and it has no C# to call back into.

So the Swift side splits in two:

| Target | Contents | Consumed by |
|---|---|---|
| `SwiftDotNetWire` (new) | `PropValue`, `ModifierData`, `WireNode`, colour/font/brush parsing | both |
| `SwiftDotNetBridge` (existing) | patch applier, `@Observable` mirror, C ABI, host controller, full view catalogue | the app |
| `SwiftDotNetWidgets` (new) | `SDNLiveView` — the **restricted** node→SwiftUI builder, no C ABI | the widget extension |

Extracting `SwiftDotNetWire` out of the 1,945-line `Bridge.swift` is a prerequisite and is mechanical.

**Rejected: running .NET in the widget extension.** .NET *can* build iOS app extensions, but a widget
extension is memory-capped hard (the commonly cited figure is ~30 MB; **verify in Phase 0**), is launched
and killed by the system at unpredictable times, and would need the runtime cold-started before any pixel
appears. Worse, `WidgetBundle`/`ActivityConfiguration` are SwiftUI result-builder types — the same reason
the Apple backend is a shim in the first place ([`docs/backends/apple.md`](../docs/backends/apple.md)).
There is no version of this that is better than a 300-line Swift interpreter.

**Deferred, not rejected: `UNNotificationContentExtension`.** For *ordinary* rich push notifications (not
Live Activities), iOS gives you a full `UIViewController` in an extension — full UIKit, so
`swiftdotnet_make_host_controller()` would work essentially as-is. That is genuinely interesting and is the
only place the existing bridge runs unmodified in an extension. It is out of scope here because it answers
a different question than "Live Activity", and it still needs the .NET-in-an-extension question answered.
Revisit after Phase 3.

### Android

Notification content is `RemoteViews`: a serialized recipe (`layoutId` + a queue of reflective setter
calls) that crosses Binder and is inflated **in SystemUI**. Compose cannot run there. Glance *does* compile
Composables down to `RemoteViews`, but Glance targets app widgets and Wear tiles — it has no notification
surface. So `Bridge.kt` contributes nothing.

**Adopted: a `RemoteViews` tree interpreter.** Ship a small set of precompiled primitive layouts in the
`.aar` and compose them at runtime:

| Layout resource | Node types |
|---|---|
| `sdn_vstack.xml` / `sdn_hstack.xml` | vertical / horizontal `LinearLayout` |
| `sdn_zstack.xml` | `FrameLayout` |
| `sdn_text.xml` | `Text`, `Label` |
| `sdn_image.xml` | `Image`, shapes-as-bitmap |
| `sdn_progress.xml` | `ProgressView`, `Gauge` |
| `sdn_button.xml` | `Button` |
| `sdn_bitmap.xml` | the §Route B escape hatch — one full-bleed `ImageView` |

Nesting is `RemoteViews.addView(containerId, childRemoteViews)`. Modifiers lower onto `@RemotableViewMethod`
setters: `setTextViewText`, `setTextColor`, `setTextViewTextSize`, `setViewPadding`, `setViewVisibility`,
`setImageViewBitmap`, `setInt(id, "setBackgroundColor", …)`, and — **API 31+ only** — `setColorStateList`,
`setViewLayoutWidth` / `setViewLayoutHeight` / `setViewLayoutMargin`. That API-31 cliff is decisive: below
it there is no runtime control of size or margin at all, so the interpreter's honest floor is **API 31**,
against the sample's current `minSdk 24`. Below 31 we fall back to Route B.

## Crux 2 — the existing `View` DSL cannot be the live DSL

Same argument as [CarPlay/Android Auto](car-backends-plan.md#crux-1--the-existing-view-dsl-cannot-be-the-car-dsl),
for a different reason: not a fixed template catalogue, but a **subset with silent failure**.

The widget SwiftUI subset is roughly: stacks, `Text`, `Image`, `Label`, `Gauge`, `ProgressView`, shapes,
gradients, `Link`, `Button(intent:)`, `Toggle(isOn:intent:)`. Not available: `ScrollView`, `List`,
`TextField`, `TextEditor`, `Picker`, `DatePicker`, `Slider`, `Stepper`, `Menu`, `TabView`, `WebView`, `Map`,
`UIViewRepresentable`, arbitrary `withAnimation`, `TimelineView`. Custom `Layout` conformances are believed
to work — which would rescue `SDNGridLayout` and `SDNAbsoluteLayout` — **verify in Phase 0**, it is worth a
real device check because it decides whether `Grid` and `AbsoluteLayout` are in or out of the subset.

The `RemoteViews` whitelist is narrower still and overlaps only partly.

**Rejected: reuse `View` and drop what doesn't map.** The failure mode is a lock-screen or notification UI
the developer cannot see in the simulator's normal run loop, silently missing half its content. Same
objection as the car plan: an explicit vocabulary makes the constraint a **build-time** fact.

**Adopted: a parallel `LiveView` vocabulary over the same pipeline**, plus a validator. New view types,
same `Node`, same `NodeJson`, same `State<T>`. The validator walks the built tree and throws on any node
type or modifier outside the intersection of the two subsets, naming the node and the platform — the
`SDN1xxx` analyzer family is the eventual home, a runtime throw is the Phase-1 version.

## Crux 3 — there is no event callback

`IBridge.SetEventHandler` and `SwiftApp`'s `_actions` dictionary (`SwiftApp.cs:24,166`) assume the host can
call us. Neither host can.

| | Mechanism | Lands in |
|---|---|---|
| iOS, tap whole activity | `widgetURL(_:)` / `Link` | app foreground, via URL |
| iOS, button | one generic `AppIntent` with `@Parameter var nodeId: String` | see below |
| Android, tap | `setOnClickPendingIntent` (whole view or per-view) | our broadcast receiver |
| Android, action buttons | `Notification.Action` + `PendingIntent` | our broadcast receiver |

The Apple half has a genuinely good answer: conform the intent to **`LiveActivityIntent`** (iOS 17.2+) and
`perform()` runs **in the app's process**, in the background — so it can call straight into
`SwiftApp`'s action dispatch and reuse the existing `_actions` lookup keyed by node id. A plain `AppIntent`
would run in the extension where there is no C#. **Verify in Phase 0**; it is the single fact this plan's
interactivity story rests on. Note it raises the Apple deployment floor for interactive activities to
**17.2**, above the bridge's current 17.0 — non-interactive activities still work from 16.1.

Android needs no such trick: a `PendingIntent` to our own `BroadcastReceiver` is always our process, and
routes to the same dispatch.

The pleasing outcome is that **`SwiftApp`'s event round-trip is reused verbatim on both platforms** — only
the transport in front of it is new.

## Crux 4 — the wire has a hard byte budget

An APNs Live Activity update carries the whole `ContentState`, and the payload ceiling is **4 KB**. Local
`activity.update()` is more generous but not unbounded. Nothing else in this repo has ever had a size
budget on the wire.

`NodeJson.Serialize` (`NodeJson.cs:12`) emits the full `{id,type,props,modifiers,children}` shape with long
key names — fine over a C ABI, wasteful at 4 KB. Options, in order of preference:

1. **Compact wire** — single-letter keys, modifiers as a positional array, omit empties. Purely a second
   serializer over the same `Node`; the Swift decoder gets a second `CodingKeys`. Cheap, ~50-60% smaller.
2. **Interned vocabulary** — map node types and modifier types to small ints via a shared generated table.
   Another ~15%, at the cost of a table both sides must agree on. Do it only if (1) misses.
3. **Deltas** — send `TreeDiffer` patches instead of whole trees. **Rejected**: the extension is stateless
   between system-driven renders, so there is no prior tree to patch against.

Design rule for the DSL: budget **2 KB** for the serialized tree and have the validator warn past it and
throw past 4 KB, with the byte count in the message. A developer who blows the budget must find out at
their desk, not from a silently-not-updating activity on a tester's phone.

## Crux 5 — one activity, five presentations

A Live Activity must supply: lock screen/banner, Dynamic Island **compact leading**, **compact trailing**,
**minimal**, and **expanded** (itself four regions: leading, trailing, center, bottom). These are not one
tree scaled down — a minimal presentation is typically a single glyph.

So the DSL is **slot-based**, not a single `Body`:

```csharp
public sealed class DeliveryActivity : LiveActivity<DeliveryState>
{
    public override LiveView LockScreen(DeliveryState s) =>
        new LiveVStack(
            new LiveText($"{s.Courier} — {s.Eta:t}").Font(LiveFont.Headline),
            new LiveProgress(s.Fraction).Tint(Colors.Green)
        ).Padding(12);

    public override LiveView CompactLeading(DeliveryState s)  => new LiveImage("truck");
    public override LiveView CompactTrailing(DeliveryState s) => new LiveText(s.Eta.ToString("t"));
    public override LiveView Minimal(DeliveryState s)         => new LiveImage("truck");

    public override LiveView Expanded(DeliveryState s) =>
        new LiveExpanded()
            .Leading(new LiveImage("truck"))
            .Trailing(new LiveText(s.Eta.ToString("t")))
            .Bottom(new LiveButton("Cancel", () => _svc.Cancel(s.Id)));
}
```

Android fills slots it has an analog for (`LockScreen` → the expanded custom content; `CompactLeading` +
`CompactTrailing` → the collapsed content) and ignores the rest. Slots without an analog on a platform are
a documented no-op, in the same spirit as the existing per-backend fallback tables.

## Route B — the bitmap escape hatch

The repo already has, sitting unused for this purpose, a complete headless tree→pixels path:
`VisualBridge.Render(json)` then `VisualBridge.Draw(canvas, size, dark)`
([`VisualBridge.cs:92,114`](../src/SwiftDotNet.Graphics/VisualBridge.cs)) over the `ICanvas` seam. Point it
at an `android.graphics.Canvas` (or reuse the Skia backend directly) and every existing view, modifier and
gradient renders into a `Bitmap`, which goes into `sdn_bitmap.xml` via `setImageViewBitmap`.

| | Route A — `RemoteViews` interpreter | Route B — bitmap |
|---|---|---|
| Fidelity | the whitelist only | anything the Skia backend can draw |
| Min API | 31 | 24 |
| Accessibility | real views, TalkBack reads them | one `ImageView` — `contentDescription` is all we have |
| Hit testing | per-view `PendingIntent` | whole-notification tap + action buttons only |
| Theme / font scale | system handles it | we must re-render and re-`notify()` |
| Transport cost | small parcel | bitmap crosses Binder; the notification's total budget is ~1 MB and an oversized one is dropped with *Bad notification posted* |

Both ship. Route A is the default, Route B is `.RenderMode(LiveRenderMode.Bitmap)` — an explicit,
documented trade of accessibility for fidelity. Cap the bitmap at roughly 400×256 dp ARGB and refuse larger
with a clear exception rather than letting the system drop the notification.

**On iOS, Route B is much weaker** and is *not* in scope for Phase 4. The extension could show an
`Image(uiImage:)` loaded from a shared App Group container, but only the app can render it — so a
**push-updated** activity, where the system wakes the extension without waking us, would show a stale
image. Local-update-only activities could use it. Documented as a known asymmetry; revisit only on demand.

## Android 16 Live Updates are a different animal

`Notification.ProgressStyle` + `requestPromotedOngoing(true)` (API 36) is the real structural analog of a
Live Activity — status-bar chip, lock-screen prominence, push-updatable. But it is **templated**: you supply
segments, points, a tracker icon and text. There is **no view tree**.

So it must not be modelled as a rendering target. It is a *data* type:

```csharp
new LiveUpdate()
    .Progress(s.Fraction)
    .Segments(seg => seg.Add(0.4, Colors.Green).Add(0.6, Colors.Gray))
    .TrackerIcon("truck")
    .Title($"{s.Courier} — {s.Eta:t}");
```

An app targeting both platforms therefore declares a `LiveActivity` *and* may declare a `LiveUpdate`; where
both exist on Android 16+, the `LiveUpdate` wins and the custom `RemoteViews` content is the fallback for
older releases. Keeping them separate types is the point — pretending `ProgressStyle` accepts a tree would
reintroduce exactly the silent-drop failure mode Crux 2 rejects.

## What is shared, precisely

| Piece | Shared? | Note |
|---|---|---|
| `State<T>` ([`State.cs`](../src/SwiftDotNet/Core/State.cs)) | ⚠️ partly | state changes must be **coalesced** into an `update()` call, not a render per set — activities have an OS update budget |
| `Node` / `NodeBuilder` / `RenderContext` | ✅ verbatim | live views are just node types |
| `NodeJson` | ⚠️ + compact variant | see Crux 4 |
| `TreeDiffer` | ❌ n/a | the extension is stateless between renders; whole trees only |
| `SwiftApp` action dispatch (`_actions`, `SwiftApp.cs:166`) | ✅ verbatim | intent / `PendingIntent` feed the same lookup |
| `IBridge` | ❌ | neither surface has a live bridge to push to; a new `ILiveHost` replaces it |
| `Core/Hosting` (DI, `CreateBuilder`) | ✅ verbatim | an activity resolves services like any view |
| Layout (`GridEngine`, `AbsoluteLayoutBounds`) | ⚠️ | Apple: only if custom `Layout` works in widgets (Phase 0). Android: never — lower `Grid` to nested stacks or Route B |
| Modifiers / styles / theme | ⚠️ subset | the validator is the contract |
| Renderer seam (`SwiftDotNet.Graphics`) | ✅ **Route B only** | already headless; needs an Android `ICanvas` |
| `Bridge.swift` decode layer | ✅ after extraction | → `SwiftDotNetWire` |
| `Bridge.swift` patch applier / C ABI | ❌ | |
| `Bridge.kt` | ❌ | Compose cannot reach SystemUI |

New surface is roughly **~12 view types + 2 interpreters + 1 Swift target split**, not a backend from
scratch.

## Packages

Follows the [Maps](../docs/maps.md#packages) companion pattern — Core stays dependency-free, the SDK weight
stays opt-in.

| Package | Role |
|---|---|
| `src/SwiftDotNet.Live` | `LiveActivity<T>`, the `LiveView` vocabulary, `LiveUpdate`, the compact serializer, the validator, and `ISurfaceChannel` (defined in [`widgets-plan.md`](widgets-plan.md#crux-1--the-channel-is-a-mailbox-not-a-socket), shared by both). Reflection-free, no platform deps. |
| `src/SwiftDotNet.Live.Apple` | `ActivityKit` P/Invoke + `SwiftDotNetLive.targets` (`NativeReference` does not flow transitively — same constraint as the bridge and Maps) |
| `src/SwiftDotNet.Live.Android` | `NotificationManager` / `RemoteViews` driver, the broadcast receiver, Route B canvas |
| `native/SwiftDotNetWidgets` | `SDNLiveView` — the Swift widget-subset interpreter, built into the consumer's widget extension |
| `native/SwiftDotNetComposeBridge` (existing aar) | `+ sdn_*.xml` layouts and `RemoteViewsInterpreter.kt` |

**The build plumbing is the real cost on Apple**, and it should be stated plainly rather than discovered in
Phase 2: a consumer needs an actual **Xcode widget-extension target** in their app, embedding
`SwiftDotNetWidgets`. There is no "just add a NuGet" story for a Live Activity. The mitigation is a
`dotnet new` template plus a documented manual recipe, not cleverness.

## Phases

| Phase | Deliverable | Gate |
|---|---|---|
| **0 — spike** | Answer the five *verify* flags: widget memory budget; does a custom `Layout` work in a Live Activity; does `LiveActivityIntent.perform()` really run in-app; `RemoteViews.addView` depth/parcel limits in practice; actual `ProgressStyle` behaviour on an API-36 emulator. Two throwaway apps, one per platform. | **Go/no-go for the whole plan.** If `LiveActivityIntent` does not run in-process, interactivity on Apple is deep-link-only and the DSL changes. |
| **1 — Android, Route A** | `SwiftDotNet.Live` vocabulary + validator + compact wire; `RemoteViewsInterpreter.kt`; tap/action routing. Emulator-verified. | Cheapest real pixels — no extension plumbing at all, and it settles the DSL against the *narrower* of the two subsets. |
| **2 — Apple** | `SwiftDotNetWire` extraction; `SwiftDotNetWidgets`; `ActivityKit` driver; sample widget-extension target + `.targets`. Simulator- and device-verified (Dynamic Island needs a Pro device or the 15 Pro sim). | The plumbing risk lands here. |
| **3 — interactivity + push** | `LiveActivityIntent` ↔ `PendingIntent` → `SwiftApp` dispatch; push-token surfacing so an app's own push stack can drive updates; update coalescing + budget guard. | |
| **4 — Route B** | Android `ICanvas` over `android.graphics.Canvas`; `LiveRenderMode.Bitmap`; size guard. | Independently useful; unblocks API 24-30. |
| **5 — Live Updates + islands** | `LiveUpdate` / `ProgressStyle` on API 36; the four Dynamic Island expanded regions; `.staleDate` / dismissal policy. | |

Phases 1 and 2 are independently shippable and Phase 1 does not block on any Apple unknown — which is why
Android goes first despite iOS being the surface people ask for.

## Open questions

1. **Where does the vocabulary live?** Same trap the car plan hit: `View.BuildNode` is `internal`, so a
   parallel vocabulary in a satellite package cannot build nodes. Either `LiveView` lives in Core (weight,
   but no new seam) or Core grows a public construction seam. This is the same unresolved question as
   [`view-construction-seam.md`](view-construction-seam.md) and should be answered once, for both.
2. **Does `State<T>` drive an activity at all?** An activity is updated from *background* code — a job, a
   push handler — often with no view tree alive. It may be that activities take a plain immutable state
   struct and never touch `State<T>`. Leaning that way; decide in Phase 1.
3. **Update budget.** iOS throttles frequent activity updates and Android rate-limits `notify()`. Do we
   coalesce silently, or throw? Suggest: coalesce, and expose a counter in DevTools.
4. **Font and image assets.** The extension has its own bundle; `LiveImage("truck")` must resolve to an
   asset *the extension can see*. Either a shared asset catalogue, or SF Symbols / Android drawables only.
   Leaning: symbols/drawables in Phase 2, arbitrary assets later.
5. **Dark mode and tint.** Live Activities are always on a lock screen of unknown brightness and Android
   notifications follow system theme. Colours probably need to be *semantic* in this vocabulary, not the
   literal `Brush` strings the main DSL uses.

## Follow-on surfaces

**Home-screen widgets now have their own plan: [`plans/widgets-plan.md`](widgets-plan.md).** It depends on
this one for the `LiveView` vocabulary and both interpreters, and owns the three things this plan does not:
the **app↔surface channel** (a Live Activity carries its state inside the ActivityKit payload; a widget
has no carrier), the **pull-vs-push refresh** reconciliation, and **families/sizing/configuration**. The
channel it defines (`ISurfaceChannel`) lands in `SwiftDotNet.Live` and is used by *both* plans — activities
need it for the same "which surfaces are live, and what did the user tap while we were suspended" questions.

Still unclaimed by either plan:

| Surface | Reuses | Extra work |
|---|---|---|
| iOS control widgets (18+) | — | a separate, tiny vocabulary |
| Wear OS tiles | `RemoteViews`-ish (`ProtoLayout`) | a third interpreter — probably not worth it |
| watchOS Live Activity presentation | free — iOS 18+ mirrors activities to the Smart Stack | verify sizing |
| `UNNotificationContentExtension` | possibly the **whole existing bridge**, unmodified (§Crux 1) | answer .NET-in-an-extension first |

## Cross-links

- [`docs/backends/apple.md`](../docs/backends/apple.md) — the shim route and why SwiftUI can't be authored from C#
- [`docs/backends/android.md`](../docs/backends/android.md) — the Compose shim
- [`plans/widgets-plan.md`](widgets-plan.md) — the companion plan: home-screen widgets and the shared channel
- [`plans/car-backends-plan.md`](car-backends-plan.md) — the other "host won't let us draw" plan; the
  parallel-vocabulary argument is shared
- [`plans/view-construction-seam.md`](view-construction-seam.md) — Open question 1
- [`docs/maps.md`](../docs/maps.md) — the companion-package + `.targets` pattern this follows
- [`src/SwiftDotNet.Graphics/VisualBridge.cs`](../src/SwiftDotNet.Graphics/VisualBridge.cs) — Route B
