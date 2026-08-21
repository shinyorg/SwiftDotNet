using System.Globalization;

namespace SwiftDotNet;

/// <summary>
/// A home-screen / lock-screen widget declared in C#.
///
/// <para><b>The inversion worth understanding before using this.</b> On Apple,
/// <see cref="TimelineAsync"/> <i>cannot run in the widget extension</i> — the extension is a separate
/// binary with no .NET in it. So the <b>app</b> computes the timeline, renders every entry for every
/// placed family, and publishes the lot into a shared App Group container; the Swift
/// <c>TimelineProvider</c> is then a dumb reader that hands the pre-built trees to WidgetKit and never
/// calls back into managed code.</para>
///
/// <para>Two consequences follow, and neither is hidden: a widget can only ever show data the app has
/// <i>already</i> computed, and keeping it fresh needs a background trigger in the <i>app</i>
/// (<c>BGAppRefreshTask</c> / <c>WorkManager</c>) that this library deliberately does not own. A widget
/// does not refresh itself. Publish a few hours of entries so a suspended app still shows something
/// plausible.</para>
///
/// <para>Android is the easy half: an <c>AppWidgetProvider</c> is a <c>BroadcastReceiver</c> in our own
/// process, so the same <see cref="TimelineAsync"/> runs in-process on demand and the <c>RemoteViews</c>
/// are built directly. The API is shaped by the Apple constraint because an API shaped by Android's
/// freedom could not be honoured on Apple.</para>
/// </summary>
/// <typeparam name="TState">The value each timeline entry carries into <see cref="Body"/>.</typeparam>
public abstract class Widget<TState>
{
    /// <summary>Stable id, shared with <see cref="ISurfaceChannel"/> and <see cref="LiveActivity{TState}"/>.</summary>
    public abstract string Kind { get; }

    /// <summary>
    /// The tree for one state in one shape. A <c>switch</c> on <paramref name="family"/> is the expected
    /// form: an <see cref="WidgetFamily.AccessoryInline"/> is one line of text beside the lock-screen
    /// clock, not a shrunken <see cref="WidgetFamily.Small"/>, and pretending otherwise produces a widget
    /// that is illegible in half its placements.
    /// </summary>
    public abstract LiveView Body(TState state, WidgetFamily family);

    /// <summary>
    /// The shapes this widget offers. Rendering is skipped for anything not listed, and the validator
    /// rejects a family the platform will never ask for (the accessories are Apple-only).
    /// </summary>
    public virtual IReadOnlyList<WidgetFamily> Families { get; } = new[]
    {
        WidgetFamily.Small, WidgetFamily.Medium, WidgetFamily.Large,
    };

    /// <summary>The widget's future. See <see cref="WidgetTimeline{TState}"/> for why this runs in the app.</summary>
    public abstract Task<WidgetTimeline<TState>> TimelineAsync(WidgetContext context);

    /// <summary>How the Android side rasterizes this widget's trees. Ignored on Apple.</summary>
    public virtual LiveRenderMode RenderMode => LiveRenderMode.Native;

