namespace SwiftDotNet.Graphics;

/// <summary>
/// A <see cref="SynchronizationContext"/> that queues callbacks and runs them on whichever thread calls
/// <see cref="Drain"/> — the minimum a windowing or game host owes <c>SwiftApp</c>.
/// </summary>
/// <remarks>
/// <para>Game loops (MonoGame's <c>Game.Update</c>, Godot's <c>_Process</c>) and raw windowing loops
/// (GLFW/Silk) have no synchronization context of their own. Without one, anything that mutates
/// <c>State&lt;T&gt;</c> off the loop thread — a timer, a socket, the hot-reload agent applying an edit —
/// rebuilds the scene tree concurrently with the paint pass reading it.</para>
///
/// <para>Install it <em>before</em> <c>SwiftApp.Run</c>, which captures the ambient context, then call
/// <see cref="Drain"/> once per frame before painting.</para>
/// </remarks>
public sealed class FrameLoopSyncContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue) _queue.Enqueue((d, state));
    }

    /// <summary>Runs everything queued so far. Call once per frame, before painting.</summary>
    public void Drain()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (_queue)
            {
                if (_queue.Count == 0) return;
                item = _queue.Dequeue();
            }
            item.Callback(item.State);
        }
    }
}
