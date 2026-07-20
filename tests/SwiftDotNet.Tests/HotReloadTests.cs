using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Locks in the hot-reload contract of <see cref="SwiftApp.Invalidate"/> — the one call
/// <see cref="HotReload"/> makes when <c>dotnet watch</c> pushes an edit into the process. See
/// <c>Core/HotReload.cs</c>.
///
/// The two properties that make hot reload useful rather than a glorified restart:
/// <list type="bullet">
/// <item>a reload emits a <b>full replace</b>, not a diff against the tree the old code built;</item>
/// <item><see cref="State{T}"/> on the retained root <b>survives</b> it.</item>
/// </list>
///
/// These drive shared static runtime state, so they must not run in parallel with each other.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class HotReloadTests
{
    [Fact]
    public void Invalidate_EmitsFullReplace_EvenWhenNothingChanged()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();

        var view = new CounterView();
        var bridge = new CapturingBridge();

        SwiftApp.Run(view, bridge);
        Assert.Equal("replace", SoleOp(bridge.Last));   // first render is always a replace

        // A normal state change diffs down to a prop update on the Text node.
        view.Count.Value = 1;
        pump.Drain();
        Assert.Equal("updateProps", SoleOp(bridge.Last));

        // A reload, by contrast, discards the baseline: the whole root ships again even though the
        // tree is identical to the one already on the host.
        SwiftApp.Invalidate();
        pump.Drain();
        Assert.Equal("replace", SoleOp(bridge.Last));

        // And the next ordinary change goes back to a minimal diff — the baseline was re-established.
        view.Count.Value = 2;
        pump.Drain();
        Assert.Equal("updateProps", SoleOp(bridge.Last));
    }

    [Fact]
    public void Invalidate_PreservesStateOnTheRetainedRoot()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();

        var view = new CounterView();
        var bridge = new CapturingBridge();

        SwiftApp.Run(view, bridge);
        view.Count.Value = 7;
        pump.Drain();

        SwiftApp.Invalidate();
        pump.Drain();

        // The replaced tree was rebuilt from the *same* root instance, so the state cell still holds 7 —
        // this is what lets you keep your place in the app across an edit.
        Assert.Equal(7, view.Count.Value);
        Assert.Contains("count: 7", bridge.Last);
    }

    [Fact]
    public void Invalidate_CoalescesWithPendingStateChanges_IntoOneRender()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();

        var view = new CounterView();
        var bridge = new CapturingBridge();

        SwiftApp.Run(view, bridge);
        Assert.Equal(1, bridge.RenderCount);

        view.Count.Value = 1;
        SwiftApp.Invalidate();      // arrives in the same tick as the state change
        Assert.Equal(1, pump.PendingCount);

        pump.Drain();

        Assert.Equal(2, bridge.RenderCount);
        Assert.Equal("replace", SoleOp(bridge.Last));   // the reload wins over the would-be prop diff
    }

    [Fact]
    public void Invalidate_FromBackgroundThread_RendersOnTheUiThread()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();

        var view = new CounterView();
        int? renderThread = null;
        var bridge = new CapturingBridge(onRender: () => renderThread = Environment.CurrentManagedThreadId);

        SwiftApp.Run(view, bridge);

        // The runtime's hot-reload agent calls UpdateApplication on its own thread, not the UI thread.
        int? agentThread = null;
        var agent = new Thread(() =>
        {
            agentThread = Environment.CurrentManagedThreadId;
            SwiftApp.Invalidate();
        });
        agent.Start();
        agent.Join();

        Assert.Equal(1, pump.PendingCount);   // queued, not run on the agent thread
        pump.Drain();

        Assert.NotEqual(agentThread, renderThread);
        Assert.Equal(Environment.CurrentManagedThreadId, renderThread);
    }

    [Fact]
    public void SkiaSurvivesAReload_TreeIsRebuiltAndStillHitTests()
    {
        // Install our own pump: xUnit installs an AsyncTestSyncContext that runs posted work on the
        // thread pool, so without this the reload render would race the RenderPng below.
        var pump = new ManualPump();
        using var restore = pump.Install();

        var view = new CounterView();
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);

        SwiftApp.Run(view, bridge);
        host.RenderPng(400, 800);
        Assert.True(bridge.TryGetFrame("0.1", out _));   // the button

        // Replace the whole tree mid-session, the way a reload does, then keep using the app: the
        // backend must have torn down and rebuilt rather than left a stale widget tree behind.
        SwiftApp.Invalidate();
        pump.Drain();
        host.RenderPng(400, 800);                        // lays out and arranges the rebuilt tree

        Assert.True(bridge.TryGetFrame("0.1", out var button));
        Assert.False(button.IsEmpty);                    // rebuilt *and* laid out, not a zero-rect stub
        Assert.True(host.Tap(button.MidX, button.MidY));
        Assert.Equal(1, view.Count.Value);               // events still route after the replace

        pump.Drain();                                    // the tap's re-render, so nothing is left queued
    }

    /// <summary>The single op kind in a one-op patch — asserts the diff collapsed to what we expect.</summary>
    static string SoleOp(string json)
    {
        var ops = JsonDocument.Parse(json).RootElement.GetProperty("ops");
        Assert.Equal(1, ops.GetArrayLength());
        return ops[0].GetProperty("op").GetString()!;
    }
}

/// <summary>A root with one state cell and a button, so a reload has both state and an action to preserve.</summary>
file sealed class CounterView : View
{
    public State<int> Count { get; } = new(0);

    public override View? Body => new VStack(
        new Text("count: " + Count.Value),
        new Button("bump", () => Count.Value++)
    );
}

/// <summary>Bridge that keeps the last patch JSON so a test can assert the op kind that crossed the wire.</summary>
file sealed class CapturingBridge(Action? onRender = null) : IBridge
{
    public int RenderCount { get; private set; }
    public string Last { get; private set; } = "";

    public void Render(string json)
    {
        RenderCount++;
        Last = json;
        onRender?.Invoke();
    }

    public void SetEventHandler(Action<string, string?> handler) { }
}

/// <summary>Queues posted callbacks instead of running them, so scheduling is assertable.</summary>
file sealed class ManualPump : SynchronizationContext
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