    /// <summary>
    /// Renders the whole timeline into a publishable snapshot: one tree per (entry × family), keyed
    /// <c>{family}@{unix-seconds}</c>.
    ///
    /// <paramref name="context"/>'s placements narrow the fan-out — with nothing placed there is nothing
    /// to render, and rendering only the placed families is the difference between 3 trees and 21.
    /// </summary>
    public async Task<WidgetPayload> BuildPayloadAsync(WidgetContext context, LiveTarget target)
    {
        var timeline = await TimelineAsync(context).ConfigureAwait(false);

        var trees = new Dictionary<string, string>();
        var actions = new Dictionary<string, Action<string?>>();
        var diagnostics = new List<LiveDiagnostic>();

        // Render what is actually on screen. Falling back to every declared family when nothing is placed
        // keeps a first publish (before the user has added the widget) from producing an empty snapshot
        // that the host would render as a blank tile.
        var families = context.PlacedFamilies.Count > 0
            ? Intersect(context.PlacedFamilies, Families)
            : Families;

        foreach (var entry in timeline.Entries)
        {
            var stamp = LiveClock.At(entry.At).ToString("0.###", CultureInfo.InvariantCulture);

            foreach (var family in families)
            {
                var familyTarget = target with { Surface = LiveSurface.Widget, Family = family };
                var key = family + "@" + stamp;

                var payload = LiveWire.Build(Body(entry.State, family), LiveSurface.Widget, key);
                trees[key] = payload.Json;

                foreach (var d in LiveValidator.Validate(payload, familyTarget))
                    diagnostics.Add(d with { Message = $"[{key}] {d.Message}" });

                foreach (var kv in payload.Actions) actions[kv.Key] = kv.Value;
            }
        }

        var snapshot = new SurfaceSnapshot
        {
            Kind = Kind,
            Surface = LiveSurface.Widget,
            Trees = trees,
            PublishedAt = LiveClock.Now,
            RefreshAfter = timeline.RefreshAt is { } r ? LiveClock.At(r) : null,
        };

        // A timeline that has run dry is the #1 way a widget goes stale, and the platform reports it as
        // nothing at all — the last entry simply stays on screen forever. Say so at publish time.
        if (timeline.Entries.Count == 0)
            diagnostics.Add(new("SDNL020", LiveSeverity.Warning,
                $"'{Kind}' published an empty timeline; the widget will keep showing whatever it last had."));
        else if (timeline.RefreshAt is null && timeline.Entries.Count == 1)
            diagnostics.Add(new("SDNL021", LiveSeverity.Info,
                $"'{Kind}' published a single entry with no RefreshAfter, so it will never update until the " +
                "app publishes again. That is fine for static content and a bug for anything time-based."));

        return new WidgetPayload(snapshot, actions, diagnostics);
    }

    static IReadOnlyList<WidgetFamily> Intersect(IReadOnlyList<WidgetFamily> placed, IReadOnlyList<WidgetFamily> declared)
    {
        var result = new List<WidgetFamily>();
        foreach (var f in placed)
        {
            if (declared.Contains(f)) result.Add(f);
        }
        return result.Count > 0 ? result : declared;
    }
}

/// <summary>Everything the driver needs to publish one widget.</summary>
/// <param name="Snapshot">Every rendered tree, keyed <c>{family}@{unix-seconds}</c>.</param>
/// <param name="Actions">Handlers keyed by node id.</param>
/// <param name="Diagnostics">Per-tree validation plus timeline findings.</param>
public readonly record struct WidgetPayload(
    SurfaceSnapshot Snapshot,
    IReadOnlyDictionary<string, Action<string?>> Actions,
    IReadOnlyList<LiveDiagnostic> Diagnostics)
{
    /// <summary>Throws on the first error-severity finding. Drivers call this before publishing.</summary>
    public void Assert()
    {
        foreach (var d in Diagnostics)
        {
            if (d.Severity == LiveSeverity.Error)
                throw new InvalidOperationException(d.ToString());
        }
    }

    /// <summary>
    /// Picks the tree a host should show for a family at a moment — the same selection the Swift
    /// provider and the Android provider each perform, kept here so both agree and it can be tested.
    /// </summary>
    public string? TreeFor(WidgetFamily family, double atUnixSeconds)
        => SelectTree(Snapshot, family, atUnixSeconds);

    /// <summary>
    /// The entry selection rule: the latest tree for this family whose timestamp is at or before
    /// <paramref name="atUnixSeconds"/>, falling back to the earliest if the clock is behind them all.
    /// </summary>
    public static string? SelectTree(SurfaceSnapshot snapshot, WidgetFamily family, double atUnixSeconds)
    {
        var prefix = family + "@";
        string? best = null;
        var bestAt = double.NegativeInfinity;
        string? earliest = null;
        var earliestAt = double.PositiveInfinity;

        foreach (var kv in snapshot.Trees)
        {
            if (!kv.Key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!double.TryParse(kv.Key.AsSpan(prefix.Length), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var at)) continue;

            if (at <= atUnixSeconds && at > bestAt) { bestAt = at; best = kv.Value; }
            if (at < earliestAt) { earliestAt = at; earliest = kv.Value; }
        }

        return best ?? earliest;
    }
}
