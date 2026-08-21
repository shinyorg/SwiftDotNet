namespace SwiftDotNet;

/// <summary>
/// Runs <see cref="LiveActivity{TState}"/> through ActivityKit.
///
/// <para>Unlike the Android driver, this one carries almost no logic: the slots are serialized by the
/// shared vocabulary, the shim hands them to ActivityKit as a <c>ContentState</c>, and a Swift
/// interpreter in the widget extension renders them. What this class owns is the *guard rails* around
/// three ActivityKit behaviours that fail silently otherwise.</para>
///
/// <list type="number">
///   <item><description><b>The 4 KB ceiling.</b> The whole content state rides inside an APNs payload.
///   Over the limit, the update is rejected and the activity keeps showing stale content, with no error
///   raised anywhere. <see cref="LiveActivityPayload.Assert"/> is called before every publish.</description></item>
///   <item><description><b>Foreground-only start.</b> <c>Activity.request</c> throws when the app is not
///   in the foreground. The shim returns null rather than propagating, and this class turns that into an
///   exception that says why.</description></item>
///   <item><description><b>Handlers outlive nothing.</b> Node ids are positional and re-registered on
///   every publish, so a tap against a tree from a previous launch has no handler. That is normal, and
///   the mailbox in <see cref="ISurfaceChannel"/> is what catches it.</description></item>
/// </list>
/// </summary>
public sealed class AppleLiveActivities : ILiveActivityDriver
{
    /// <inheritdoc />
    public Task<string> StartAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default)
    {
        var payload = Publish(activity, state);
        var id = AppleLiveBridge.TakeString(
            AppleLiveBridge.Start(activity.Kind, FileSurfaceChannel.Encode(payload.Snapshot)));

        if (id is null)
        {
            throw new InvalidOperationException(
                $"ActivityKit refused to start '{activity.Kind}'. The usual cause is that the app was not " +
                "in the foreground - Activity.request requires it - or that the user has Live Activities " +
                "turned off for this app in Settings.");
        }

        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task UpdateAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default)
    {
        var payload = Publish(activity, state);
        AppleLiveBridge.Update(activity.Kind, FileSurfaceChannel.Encode(payload.Snapshot));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EndAsync<TState>(LiveActivity<TState> activity, TState? finalState = default, CancellationToken ct = default)
    {
        string? snapshot = null;
        if (finalState is not null)
        {
            // A final state renders the dismissal presentation, which the system may keep on screen for a
            // while after the activity is over - the difference between "Delivered" and a card that
            // vanishes the instant it completes.
            var payload = activity.BuildPayload(finalState, Target, LiveClock.Now);
            payload.Assert();
            snapshot = FileSurfaceChannel.Encode(payload.Snapshot);
        }

        AppleLiveBridge.End(activity.Kind, snapshot);
        SwiftDotNetLive.Router.Forget(activity.Kind);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ActiveAsync(string kind, CancellationToken ct = default)
    {
        var raw = AppleLiveBridge.TakeString(AppleLiveBridge.Active(kind));
        var ids = string.IsNullOrEmpty(raw)
            ? Array.Empty<string>()
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return Task.FromResult<IReadOnlyList<string>>(ids);
    }

    LiveActivityPayload Publish<TState>(LiveActivity<TState> activity, TState state)
    {
        var payload = activity.BuildPayload(state, Target, LiveClock.Now);
        payload.Assert();
        SwiftDotNetLive.Router.Register(activity.Kind, payload.Actions);
        return payload;
    }

    static LiveTarget Target { get; } = new()
    {
        Surface = LiveSurface.Activity,
        Platform = LivePlatform.Apple,
    };
}
