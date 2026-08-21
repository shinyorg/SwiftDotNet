using System.Globalization;

namespace SwiftDotNet;

/// <summary>The named slots a Live Activity must fill. Android maps the ones it has an analog for and ignores the rest.</summary>
public static class LiveSlot
{
    /// <summary>The lock-screen / banner presentation. The only slot every platform renders.</summary>
    public const string LockScreen = "lockScreen";
    /// <summary>Dynamic Island, collapsed, left of the sensor housing. Android: the collapsed notification's leading content.</summary>
    public const string CompactLeading = "compactLeading";
    /// <summary>Dynamic Island, collapsed, right of the sensor housing. Android: the collapsed notification's trailing content.</summary>
    public const string CompactTrailing = "compactTrailing";
    /// <summary>Dynamic Island, minimal — one glyph, shown when another activity shares the island. Apple only.</summary>
    public const string Minimal = "minimal";
    /// <summary>Dynamic Island, expanded, leading region. Apple only.</summary>
    public const string ExpandedLeading = "expandedLeading";
    /// <summary>Dynamic Island, expanded, trailing region. Apple only.</summary>
    public const string ExpandedTrailing = "expandedTrailing";
    /// <summary>Dynamic Island, expanded, center region. Apple only.</summary>
    public const string ExpandedCenter = "expandedCenter";
    /// <summary>Dynamic Island, expanded, bottom region. Apple only.</summary>
    public const string ExpandedBottom = "expandedBottom";
}

/// <summary>The four regions of an expanded Dynamic Island. All optional; omitted regions collapse.</summary>
public sealed class LiveExpanded
{
    internal LiveView? LeadingView;
    internal LiveView? TrailingView;
    internal LiveView? CenterView;
    internal LiveView? BottomView;

    public LiveExpanded Leading(LiveView view) { LeadingView = view; return this; }
    public LiveExpanded Trailing(LiveView view) { TrailingView = view; return this; }
    public LiveExpanded Center(LiveView view) { CenterView = view; return this; }
    public LiveExpanded Bottom(LiveView view) { BottomView = view; return this; }
}

/// <summary>
/// A Live Activity: one surface, several simultaneous presentations, driven by a state value.
///
/// The slot shape is not decoration. A Live Activity must supply a lock-screen presentation *and* up to
/// seven Dynamic Island regions at once, and they are not one tree scaled down — a minimal presentation
/// is typically a single glyph beside the sensor housing. Modelling this as a single <c>Body</c> would
/// force every app to re-derive the shapes from a size hint the platform never gives them.
///
/// State is a plain value, not a <see cref="State{T}"/>. An activity is almost always updated from
/// *background* code — a push handler, a job, a location callback — with no view tree alive anywhere in
/// the process, so the invalidate-and-re-render machinery has nothing to hang off.
/// </summary>
/// <typeparam name="TState">The activity's content state. Kept small; it rides inside a 4 KB push payload.</typeparam>
public abstract class LiveActivity<TState>
{
    /// <summary>Stable id for this activity, shared with <see cref="ISurfaceChannel"/> and widgets.</summary>
    public abstract string Kind { get; }

    /// <summary>The lock-screen / banner presentation. Required — it is the only universal slot.</summary>
    public abstract LiveView LockScreen(TState state);

    /// <summary>Dynamic Island collapsed-leading. Null falls back to the app icon.</summary>
    public virtual LiveView? CompactLeading(TState state) => null;

    /// <summary>Dynamic Island collapsed-trailing. Null renders nothing there.</summary>
    public virtual LiveView? CompactTrailing(TState state) => null;

    /// <summary>Dynamic Island minimal — one glyph. Null falls back to the app icon.</summary>
    public virtual LiveView? Minimal(TState state) => null;

    /// <summary>The expanded Dynamic Island regions. Null means the island does not expand.</summary>
    public virtual LiveExpanded? Expanded(TState state) => null;

    /// <summary>How the Android side rasterizes this activity's trees. Ignored on Apple.</summary>
    public virtual LiveRenderMode RenderMode => LiveRenderMode.Native;

