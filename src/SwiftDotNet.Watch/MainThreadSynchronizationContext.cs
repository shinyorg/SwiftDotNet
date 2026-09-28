using System.Collections.Concurrent;

namespace SwiftDotNet.Watch;

/// <summary>
/// The watch's UI-thread pump. <see cref="SwiftApp"/> captures it at <c>Run</c> and posts every coalesced
/// render here, and <c>await</c> continuations resume here, so app state is only touched on the SwiftUI
/// main thread. There is no managed run loop on the watch: posting asks the Swift host to call back
/// (<c>swiftdotnet_watch_drain</c>) on its main queue, which runs everything queued so far.
/// </summary>
sealed class MainThreadSynchronizationContext(IWatchPlatform platform, int mainThreadId) : SynchronizationContext
{
    readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _queue = new();
    int _drainRequested;

    public bool IsMainThread => Environment.CurrentManagedThreadId == mainThreadId;

    public override void Post(SendOrPostCallback d, object? state)
    {
        _queue.Enqueue((d, state));
        if (Interlocked.Exchange(ref _drainRequested, 1) == 0)
            platform.RequestMainThreadDrain();
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (IsMainThread)
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        Exception? error = null;
        Post(_ =>
        {
            try { d(state); }
            catch (Exception ex) { error = ex; }
            finally { done.Set(); }
        }, null);
        done.Wait();
        if (error is not null)
            throw new AggregateException(error);
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Runs everything queued so far, on the main thread. Returns how many items ran.</summary>
    public int Drain(Action<Exception> onError)
    {
        Volatile.Write(ref _drainRequested, 0);
        var ran = 0;
        while (_queue.TryDequeue(out var item))
        {
            ran++;
            try { item.Callback(item.State); }
            catch (Exception ex) { onError(ex); }
        }
        return ran;
    }
}
