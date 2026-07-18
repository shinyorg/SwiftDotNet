using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Locks in the render-scheduling contract of <see cref="SwiftApp"/>: multiple state mutations in
/// one tick coalesce into a single render, renders are marshaled onto the captured UI thread, and
/// <see cref="SwiftApp.Transaction"/> flushes exactly once. See <c>Core/SwiftApp.cs</c>.
///
/// These tests drive shared static runtime state, so they must not run in parallel with each other.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class RenderSchedulingTests
{
    [Fact]
    public void MultipleStateMutations_CoalesceIntoOneRender()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();

        var view = new TwoStateView();
        var bridge = new CountingBridge();

        SwiftApp.Run(view, bridge);      // initial synchronous render
        Assert.Equal(1, bridge.RenderCount);

        // Two genuine changes in the same tick.
        view.A.Value = "x";
        view.B.Value = "y";

        // Deferred: nothing has rendered yet, and only ONE callback was posted (the second fold in).
        Assert.Equal(1, bridge.RenderCount);
        Assert.Equal(1, pump.PendingCount);

        pump.Drain();

        // Both mutations produced a single additional render.
        Assert.Equal(2, bridge.RenderCount);
    }

    [Fact]
    public void RenderIsMarshaledOntoCapturedUiThread()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();

        var view = new TwoStateView();
        int? renderThread = null;
        var bridge = new CountingBridge(onRender: () => renderThread = Environment.CurrentManagedThreadId);

        SwiftApp.Run(view, bridge);

        int? backgroundThread = null;
        // Mutate state from a different thread; the render must not run there.
        var worker = new Thread(() =>
        {
            backgroundThread = Environment.CurrentManagedThreadId;
            view.A.Value = "from-bg";
        });
        worker.Start();
        worker.Join();

        // The posted render is still queued on the pump, not run on the worker thread.
        Assert.Equal(1, pump.PendingCount);

        pump.Drain();  // drained on the test (UI) thread

        Assert.NotNull(renderThread);
        Assert.NotEqual(backgroundThread, renderThread);
        Assert.Equal(Environment.CurrentManagedThreadId, renderThread);
    }

    [Fact]
    public void Transaction_FlushesExactlyOnce_Synchronously()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();

        var view = new TwoStateView();
        var bridge = new CountingBridge();

        SwiftApp.Run(view, bridge);
        Assert.Equal(1, bridge.RenderCount);

        SwiftApp.Transaction(() =>
        {
            view.A.Value = "1";
            view.B.Value = "2";
        });

        // Transaction renders synchronously (one render) and posts nothing to the pump.
        Assert.Equal(2, bridge.RenderCount);
        Assert.Equal(0, pump.PendingCount);
    }

    [Fact]
    public void UnchangedValue_DoesNotRender()
    {
        var pump = new ManualSyncContext();
        using var _ = pump.Install();

        var view = new TwoStateView();
        var bridge = new CountingBridge();

        SwiftApp.Run(view, bridge);
        Assert.Equal(1, bridge.RenderCount);

        view.A.Value = view.A.Value;  // equal assignment — no-op

        Assert.Equal(0, pump.PendingCount);
        pump.Drain();
        Assert.Equal(1, bridge.RenderCount);
    }
}

/// <summary>A composite view whose rendered text derives from two independent state cells.</summary>
file sealed class TwoStateView : View
{
    public State<string> A { get; } = new("a");
    public State<string> B { get; } = new("b");

    public override View? Body => new Text(A.Value + "|" + B.Value);
}

/// <summary>Bridge that counts <see cref="IBridge.Render"/> calls and optionally records the thread.</summary>
file sealed class CountingBridge(Action? onRender = null) : IBridge
{
    public int RenderCount { get; private set; }

    public void Render(string json)
    {
        RenderCount++;
        onRender?.Invoke();
    }

    public void SetEventHandler(Action<string, string?> handler) { }
}

/// <summary>
/// A single-threaded <see cref="SynchronizationContext"/> that queues posted callbacks instead of
/// running them, so a test can assert what was scheduled and drain it deterministically.
/// </summary>
file sealed class ManualSyncContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();

    public int PendingCount => _queue.Count;

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public void Drain()
    {
        while (_queue.Count > 0)
        {
            var (cb, state) = _queue.Dequeue();
            cb(state);
        }
    }

    /// <summary>Install as current for the duration; restores the previous context on dispose.</summary>
    public IDisposable Install()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        return new Restore(previous);
    }

    sealed class Restore(SynchronizationContext? previous) : IDisposable
    {
        public void Dispose() => SetSynchronizationContext(previous);
    }
}
