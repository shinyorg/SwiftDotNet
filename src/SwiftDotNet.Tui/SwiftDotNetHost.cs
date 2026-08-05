using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace SwiftDotNet;

/// <summary>
/// How the terminal host runs. Deliberately a SwiftDotNet type rather than Terminal.UI's
/// <c>TerminalAppOptions</c>: both libraries name several types identically (<c>State&lt;T&gt;</c>,
/// <c>Color</c>, <c>Style</c>, <c>VStack</c>, <c>Button</c>…), so an app that had to
/// <c>using XenoAtom.Terminal.UI;</c> just to configure its host would immediately hit ambiguity errors
/// on its own DSL. <see cref="Configure"/> is the escape hatch for the rare setting not surfaced here.
/// </summary>
public sealed class TuiHostOptions
{
    /// <summary>Take over the alternate screen (the default), or render inline below the shell prompt.</summary>
    public bool Fullscreen { get; set; } = true;

    /// <summary>
    /// Enable mouse reporting. On is what makes <c>.OnTapGesture</c> and list-row selection clickable
    /// rather than keyboard-only; off leaves the terminal's own text selection alone.
    /// </summary>
    public bool EnableMouse { get; set; } = true;

    /// <summary>Focus the first focusable control on start, so the app is usable without a click.</summary>
    public bool AutoFocus { get; set; } = true;

    /// <summary>Direct access to the underlying Terminal.UI options, applied last.</summary>
    public Action<TerminalAppOptions>? Configure { get; set; }
}

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a terminal — over SSH, in a container, on a
/// headless box, anywhere a TTY exists:
/// <code>
/// return SwiftDotNetHost.Run(new ContentView());
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    /// <summary>
    /// Runs <paramref name="root"/> until the app stops (the exit gesture, or a view calling
    /// <see cref="Stop"/>). <paramref name="services"/> becomes the app's ambient container, exactly as on
    /// every other backend.
    /// </summary>
    public static int Run(View root, IServiceProvider? services = null, TuiHostOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(root);

        var bridge = new TuiBridge();
        var app = new TerminalApp(bridge.Host, terminal: null, BuildOptions(options ?? new TuiHostOptions()));
        bridge.App = app;
        Current = app;

        // SwiftApp captures SynchronizationContext.Current to marshal renders onto the UI thread (that is
        // what lets a timer or socket assign State<T>.Value directly). Terminal.UI schedules through its
        // own Dispatcher rather than a sync-context, so install the adapter FIRST — afterwards would leave
        // SwiftApp with whatever context the console app started on, and patches would mutate visuals off
        // the dispatcher thread.
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new TuiSynchronizationContext(app));
        try
        {
            SwiftApp.Run(root, bridge, services);
            app.Run(CancellationToken.None);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            Current = null;
        }
        return 0;
    }

    /// <summary>The running app, for callers that need to reach the terminal directly. Null when not running.</summary>
    public static TerminalApp? Current { get; private set; }

    /// <summary>Stops the running app, returning the terminal to the shell.</summary>
    public static void Stop() => Current?.Stop();

    static TerminalAppOptions BuildOptions(TuiHostOptions options)
    {
        var terminalOptions = new TerminalAppOptions
        {
            HostKind = options.Fullscreen ? TerminalHostKind.Fullscreen : TerminalHostKind.Inline,
            EnableMouse = options.EnableMouse,
            EnableBracketedPaste = true,
            InitialFocusMode = options.AutoFocus ? InitialFocusMode.FirstFocusable : InitialFocusMode.None,
            // Set by TuiGraphics.Enable() in the optional SwiftDotNet.Tui.Graphics package. Reading it
            // here rather than taking it as a parameter is what keeps that package opt-in without the
            // host — or the app — having to name any of its types. It is init-only, hence the initializer.
            GraphicsPresenter = TuiImageOptions.GraphicsPresenter!,
        };

        options.Configure?.Invoke(terminalOptions);
        return terminalOptions;
    }
}

/// <summary>
/// Bridges <see cref="SynchronizationContext"/> onto Terminal.UI's dispatcher, so
/// <c>SwiftApp.RequestRender</c> lands on the thread that owns the visual tree. <see cref="Send"/> runs
/// inline when already on that thread and otherwise blocks on a posted callback — the standard contract,
/// and the one <c>async void</c> handlers resuming on this context depend on.
/// </summary>
sealed class TuiSynchronizationContext(TerminalApp app) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => app.Post(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (app.Dispatcher.CheckAccess())
        {
            d(state);
            return;
        }
        using var done = new ManualResetEventSlim();
        app.Post(() =>
        {
            try { d(state); }
            finally { done.Set(); }
        });
        done.Wait();
    }

    public override SynchronizationContext CreateCopy() => new TuiSynchronizationContext(app);
}
