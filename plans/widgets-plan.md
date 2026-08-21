# Plan: Home-screen widgets & the surface channel (`SwiftDotNet.Widgets`)

**Status:** **Implemented** (both pure-C# libraries tested; platform drivers compile, never run) · **Date:** 2026-08-20
**Save to (repo convention):** `plans/widgets-plan.md`

> **Companion plan.** This plan **depends on** [Live Activities & rich notifications](live-activities-plan.md)
> for the two things it does not re-solve: the restricted `LiveView` vocabulary (Crux 2 there) and the two
> interpreters that render it (`SwiftDotNetWidgets` in Swift, `RemoteViewsInterpreter` in Kotlin). Read that
> plan's Crux 1–3 first; they apply here unchanged.
>
> **What this plan owns**, and the live-activities plan does not:
> 1. The **surface channel** — how a running app talks *to* a widget, and how a widget talks *back*. A Live
>    Activity carries its state inside the ActivityKit payload; a widget has no carrier and must be given one.
>    The channel is shared infrastructure and lands in `SwiftDotNet.Live`, used by both plans.
> 2. **Pull vs push refresh** — iOS widgets are *timeline*-based and budgeted; Android widgets are
>    push-based and un-budgeted. Reconciling those is the hard part.
> 3. **Families, sizing and configuration** — a widget exists in several sizes at once and may be
>    user-configured; a Live Activity is one shape with fixed slots.
>
> Out of scope: Wear OS tiles and watchOS complications (a third layout system, `ProtoLayout`); iOS 18
> control widgets (a different, tiny vocabulary); Android collection widgets backed by
> `RemoteViewsService` (see §Open question 4).

> **Built.** See the *Built* section in
> [`live-activities-plan.md`](live-activities-plan.md#built--2026-08-20) — it covers both plans, including
> the four places this one's design changed on contact with the platforms.

## Context — the asymmetry that shapes everything

The live-activities plan's premise was "we do not own the renderer". For widgets that stays true on Apple
and is **half false on Android**, and the difference is structural rather than cosmetic.

| | iOS widget | Android app widget |
|---|---|---|
| Our code runs where | a **separate extension process**, launched by the system | **`AppWidgetProvider` is a `BroadcastReceiver` in our own app process** |
| Can .NET run in it | **No** — pure Swift `TimelineProvider` + `WidgetBundle` | **Yes** — `onUpdate` is our process, our runtime, our C# |
| Content build | Swift builds SwiftUI, system archives it | **C# builds the `RemoteViews`**; SystemUI only *inflates* them |
| Refresh model | **pull** — system asks the provider for a timeline; hard daily budget | **push** — we call `updateAppWidget` whenever we like; plus an optional `updatePeriodMillis` (min **30 min**) |
| State handoff | must cross a process boundary → **App Group** | none needed — same process, same objects |
| Back-channel | `Link`/`widgetURL`, or `AppIntent` running **in the extension** | `PendingIntent` → our receiver, in-process |

So on Android the "channel" is nearly a no-op and the widget is just another render target for the
`RemoteViews` interpreter. On Apple, the channel *is* the feature. A shared API has to be shaped by the
Apple constraint and then implemented trivially on Android — not the other way round, or we'd design
something Apple cannot honour.

**Rejected: Glance.** `GlanceAppWidget` compiles Composables to `RemoteViews` and is the idiomatic Kotlin
answer. It is rejected for the same reason `Bridge.kt` is not reused for notifications: it would put the
widget's content vocabulary in **Kotlin**, when the whole point is that C# declares it. Since our provider
runs in our own process, we can build `RemoteViews` directly and skip Glance's runtime entirely.

## Crux 1 — the channel is a mailbox, not a socket

The natural API is "the app pushes new widget state". On Apple that phrase hides a hard fact: **the app and
the widget extension are almost never alive at the same time.** The user taps a widget, the extension is
launched, renders, and dies. The app may have been suspended for hours. There is no live IPC.

What actually exists between them:

| Mechanism | Direction | Constraint |
|---|---|---|
| **App Group container** (`UserDefaults(suiteName:)`, or files) | both | the only durable shared state; requires an App Group entitlement in *both* targets |
| `WidgetCenter.shared.reloadTimelines(ofKind:)` | app → widget | a *request* to refresh; the system decides when, and it draws on the refresh budget |
| `WidgetCenter.shared.getCurrentConfigurations` | app ← system | which widget kinds/families the user actually installed |
| `Link` / `widgetURL` | widget → app | opens the app; the URL is the payload |
| `AppIntent.perform()` | widget → **extension** | runs in the *extension*, **not** the app (unlike `LiveActivityIntent`) — it can only write to the App Group |
| `ForegroundContinuableIntent` | widget → app | brings the app forward to finish the work |
| Darwin notifications | both | wakes a process **only if it is already running** — useful for the app-in-foreground case, useless otherwise |

That is a **mailbox**: durable shared storage plus a "please look at it" nudge in each direction. Any API
that implies a synchronous call across the boundary is a lie we would have to break later.

**Adopted:** an explicitly asynchronous, store-and-nudge channel.

```csharp
public interface ISurfaceChannel
{
    // app → surface. Durable: written to shared storage, survives both processes dying.
    Task PublishAsync<T>(string kind, T state, CancellationToken ct = default);

    // surface → app. Delivered on next app launch/foreground if the app was not alive.
    IAsyncEnumerable<SurfaceAction> Actions { get; }

    // what the user actually installed, so we do not publish into the void
    Task<IReadOnlyList<SurfacePlacement>> GetPlacementsAsync(CancellationToken ct = default);
}
```

| | Apple | Android |
|---|---|---|
| `PublishAsync` | write to the App Group suite, then `reloadTimelines(ofKind:)` | write to `SharedPreferences`, then `AppWidgetManager.updateAppWidget` |
| `Actions` | drain a mailbox file the extension's `AppIntent` appended to, on app foreground | delivered live from our `BroadcastReceiver`; the mailbox is a same-process queue |
| `GetPlacementsAsync` | `getCurrentConfigurations()` | `AppWidgetManager.getAppWidgetIds()` |

The Android implementation degenerates to "call the handler directly", which is correct and cheap. The
shape stays honest on both.

**The same channel serves Live Activities**, which is why it lives in `SwiftDotNet.Live` rather than here:
a Live Activity's `LiveActivityIntent` runs in-process, but the *stale-activity-after-app-restart* problem
and the "which activities are live" query are the same mailbox questions.

## Crux 2 — pull vs push, and the refresh budget

An iOS widget cannot be told "show this now". The app publishes state and *requests* a reload; the system
asks the `TimelineProvider` for a set of **future-dated entries** and renders them on its own schedule.
WidgetKit enforces a daily refresh budget (commonly quoted at roughly 40–70 reloads for a frequently viewed
widget; **verify in Phase 0** — it is undocumented and version-dependent). Android has no such budget: we
push whenever we want, and `updatePeriodMillis` has a 30-minute floor that we mostly ignore in favour of
explicit updates and WorkManager.

Modelling this as "push" would produce apps that silently stop updating on iOS. Modelling it as "pull"
would waste Android's freedom. The reconciling abstraction is a **timeline both platforms can honour**:

```csharp
public sealed class WeatherWidget : Widget<WeatherState>
{
    public override string Kind => "weather";

    // Called to produce state. On Apple this is invoked by the APP (never the extension — no .NET there)
    // and the result is published to the App Group. On Android it is invoked in-process by the provider.
    public override async Task<WidgetTimeline<WeatherState>> TimelineAsync(WidgetContext ctx)
    {
        var forecast = await _svc.GetAsync(ctx.CancellationToken);
        return WidgetTimeline
            .Entry(DateTimeOffset.Now,             forecast.Now)
            .Entry(DateTimeOffset.Now.AddHours(1), forecast.NextHour)
            .Entry(DateTimeOffset.Now.AddHours(2), forecast.TwoHours)
            .RefreshAfter(DateTimeOffset.Now.AddHours(3));
    }

    public override LiveView Body(WeatherState s, WidgetFamily family) => family switch
    {
        WidgetFamily.Small => new LiveVStack(
            new LiveImage(s.Symbol),
            new LiveText($"{s.Degrees}°").Font(LiveFont.Title)),

        WidgetFamily.AccessoryInline => new LiveText($"{s.Symbol} {s.Degrees}°"),

        _ => new LiveHStack(
            new LiveImage(s.Symbol),
            new LiveVStack(new LiveText(s.Place), new LiveText($"{s.Degrees}°"))),
    };
}
```

**The critical inversion, and the reason this is not obvious:** on Apple, `TimelineAsync` **cannot run in
the widget extension** — there is no .NET there. So the app computes the timeline, serializes *all* its
entries (each entry's rendered `LiveView` tree, pre-built) into the App Group, and the Swift
`TimelineProvider` becomes a dumb reader that hands the archived trees back to WidgetKit. The provider
never calls C#.

Consequences worth stating plainly:

- **A widget can only show data the app has already computed and published.** If the app has not run for a
  day, the widget shows the last published timeline until it runs dry.
- Keeping it fresh therefore needs a **background trigger in the app** — BGAppRefreshTask on iOS, WorkManager
  on Android — which is deliberately *not* this plan's job. It is the app's job, and the docs must say so
  rather than implying widgets self-refresh.
- The timeline's tail is the safety margin: publish several hours of entries so a suspended app still shows
  something plausible. `RefreshAfter` maps to WidgetKit's `TimelineReloadPolicy.after(_:)` and to a
  WorkManager one-shot on Android.

Android takes the same timeline and: renders entry 0 immediately, schedules WorkManager for the rest.
Identical developer model, no budget lies.

## Crux 3 — one widget, many shapes and one configuration

A widget is not one tree. Apple families: `systemSmall`, `systemMedium`, `systemLarge`, `systemExtraLarge`
(iPad), plus the iOS 16+ lock-screen accessories `accessoryCircular`, `accessoryRectangular`,
`accessoryInline`. Android is a continuous `minWidth`/`minHeight` grid, with API 31's
`RemoteViews(Map<SizeF, RemoteViews>)` for responsive variants and `OPTION_APPWIDGET_MIN_WIDTH` options for
the rest.

The DSL takes `WidgetFamily` as a `Body` parameter (above) rather than exposing named slots the way a Live
Activity does — families are a continuum on Android and an enum on Apple, and a `switch` degrades honestly
on both. Android maps its size buckets onto the nearest Apple family and additionally exposes the raw
`WidgetSize` for apps that want it.

Supporting details, none of them deep but all of them required for a widget to be shippable:

| Concern | Apple | Android |
|---|---|---|
| Background | `containerBackground(for: .widget)` is **mandatory** from iOS 17 | ordinary background + `system_app_widget_background_radius` |
| Preview in the picker | `#Preview(as:)` / the widget's placeholder | `previewLayout` (API 31) or `setWidgetPreview` (API 35) |
| Configuration | `AppIntentConfiguration` + a `WidgetConfigurationIntent` | a configuration `Activity` declared via `android:configure` |
| Placeholder / redacted | `.redacted(reason: .placeholder)` | initial layout resource |
| Tap target | `widgetURL` (whole) or per-`Link` | `setOnClickPendingIntent` per view |

**Configuration is the one that leaks into C# awkwardly**, because Apple's configuration UI is generated
from a Swift `AppIntent`'s `@Parameter`s. A C#-declared parameter list would have to be code-generated into
Swift at build time. Proposal: **Phase 3 supports static (non-configurable) widgets only**, and
configuration lands later behind a source generator that emits the Swift intent from a C# attribute. Say so
in the docs rather than half-supporting it.

## What is shared, precisely

Beyond everything the [live-activities plan](live-activities-plan.md#what-is-shared-precisely) already lists:

| Piece | Shared? | Note |
|---|---|---|
| `LiveView` vocabulary + validator | ✅ verbatim | widgets use the **same** restricted subset; the validator gains family-specific caps (an `accessoryInline` is one line of text, full stop) |
| `SwiftDotNetWidgets` (Swift interpreter) | ✅ verbatim | it was always a widget interpreter; Live Activities were its first caller |
| `RemoteViewsInterpreter` (Kotlin) | ⚠️ mostly | app widgets add `setRemoteAdapter` and the size-bucket map |
| Compact wire (Crux 4 there) | ✅ + relaxed | **no 4 KB ceiling here** — the App Group file has no payload limit, so a widget timeline may be far larger than an activity payload |
| `SwiftApp` action dispatch | ✅ verbatim | `PendingIntent` / mailbox drain feed the same `_actions` lookup |
| `ISurfaceChannel` | ✅ **defined here, used by both** | |
| `TreeDiffer` | ❌ n/a | whole trees only, same as activities |
| Route B bitmap | ✅ Android only | an app widget is a fine bitmap target; same accessibility trade |

## Packages

Extends the live-activities table rather than duplicating it:

| Package | Added by this plan |
|---|---|
| `src/SwiftDotNet.Live` | `ISurfaceChannel`, `SurfaceAction`, `SurfacePlacement`, `WidgetTimeline<T>` |
| `src/SwiftDotNet.Widgets` | `Widget<T>`, `WidgetFamily`, `WidgetSize`, `WidgetContext`, the publish pipeline |
| `src/SwiftDotNet.Widgets.Apple` | App Group store, `WidgetCenter` P/Invoke, `SwiftDotNetWidgets.targets` |
| `src/SwiftDotNet.Widgets.Android` | `SwiftDotNetAppWidgetProvider`, size-bucket mapping, WorkManager scheduling |
| `native/SwiftDotNetWidgets` | `+ SDNTimelineProvider` — the dumb App-Group-reading provider |

**Same Apple build-plumbing cost as the live-activities plan, and it does not get cheaper by being shared:**
the consumer needs a widget-extension target *and* an App Group entitlement on both targets. The App Group
identifier must be discovered by both C# and Swift at runtime — plan on a build property
(`$(SwiftDotNetAppGroup)`) written into both the entitlements file and a generated constant, because
getting this wrong fails **silently** (the extension reads an empty suite and renders a placeholder forever).
A doctor check for it is not optional; `SwiftDotNetDoctor` already exists in the Rider plugin
([`tooling/rider/.../doctor`](../tooling/rider/src/main/kotlin/com/swiftdotnet/rider/doctor)) and should
grow this rule.

## Phases

Sequenced to interleave with the live-activities plan rather than run after it — Phase W1 depends only on
that plan's Phase 1.

| Phase | Deliverable | Depends on |
|---|---|---|
| **W0 — spike** | Confirm the real WidgetKit refresh budget; confirm an `AppIntent` in a widget really cannot reach the app process; measure App Group read latency at extension launch; check `RemoteViews(Map<SizeF,…>)` behaviour on API 31 vs 36. | — |
| **W1 — Android widgets** | `Widget<T>`, `SwiftDotNetAppWidgetProvider`, in-process render via the `RemoteViews` interpreter, `PendingIntent` routing. Emulator-verified. | Live plan Phase 1 |
| **W2 — the channel** | `ISurfaceChannel` + both stores; the mailbox and its drain-on-foreground; `GetPlacementsAsync`. Retro-fitted to Live Activities in the same change. | W1 |
| **W3 — Apple widgets** | `SDNTimelineProvider`, App Group plumbing, `$(SwiftDotNetAppGroup)`, doctor rule, sample extension target. Device-verified (lock-screen accessories need a real device). | Live plan Phase 2, W2 |
| **W4 — timelines** | `WidgetTimeline<T>`, `RefreshAfter`, WorkManager scheduling, budget diagnostics in DevTools. | W3 |
| **W5 — configuration** | The C#-attribute → Swift `WidgetConfigurationIntent` generator; Android configuration activity. | W4 |

W1 alone is a genuinely useful shipped feature — an Android app widget declared in C# — and it needs no
App Group, no extension target, and no Apple unknowns.

## Open questions

1. **Does `Widget<T>` live in Core?** Same unresolved seam as the live plan's Open question 1 and
   [`view-construction-seam.md`](view-construction-seam.md): `View.BuildNode` is `internal`, so a satellite
   package cannot build nodes. Answer once, for `LiveView` and `Widget<T>` together.
2. **Who owns background refresh?** This plan says "not us" (Crux 2). But a widget that goes stale because
   the app never runs is the #1 support question we will get. Minimum viable answer: document it loudly and
   ship a DevTools warning when a published timeline has run out of entries. Maximal answer: a
   `SwiftDotNet.Background` companion wrapping BGTaskScheduler/WorkManager — a separate plan, and arguably
   the app's existing job library's problem, not ours.
3. **Serialized timeline size.** Pre-rendering *every* entry × *every* installed family multiplies the tree
   count fast (3 entries × 4 families = 12 trees). Cheap in absolute terms, but it is written on every
   publish. Measure in W0; if it bites, render lazily per family using the placement list.
4. **Collection widgets.** A scrolling list in an Android widget needs `setRemoteAdapter` +
   `RemoteViewsService`, which is a whole second lifecycle; API 31's `RemoteViews.RemoteCollectionItems`
   avoids the service but caps item count. Apple has no scrolling widgets at all. Recommend: **not
   supported**, and the validator rejects `List` inside a widget with a clear message. Revisit only if asked.
5. **Do widgets and Live Activities share a `Kind` namespace?** Leaning yes — one string id space, one
   channel, one mailbox — so an app can migrate a surface from one to the other without re-keying.

## Cross-links

- [`plans/live-activities-plan.md`](live-activities-plan.md) — the vocabulary, both interpreters, and Crux 1–3
- [`plans/view-construction-seam.md`](view-construction-seam.md) — Open question 1
- [`docs/backends/apple.md`](../docs/backends/apple.md) · [`docs/backends/android.md`](../docs/backends/android.md)
- [`docs/maps.md`](../docs/maps.md) — the companion-package + `.targets` pattern both plans follow
