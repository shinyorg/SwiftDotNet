namespace SwiftDotNet;

/// <summary>One future state of a widget, and when it becomes current.</summary>
/// <param name="At">When this entry starts being shown.</param>
/// <param name="State">The state rendered from that moment.</param>
public readonly record struct WidgetEntry<TState>(DateTimeOffset At, TState State);

/// <summary>
/// A widget's future, as a set of dated states.
///
/// This is the shape that reconciles two platforms that could hardly be less alike. **iOS widgets are
/// pull-based**: the system asks a <c>TimelineProvider</c> for entries and renders them on its own
/// schedule, against a daily refresh budget the app cannot see or raise. **Android widgets are
/// push-based**: we call <c>updateAppWidget</c> whenever we like, and <c>updatePeriodMillis</c> has a
/// 30-minute floor we mostly ignore in favour of explicit updates.
///
/// Exposing "push" would produce apps that silently stop updating on iOS. Exposing "pull" would waste
/// Android's freedom. A timeline is the thing both can honour: Apple hands it to WidgetKit as-is; Android
/// renders entry zero immediately and schedules the rest.
///
/// The tail is a safety margin, not a nicety — publish several hours of entries so a suspended app still
/// shows something plausible. See <see cref="Widget{TState}.TimelineAsync"/> for why the app, not the
/// widget, computes this.
/// </summary>
public sealed class WidgetTimeline<TState>
{
    readonly List<WidgetEntry<TState>> _entries = new();

    /// <summary>The dated states, in the order they were added.</summary>
    public IReadOnlyList<WidgetEntry<TState>> Entries => _entries;

    /// <summary>
    /// When the host should come back for a fresh timeline. Apple maps it to
    /// <c>TimelineReloadPolicy.after(_:)</c>; Android to a one-shot WorkManager request.
    /// Null means "never on your own" — the app must publish again for anything to change.
    /// </summary>
    public DateTimeOffset? RefreshAt { get; private set; }

    /// <summary>Appends a dated state.</summary>
    public WidgetTimeline<TState> Entry(DateTimeOffset at, TState state)
    {
        _entries.Add(new WidgetEntry<TState>(at, state));
        return this;
    }

    /// <summary>Appends a state effective immediately.</summary>
    public WidgetTimeline<TState> Now(TState state) => Entry(DateTimeOffset.UtcNow, state);

    /// <summary>Asks the host to request a fresh timeline at <paramref name="when"/>.</summary>
    public WidgetTimeline<TState> RefreshAfter(DateTimeOffset when)
    {
        RefreshAt = when;
        return this;
    }
}

/// <summary>Entry points for building a <see cref="WidgetTimeline{TState}"/> fluently.</summary>
public static class WidgetTimeline
{
    /// <summary>Starts a timeline with one dated entry.</summary>
    public static WidgetTimeline<TState> Entry<TState>(DateTimeOffset at, TState state)
        => new WidgetTimeline<TState>().Entry(at, state);

    /// <summary>Starts a timeline with a single state effective immediately — the common case.</summary>
    public static WidgetTimeline<TState> Single<TState>(TState state)
        => new WidgetTimeline<TState>().Now(state);
}

/// <summary>
/// What a widget knows when it computes its timeline. Notably it does <b>not</b> carry a family: a
/// timeline is family-independent, and <see cref="Widget{TState}.Body"/> is what varies by shape.
/// </summary>
public sealed record WidgetContext
{
    /// <summary>The widget instances the user has actually placed. Empty is the normal case for most apps.</summary>
    public IReadOnlyList<SurfacePlacement> Placements { get; init; } = Array.Empty<SurfacePlacement>();

    /// <summary>Cancellation for whatever background work produced this refresh.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Every family actually placed, deduplicated. Rendering only these avoids wasted work.</summary>
    public IReadOnlyList<WidgetFamily> PlacedFamilies
    {
        get
        {
            var seen = new List<WidgetFamily>();
            foreach (var p in Placements)
            {
                if (p.Family is { } f && !seen.Contains(f)) seen.Add(f);
            }
            return seen;
        }
    }
}
