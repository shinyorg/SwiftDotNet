# Live Surfaces — Live Activities, notifications & widgets

The `SwiftDotNet.Live` / `SwiftDotNet.Widgets` companions render C#-declared UI on the surfaces the
**system** draws for you: iOS **Live Activities** (lock screen + Dynamic Island), **home-screen and
lock-screen widgets**, Android **custom-content notifications**, Android app widgets, and Android 16
**Live Updates**.

```csharp
public sealed class DeliveryActivity : LiveActivity<DeliveryState>
{
    public override string Kind => "delivery";

    public override LiveView LockScreen(DeliveryState s) =>
        new LiveVStack(
                new LiveText(s.Courier).Font(Font.Headline),
                new LiveTimer(s.Eta),                         // ticks by itself, costs nothing
                new LiveProgress(s.Fraction).Tint(Color.Green))
            .Spacing(8)
            .Padding(12);

    public override LiveView? CompactLeading(DeliveryState s)  => new LiveImage("truck");
    public override LiveView? CompactTrailing(DeliveryState s) => new LiveTimer(s.Eta);

    public override LiveExpanded? Expanded(DeliveryState s) =>
        new LiveExpanded()
            .Leading(new LiveImage("truck"))
            .Bottom(new LiveButton("Cancel", () => _svc.Cancel(s.Id)));
}

await SwiftDotNetLive.Activities!.StartAsync(new DeliveryActivity(), state);
```

Design history, alternatives rejected, and the open questions live in
[`plans/live-activities-plan.md`](../plans/live-activities-plan.md) and
[`plans/widgets-plan.md`](../plans/widgets-plan.md).

## Why this is not just another backend

Every other backend assumes **our process hosts the renderer**, and C# drives it by pushing patches
through [`IBridge`](../src/SwiftDotNet/Core/IBridge.cs). None of these surfaces work that way.

| | iOS Live Activity | iOS widget | Android notification | Android app widget |
|---|---|---|---|---|
| Renders in | WidgetKit extension, archived by the system | WidgetKit extension | **SystemUI** inflates our `RemoteViews` | SystemUI inflates our `RemoteViews` |
| Our code runs where | nowhere — the extension has no .NET | nowhere | the app posts; SystemUI draws | **`AppWidgetProvider` is a `BroadcastReceiver` in our own process** |
| Who triggers a render | the system | the system | us, on `notify()` | us, on `updateAppWidget()` |
| Refresh model | push (ActivityKit / APNs) | **pull**, against a daily budget | push | push |
| State handoff | inside the ActivityKit payload | **App Group container** | none needed | none needed |

Two consequences shape everything below:

