using SwiftDotNet;

namespace SwiftDotNet.Tests;

/// <summary>
/// Runs a view on the terminal backend without a terminal. There is no <c>TerminalApp</c> and no TTY —
/// <see cref="TuiBridge"/> builds and patches its retained visual tree perfectly well without one, which
/// is what makes this backend testable on CI at all (the GTK and Web backends are not).
///
/// <para>The pump is the important part. <see cref="SwiftApp"/> captures the ambient
/// <see cref="SynchronizationContext"/> to marshal renders, and xUnit installs one of its own, so a
/// state change would otherwise queue a render onto xUnit's scheduler and the assertion would race it.
/// Installing a manual pump makes every render land exactly when <see cref="ManualPump.Drain"/> says.</para>
/// </summary>
static class TuiTestHost
{
    public static (TuiBridge Bridge, ManualPump Pump) Run(View view)
    {
        var pump = new ManualPump();
        pump.Install();
        var bridge = new TuiBridge();
        SwiftApp.Run(view, bridge);      // initial render is synchronous
        return (bridge, pump);
    }
}

/// <summary>A <see cref="SynchronizationContext"/> that runs posted callbacks only when told to.</summary>
sealed class ManualPump : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

    public void Install() => SetSynchronizationContext(this);

    /// <summary>Runs every queued callback, including any queued while draining.</summary>
    public void Drain()
    {
        while (_queue.Count > 0)
        {
            var (cb, state) = _queue.Dequeue();
            cb(state);
        }
    }
}
