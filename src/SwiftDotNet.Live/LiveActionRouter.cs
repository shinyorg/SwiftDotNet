namespace SwiftDotNet;

/// <summary>
/// Keeps the handlers for every published surface and dispatches an inbound <see cref="SurfaceAction"/>
/// to the right one.
///
/// This is the piece that makes an interactive surface work at all, and it exists because the surfaces
/// hand back an *id*, never a callback. On Apple a <c>LiveActivityIntent</c> arrives in the app process
/// carrying only the node id it was configured with; on Android a <c>PendingIntent</c> arrives at our
/// receiver with the id as an extra. Both then need what <c>SwiftApp</c>'s own <c>_actions</c> dictionary
/// does for ordinary views: id → delegate.
///
/// The lifetime problem is the interesting part. A surface outlives the process that published it, so an
/// action can arrive against a tree published by a *previous launch* whose handlers no longer exist.
/// That is not an error and must not throw — <see cref="Dispatch"/> reports it as unhandled and the app
/// decides. The durable answer is a deep link, which survives a relaunch; a handler is the convenience.
/// </summary>
public sealed class LiveActionRouter
{
    readonly Dictionary<string, Dictionary<string, Action<string?>>> _byKind = new();
    readonly object _gate = new();

    /// <summary>Replaces the handler set for a surface. Called on every publish, since ids are positional.</summary>
    public void Register(string kind, IReadOnlyDictionary<string, Action<string?>> actions)
    {
        lock (_gate)
            _byKind[kind] = new Dictionary<string, Action<string?>>(actions);
    }

    /// <summary>Drops a surface's handlers — on withdraw, or when an activity ends.</summary>
    public void Forget(string kind)
    {
        lock (_gate) _byKind.Remove(kind);
    }

    /// <summary>
    /// Runs the handler for an action. Returns false when there is none — the normal outcome for an
    /// action against a surface published by an earlier launch of the app.
    /// </summary>
    public bool Dispatch(SurfaceAction action)
    {
        Action<string?>? handler;
        lock (_gate)
        {
            if (!_byKind.TryGetValue(action.Kind, out var actions)) return false;
            if (!actions.TryGetValue(action.NodeId, out handler)) return false;
        }

        handler(action.Value);
        return true;
    }

    /// <summary>
    /// Drains a channel's mailbox and dispatches everything in it, returning what had no handler.
    /// Call on app foreground: on Apple these accumulated while the app was suspended.
    /// </summary>
    public async Task<IReadOnlyList<SurfaceAction>> DrainAsync(ISurfaceChannel channel, CancellationToken ct = default)
    {
        var pending = await channel.DrainActionsAsync(ct).ConfigureAwait(false);
        List<SurfaceAction>? unhandled = null;

        foreach (var action in pending)
        {
            if (!Dispatch(action))
                (unhandled ??= new()).Add(action);
        }

        return (IReadOnlyList<SurfaceAction>?)unhandled ?? Array.Empty<SurfaceAction>();
    }
}