1. **The vocabulary is a separate, restricted DSL** (`LiveView`), not `View`. See [The vocabulary](#the-vocabulary).
2. **On Apple, the app pre-renders everything**; on Android, the provider renders in-process. See
   [The Apple inversion](#the-apple-inversion-the-app-pre-renders-everything).

## Packages

| Package | Role | Status |
|---|---|---|
| [`src/SwiftDotNet.Live`](../src/SwiftDotNet.Live) | The `LiveView` vocabulary, compact wire, validator, `ISurfaceChannel`, `LiveActivity<T>`, `LiveUpdate`. Plain `net10.0`, no platform deps. | ✅ Built, 41 tests |
| [`src/SwiftDotNet.Widgets`](../src/SwiftDotNet.Widgets) | `Widget<T>`, `WidgetTimeline<T>`, family fan-out. Plain `net10.0`. | ✅ Built, tested |
| [`src/SwiftDotNet.Live.Android`](../src/SwiftDotNet.Live.Android) | `RemoteViews` interpreter, notification + app-widget drivers, bitmap renderer. **No Kotlin, no new `.aar`.** | 🧩 Builds; never run on a device |
| [`src/SwiftDotNet.Live.Apple`](../src/SwiftDotNet.Live.Apple) | App Group channel, ActivityKit + WidgetCenter P/Invoke. | 🧩 Builds; never run on a device |
| [`native/SwiftDotNetWidgets`](../native/SwiftDotNetWidgets) | Swift: `SDNLiveView` (widget-subset SwiftUI interpreter), `SDNTimelineProvider`, `SDNActivityAttributes`, the `@_cdecl` bridge. | 🧩 xcframework builds (iOS device + sim) |

> **Status is honest and narrow.** The two pure-C# packages are exercised by 41 headless tests. The two
> platform drivers and the Swift shim **compile** — nothing here has been run on a device or an emulator.

## The vocabulary

`LiveView` is a parallel vocabulary over the same pipeline: same [`Node`](../src/SwiftDotNet/Core/Node.cs),
same [`SwiftColor`](../src/SwiftDotNet/Core/Values.cs)/`SwiftFont`/[`Brush`](../src/SwiftDotNet/Core/Brush.cs)
tokens, same action dispatch. Only the wire and the view types differ.

**Why not reuse `View` and drop what doesn't map?** Because the failure mode is a lock-screen UI the
developer never sees, silently missing half its content. A separate vocabulary makes the subset a
build-time fact. It is the same argument the [car backends plan](../plans/car-backends-plan.md) makes for
a different reason.

| View | Apple | Android (`Native`) | Notes |
|---|---|---|---|
| `LiveText` | `Text` | `TextView` | |
| `LiveTimer` | `Text(timerInterval:)` | `Chronometer` + `setChronometerCountDown` | **Ticks by itself** — see below |
| `LiveDate` | `Text(_, style:)` | formatted at publish | Host-formatted on Apple only |
| `LiveImage` | `Image(systemName:)` | drawable by name | Symbols/drawables only — an extension cannot see the app's assets |
| `LiveBitmap` | `Image(uiImage:)` from base64 | `setImageViewBitmap` | Requires `.AccessibilityLabel` |
| `LiveVStack` / `LiveHStack` / `LiveZStack` | `VStack` / `HStack` / `ZStack` | `LinearLayout` / `FrameLayout` | |
| `LiveSpacer` / `LiveDivider` | `Spacer` / `Divider` | weighted / 1dp `TextView` | A bare `View` and `Space` are **not** `RemoteViews`-inflatable |
| `LiveProgress` | `ProgressView` | `ProgressBar` | |
| `LiveGauge` | `Gauge` | ⚠️ degrades to `LiveProgress` | `SDNL011` |
| `LiveShape` | `Rectangle`/`RoundedRectangle`/`Capsule`/`Circle` | background colour on a `TextView` | |
| `LiveButton` | `Button(intent:)` — **activities only** | `PendingIntent` | `SDNL003` on an Apple widget |
| `LiveLink` | `Link` / `widgetURL` | `setOnClickPendingIntent` | The only Apple *widget* interaction |

**`LiveTimer` is the most valuable node here** and has no analog in the main DSL. It counts up or down
without any update from us — so a running countdown costs **zero** against the iOS activity-update budget
and **zero** `notify()` calls on Android. Anything showing an ETA or a countdown should use it rather than
republishing every second.

### Modifiers

`.Font` `.ForegroundColor` `.Background(SwiftColor | Brush)` `.Padding` `.Frame` `.CornerRadius`
`.Opacity` `.Tint` `.LineLimit` `.Bold` `.AccessibilityLabel` `.OnTapUrl`

The omissions are deliberate: there is no `.Rotation`, `.ScaleEffect`, `.Shadow`, `.Blur`, `.Animation` or
any gesture modifier, because a `RemoteViews` tree has no transform, filter or animation vocabulary and a
widget's SwiftUI cannot animate on demand. Adding them would produce a modifier that works on one platform
and is silently dropped on the other.

| Modifier | Gotcha |
|---|---|
| `.Frame` | Android needs **API 31** (`setViewLayoutWidth`). Below that it is a no-op — `SDNL007`. |
| `.Background(Brush)` | Android has no runtime gradient drawable; renders as the **first stop** — `SDNL010`. |
| `.Opacity` | Android applies it to images only (`setImageAlpha`). `View.setAlpha` is not `@RemotableViewMethod` and calling it would throw at inflate time. |
| `.CornerRadius` | Android: not applied — rounding needs a drawable a recipe cannot construct. Use `LiveRenderMode.Bitmap`. |
| `.AccessibilityLabel` | **Required** on a `LiveBitmap` — it is the only thing a screen-reader user gets. |

## The 4 KB ceiling, and the validator

A Live Activity's whole content state rides inside an APNs payload capped at **4 KB**. Blowing it does not
throw — the update is rejected and the activity keeps showing stale content on a tester's lock screen.

That is representative: **every constraint on these surfaces fails silently.** A `Button` in an Apple
widget compiles, ships, and does nothing. An oversized notification is dropped with a log line nobody
reads. So [`LiveValidator`](../src/SwiftDotNet.Live/LiveValidator.cs) is not a nicety — it is the
substitute for the compiler errors these platforms decline to give us, and every driver calls it before
publishing.

| Code | Severity | Meaning |
|---|---|---|
| `SDNL001` | Error | Activity payload over 4 KB (checked across **all slots at once**) |
| `SDNL002` | Warning | Past the 2 KB guideline, or a large notification/widget payload |
| `SDNL003` | Error | `LiveButton` on an Apple **widget** — use `LiveLink` |
| `SDNL004` | Error | `LiveBitmap` with no accessibility label |
| `SDNL005` | Error | Bitmap over the pixel ceiling |
| `SDNL006` | Error | Non-text root in an `AccessoryInline` widget |
| `SDNL007` | Warning | `.Frame` with Android `minSdk` < 31 |
| `SDNL008` | Warning | Tree nested more than 10 deep (RemoteViews parcels badly) |
| `SDNL009` | Warning | More than 3 notification actions |
| `SDNL010` | Info | Gradient flattens to its first stop on Android |
| `SDNL011` | Info | `LiveGauge` degrades to a progress bar on Android |
| `SDNL020` / `SDNL021` | Warning / Info | Empty timeline; single entry with no refresh point |
| `SDNL030` | Warning | `LiveTimer` frozen by `LiveRenderMode.Bitmap` |

The compact wire ([`LiveWire`](../src/SwiftDotNet.Live/LiveWire.cs)) exists for the same budget: it uses
single-letter keys, drops the `L` type prefix, omits empty collections, rounds doubles to 3 decimals, and
writes ids only on addressable nodes. It is **>30% smaller** than the core wire, which a test pins.

> **Deltas were rejected.** [`TreeDiffer`](../src/SwiftDotNet/Core/TreeDiffer.cs) cannot help: the renderer
> is stateless between system-driven renders — a widget extension launches, draws, and dies — so there is
> never a prior tree on the far side to patch against. Whole trees only.

## Interactivity

Neither surface can call us back; both hand back an **id**. `LiveButton` handlers are routed through
[`LiveActionRouter`](../src/SwiftDotNet.Live/LiveActionRouter.cs) into the same `id → delegate` lookup
`SwiftApp` already uses for ordinary views.

| | Route | Runs in |
|---|---|---|
| iOS Live Activity button | `SDNLiveActionIntent : LiveActivityIntent` (17.2+) | **the app's process** |
| iOS widget button | ❌ not supported — a widget's `AppIntent` runs in the extension, which has no .NET | — |
| iOS tap (either) | `widgetURL` / `Link` | the app, via URL |
| Android, anything | `PendingIntent` → `LiveActionReceiver` | the app's process |

**`LiveActivityIntent`, not `AppIntent`**, is the load-bearing detail of the whole interactive story on
Apple. A plain `AppIntent`'s `perform()` runs inside the widget extension where there is no managed code.

A surface **outlives the process that published it**, so a tap can arrive against a tree published by a
previous launch whose handlers no longer exist. That is normal, not an error: the tap is written to a
durable mailbox and `DrainPendingAsync()` picks it up on next foreground.

## The channel

[`ISurfaceChannel`](../src/SwiftDotNet.Live/SurfaceChannel.cs) is **a mailbox, not a socket**, and the API
is shaped to say so. On Apple the app and its widget extension are almost never alive at the same time —
the user taps a widget, the extension launches, renders and dies, while the app may have been suspended
for hours. There is no live IPC. What exists is durable shared storage plus a "please look at it" nudge in
each direction.

```csharp
await channel.PublishAsync(snapshot);            // store, then nudge
var placed  = await channel.GetPlacementsAsync(); // what the user actually installed
var pending = await channel.DrainActionsAsync();  // taps queued while we were suspended
```

| | Apple | Android |
|---|---|---|
| Store | App Group container (**a directory** — `FileSurfaceChannel` *is* the store) | app files dir |
| Nudge out | `WidgetCenter.reloadTimelines(ofKind:)` | `AppWidgetManager` broadcast |
| Nudge in | intent appends to the mailbox + a C callback when running | `PendingIntent` → receiver, live |
| Placements | `WidgetCenter.getCurrentConfigurations` | `AppWidgetManager.getAppWidgetIds` |

On Android this degenerates almost to a direct call, which is correct. The API is designed against the
**Apple** constraint, because an API shaped by Android's freedom could not be honoured on Apple.

> **The silent failure to know about:** a wrong or unentitled App Group id does not throw. The container
> comes back null, or the extension gets a different directory, and the widget renders a placeholder
> forever. `AppleSurfaceChannel` throws on a missing container rather than falling back to a temp
> directory that would appear to work in the app and never work in the extension.

## The Apple inversion — the app pre-renders everything

On Apple, `Widget<T>.TimelineAsync` **cannot run in the widget extension**; there is no .NET there. So:

1. the **app** computes the timeline and renders every entry × every placed family,
2. publishes them into the App Group keyed `{family}@{unix-seconds}`,
3. and the Swift `SDNTimelineProvider` is a **dumb reader** that hands the pre-built trees to WidgetKit.

Two consequences, stated plainly because they will otherwise be discovered as bugs:

- **A widget can only show data the app has already computed.** If the app has not run for a day, the
  widget shows the last published timeline until it runs dry.
- **A widget does not refresh itself.** Keeping it fresh needs a background trigger in the *app*
  (`BGAppRefreshTask` / `WorkManager`), which this library deliberately does not own. Publish several hours
  of entries as a safety margin; `SDNL021` warns about a single-entry timeline with no refresh point.

Android runs the same `TimelineAsync` in-process, on demand, from `SwiftDotNetAppWidgetProvider`. Identical
developer model, no budget lies.

The entry-selection rule — *the latest tree for this family at or before now, falling back to the earliest*
— is implemented three times (managed, Swift, Android) and pinned by a
[theory test](../tests/SwiftDotNet.Tests/LiveSurfaceTests.cs) so the three cannot drift.

## Android: two render modes

| | `LiveRenderMode.Native` (default) | `LiveRenderMode.Bitmap` |
|---|---|---|
| Fidelity | the `@RemotableViewMethod` whitelist only | **anything the Skia engine can draw** |
| Min API | 31 | 24 |
| Accessibility | real views; TalkBack reads them | one `ImageView` + a content description |
| Hit testing | per-view `PendingIntent` | whole-surface tap + action buttons |
| Theme / font scale | system handles it | must re-render and re-post |
| `LiveTimer` | ticks | **frozen** — `SDNL030` |

The bitmap route is nearly free because the live vocabulary is a strict *subset* of the main DSL:
[`LiveLowering`](../src/SwiftDotNet.Live/LiveLowering.cs) rewrites the type names onto core nodes, the
headless engine in [`SwiftDotNet.Graphics`](../src/SwiftDotNet.Graphics) lays out and paints them, and
Android supplies only [`AndroidCanvas`](../src/SwiftDotNet.Live.Android/AndroidCanvas.cs). No second
rasterizer, no second layout pass. It is the first time the [renderer seam](backends/skia.md) has been
implemented against a platform toolkit rather than another Skia binding.

> **There is no bitmap route on Apple.** The extension could show an image from the App Group, but only the
> app can render it — so a *push-updated* activity, where the system wakes the extension without waking us,
> would show a stale image.

## Android 16 Live Updates

`LiveUpdate` maps to `Notification.ProgressStyle` + promoted-ongoing (API 36): a status-bar chip and
lock-screen prominence.

```csharp
var update = new LiveUpdate { Title = "Arriving 4:32 PM", Progress = 0.4, TrackerIcon = "truck" }
    .Segment(0.4, Color.Green)
    .Segment(0.6, Color.Secondary)
    .Point(0.4, Color.Blue);
```

**It is a data model, not a view tree, and that is the point.** A Live Update is *templated* — you supply
segments, points, a tracker icon and text, and the system draws it. Modelling it as a `LiveView` would
reintroduce exactly the silent-drop failure the vocabulary exists to prevent.

> **Honest limitation:** `Notification.Builder.requestPromotedOngoing` is **not bound** in the current
> Android SDK (36.1.69), so `AndroidLiveActivities.PostLiveUpdate` reaches it through JNI on API 36+ and
> skips it otherwise. Without it the notification is still correct — ongoing, with real progress — it
> simply does not get the status-bar chip.

## Setup

### Android

```csharp
// Application.OnCreate — static because a BroadcastReceiver constructed by the system
// must be able to find the router in a process it may have started cold.
SwiftDotNetLive.Init(this, Resource.Drawable.ic_notification);
```

Then subclass `LiveActionReceiver` (manifest-registered for `LiveActionReceiver.ActionTap`) and, for
widgets, `SwiftDotNetAppWidgetProvider`. No `.aar` and no Kotlin: `RemoteViews` is a serialized recipe, and
`AppWidgetProvider` runs in our process, so C# builds the tree directly. **Glance was rejected** for the
same reason the Compose bridge is not reused — it would put the content vocabulary back in Kotlin.

### Apple

```bash
./native/SwiftDotNetWidgets/build-xcframework.sh
```

```xml
<Import Project="..\..\src\SwiftDotNet.Live.Apple\SwiftDotNetLive.targets" />
```

```csharp
SwiftDotNetLive.Init("group.com.example.app");   // FinishedLaunching
```

The consumer also needs an **Xcode widget-extension target** embedding the same xcframework, and the App
Group entitled on **both** targets. There is no "just add a NuGet" story for a Live Activity — a WidgetKit
extension's `@main WidgetBundle` must be Swift. The extension's body is three lines:

```swift
struct DeliveryWidget: Widget {
    var body: some WidgetConfiguration {
        StaticConfiguration(kind: "forecast",
                            provider: SDNTimelineProvider(kind: "forecast",
                                                          appGroup: "group.com.example.app")) { entry in
            SDNWidgetView(entry: entry)
        }
    }
}
```

## Status & what is unproven

| Piece | Status |
|---|---|
| Vocabulary, compact wire + reader, validator, lowering | ✅ Verified headlessly (41 tests, macOS) |
| `LiveActivity<T>` slots, budget check, action collection | ✅ Verified headlessly |
| `Widget<T>` timeline, family fan-out, entry selection | ✅ Verified headlessly |
| `ISurfaceChannel` publish/read/drain, mailbox escaping | ✅ Verified headlessly |
| `RemoteViews` interpreter, notification + widget drivers | 🧩 Compiles; **never run on an emulator** |
| `AndroidCanvas` + bitmap route | 🧩 Compiles; **no pixels ever produced** |
| Swift `SDNLiveView`, provider, ActivityKit host | 🧩 xcframework builds; **never run in a simulator** |
| Android 16 Live Update promotion | 🧩 JNI path, unexercised |
| Widget configuration (`WidgetConfigurationIntent`) | ❌ Not implemented — static widgets only |
| Push-updated activities | Token surfaced via `SwiftDotNetLive.PushTokenReceived`; sending is the app's push stack's job |
| Collection widgets (`setRemoteAdapter`) | ❌ Not supported by design |

Both plans list five facts flagged **verify in Phase 0** that remain unverified because they need a device:
the widget memory budget, whether a custom `Layout` survives archiving, whether `LiveActivityIntent` really
runs in-process, real `RemoteViews` parcel limits, and the WidgetKit refresh budget.

## Cross-links

- [`plans/live-activities-plan.md`](../plans/live-activities-plan.md) · [`plans/widgets-plan.md`](../plans/widgets-plan.md)
- [Apple backend](backends/apple.md) — why SwiftUI needs a shim at all
- [Android backend](backends/android.md) — the Compose shim this deliberately does not reuse
- [Skia backend](backends/skia.md) — the engine the bitmap route borrows
- [Views & Controls](views-and-controls.md) — the full DSL this vocabulary is a subset of