    /// <summary>
    /// Builds every slot, validates each against the target, and returns one snapshot plus the union of
    /// the handlers found. Called by the platform drivers; call it directly to unit-test an activity.
    /// </summary>
    public LiveActivityPayload BuildPayload(TState state, LiveTarget target, double nowUnixSeconds)
    {
        var trees = new Dictionary<string, string>();
        var actions = new Dictionary<string, Action<string?>>();
        var diagnostics = new List<LiveDiagnostic>();

        Add(LiveSlot.LockScreen, LockScreen(state));
        Add(LiveSlot.CompactLeading, CompactLeading(state));
        Add(LiveSlot.CompactTrailing, CompactTrailing(state));
        Add(LiveSlot.Minimal, Minimal(state));

        if (Expanded(state) is { } expanded)
        {
            Add(LiveSlot.ExpandedLeading, expanded.LeadingView);
            Add(LiveSlot.ExpandedTrailing, expanded.TrailingView);
            Add(LiveSlot.ExpandedCenter, expanded.CenterView);
            Add(LiveSlot.ExpandedBottom, expanded.BottomView);
        }

        var snapshot = new SurfaceSnapshot
        {
            Kind = Kind,
            Surface = LiveSurface.Activity,
            Trees = trees,
            PublishedAt = nowUnixSeconds,
        };

        // The 4 KB ceiling is on the *whole* content state, not on any one slot — so the combined size is
        // what gets checked. A per-slot check would pass eight times and still be rejected by APNs.
        if (target.Platform == LivePlatform.Apple && snapshot.Bytes > LiveBudget.ActivityHardBytes)
            diagnostics.Add(new("SDNL001", LiveSeverity.Error,
                $"'{Kind}' serializes to {snapshot.Bytes} bytes across {trees.Count} slots; APNs caps a Live " +
                $"Activity payload at {LiveBudget.ActivityHardBytes}. The update would be rejected silently."));
        else if (target.Platform == LivePlatform.Apple && snapshot.Bytes > LiveBudget.ActivityWarnBytes)
            diagnostics.Add(new("SDNL002", LiveSeverity.Warning,
                $"'{Kind}' serializes to {snapshot.Bytes} bytes across {trees.Count} slots, past the " +
                $"{LiveBudget.ActivityWarnBytes}-byte guideline."));

        return new LiveActivityPayload(snapshot, actions, diagnostics);

        void Add(string slot, LiveView? view)
        {
            if (view is null) return;

            var ctx = new LiveContext { Surface = LiveSurface.Activity };
            var node = view.ToNode(ctx, slot);
            var json = LiveWire.Serialize(node);
            trees[slot] = json;

            var payload = new LivePayload(json, node, ctx.Actions);
            foreach (var d in LiveValidator.Validate(payload, target))
            {
                // Byte-budget findings are re-derived above against the combined size; a per-slot one here
                // would be both wrong and noisy.
                if (d.Code is "SDNL001" or "SDNL002") continue;
                diagnostics.Add(d with { Message = $"[{slot}] {d.Message}" });
            }

            foreach (var kv in ctx.Actions) actions[kv.Key] = kv.Value;
        }
    }
}

/// <summary>Everything the driver needs to start or update one activity.</summary>
/// <param name="Snapshot">The slot trees, ready for the channel.</param>
/// <param name="Actions">Handlers keyed by node id, for the intent/receiver route back.</param>
/// <param name="Diagnostics">Findings from every slot, plus the combined byte budget.</param>
public readonly record struct LiveActivityPayload(
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
}

/// <summary>
/// What a platform must provide to run Live Activities. Implemented by <c>AppleLiveActivities</c> and
/// <c>AndroidLiveActivities</c>; the shape is the same even though the mechanisms share nothing —
/// ActivityKit on one side, an ongoing notification (or an Android 16 Live Update) on the other.
/// </summary>
public interface ILiveActivityDriver
{
    /// <summary>Starts an activity, returning the platform's own handle for it.</summary>
    Task<string> StartAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default);

    /// <summary>Pushes new content to a running activity.</summary>
    Task UpdateAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default);

    /// <summary>
    /// Ends an activity. <paramref name="finalState"/> renders the dismissal presentation, which the
    /// system may keep on screen for a while after the activity itself is over.
    /// </summary>
    Task EndAsync<TState>(LiveActivity<TState> activity, TState? finalState = default, CancellationToken ct = default);

    /// <summary>Handles for every activity of this kind that is currently live.</summary>
    Task<IReadOnlyList<string>> ActiveAsync(string kind, CancellationToken ct = default);
}

/// <summary>Unix-seconds helper shared by the drivers, kept in one place so the wire agrees everywhere.</summary>
public static class LiveClock
{
    /// <summary>Now, as unix seconds with millisecond resolution.</summary>
    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>An instant, as unix seconds.</summary>
    public static double At(DateTimeOffset when) => when.ToUnixTimeMilliseconds() / 1000.0;

    /// <summary>Formats unix seconds the way the wire writes them.</summary>
    public static string Format(double unixSeconds) =>
        unixSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
