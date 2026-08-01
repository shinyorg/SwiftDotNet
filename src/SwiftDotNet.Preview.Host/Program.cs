using System.Diagnostics;
using System.Reflection;
using SwiftDotNet;
using SwiftDotNet.Preview;

// The out-of-process half of the "SwiftDotNet Preview" tool window: loads a view assembly, renders it
// headlessly with Skia, and streams PNG frames over the dev-tools socket while taking input back.
//
// Out-of-process on purpose (plans/rider-plugin-plan.md Decision 6): the previewed code is the user's,
// it can hang or throw, and none of that should be able to take the IDE with it.

var options = PreviewOptions.Parse(args);
if (options is null)
{
    Console.Error.WriteLine(PreviewOptions.Usage);
    return 2;
}

using var server = new DevToolsServer(options.Port)
{
    Greeting = $"backend=skia-preview;pid={Environment.ProcessId};protocol=1",
};

// Printed so the IDE can learn the port when it passed 0, and so a developer running this by hand has
// something to point a client at.
Console.WriteLine($"[preview] listening on 127.0.0.1:{server.Port}");

// SwiftApp marshals renders onto whatever SynchronizationContext was current when Run was called. Install
// one that queues onto this loop *before* the first session starts, or a State mutation from a timer —
// or from the socket thread below — rebuilds the tree underneath the paint. Same reasoning as the Silk
// sample's RenderLoopSyncContext; see docs/hot-reload.md.
var pump = new RenderLoopSyncContext();
SynchronizationContext.SetSynchronizationContext(pump);

var commands = new System.Collections.Concurrent.ConcurrentQueue<DevToolsProtocol.Frame>();
server.CommandReceived += frame => commands.Enqueue(frame);

var width = options.Width;
var height = options.Height;
var dark = options.Dark;

PreviewSession? session = null;
var painted = false;                         // has the current session laid out at the current size?
var reloadRequested = true;                  // the first pass through the loop is the initial load

// Seeded from the file rather than left at MinValue: otherwise the first watch probe sees "newer than
// never", and every preview loads the app twice before it has drawn a frame.
var lastWrite = SafeLastWrite(options.AssemblyPath);
var clock = Stopwatch.StartNew();
var lastTick = clock.Elapsed.TotalSeconds;
var lastProbe = 0.0;

using var stopping = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stopping.Set(); };

while (!stopping.IsSet)
{
    var now = clock.Elapsed.TotalSeconds;
    var dt = now - lastTick;
    lastTick = now;

    while (commands.TryDequeue(out var frame))
    {
        switch (frame.Type)
        {
            case DevToolsProtocol.ClientFrames.Input:
                if (session is not null)
                {
                    // Hit testing reads the layout from the last paint, so an input that arrives before
                    // the first frame has nothing to hit and is silently swallowed. That is a real race
                    // rather than a theoretical one: the IDE connects and can forward a click in the same
                    // few milliseconds. Establish layout first, then dispatch.
                    if (!painted)
                    {
                        session.RenderPng(width, height);
                        painted = true;
                    }
                    session.HandleInput(frame.Text);
                }
                break;

            case DevToolsProtocol.ClientFrames.Resize:
                var size = frame.Text.Split(' ');
                if (size.Length == 2 && int.TryParse(size[0], out var w) && int.TryParse(size[1], out var h))
                {
                    // Clamped rather than trusted: a tool window can be dragged to one pixel wide, and
                    // SKSurface.Create returns null for a zero-area surface, which would fault the paint.
                    width = Math.Clamp(w, 64, 4096);
                    height = Math.Clamp(h, 64, 4096);
                }
                break;

            case DevToolsProtocol.ClientFrames.Theme:
                dark = frame.Text.Trim() == "dark";
                if (session is not null)
                    session.Dark = dark;
                break;

            case DevToolsProtocol.ClientFrames.Reload:
                reloadRequested = true;
                break;

            case DevToolsProtocol.ClientFrames.Ping:
                server.Broadcast(DevToolsProtocol.ServerFrames.Log, "pong");
                break;
        }
    }

    // Poll the file rather than using a FileSystemWatcher: a build writes the assembly in several steps
    // and editors vary in how they do it, so watchers fire two or three times per build and sometimes on
    // a half-written file. A timestamp check settles by itself.
    if (options.Watch && now - lastProbe > 0.25)
    {
        lastProbe = now;
        var write = SafeLastWrite(options.AssemblyPath);

        // The second half of the condition waits for the file to stop being written before reacting.
        // A build writes the assembly in stages, and loading it at the wrong moment gives a
        // BadImageFormatException that looks like the developer's fault and isn't.
        if (write > lastWrite && write < DateTime.UtcNow.AddMilliseconds(-150))
        {
            lastWrite = write;
            reloadRequested = true;
        }
    }

    if (reloadRequested)
    {
        reloadRequested = false;
        try
        {
            session?.Dispose();
            session = null;

            // Unload is cooperative: the context goes away once nothing references it. Collect here so
            // a long-lived preview does not accumulate one dead copy of the app per save.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            session = PreviewSession.Load(options with { Dark = dark });
            painted = false;
            server.Broadcast(DevToolsProtocol.ServerFrames.Log, $"loaded {session.Description}");
        }
        catch (Exception ex)
        {
            // The developer's code failed to load or construct. That is ordinary during editing, so the
            // preview reports it and keeps running rather than exiting and needing to be restarted.
            var message = ex is PreviewException or TargetInvocationException
                ? (ex.InnerException ?? ex).Message
                : ex.ToString();
            server.Broadcast(DevToolsProtocol.ServerFrames.Log, $"error {message}");
            Console.Error.WriteLine($"[preview] {message}");
        }
    }

    if (session is not null)
    {
        pump.Drain();

        if (session.Step(dt) && server.HasClients)
        {
            try
            {
                server.Broadcast(DevToolsProtocol.ServerFrames.Frame, session.RenderPng(width, height));
                painted = true;
            }
            catch (Exception ex)
            {
                server.Broadcast(DevToolsProtocol.ServerFrames.Log, $"error paint failed: {ex.Message}");
            }
        }
    }

    stopping.Wait(16);
}

session?.Dispose();
return 0;

static DateTime SafeLastWrite(string path)
{
    // Mid-build the file can be missing or locked. "No newer than what we saw" is the right answer:
    // the next probe, a quarter of a second later, gets the real one.
    try
    {
        return File.GetLastWriteTimeUtc(path);
    }
    catch (IOException)
    {
        return DateTime.MinValue;
    }
}

/// <summary>Command line for the preview host.</summary>
internal sealed record PreviewOptions(
    string AssemblyPath,
    string? ViewTypeName,
    string? Initializer,
    int Port,
    int Width,
    int Height,
    bool Dark,
    bool Watch)
{
    public const string Usage = """
        SwiftDotNet preview host

          --assembly <path>     the built assembly holding the views (required)
          --view <TypeName>     a View subclass to preview; default: SwiftProgram.CreateSwiftApp()
          --init <Type.Method>  static method to call before loading (e.g. renderer registration)
          --port <n>            dev-tools port; 0 (default) picks a free one and prints it
          --width <n>           surface width  (default 390)
          --height <n>          surface height (default 844)
          --dark                start in dark appearance
          --no-watch            do not reload when the assembly is rebuilt
        """;

    public static PreviewOptions? Parse(string[] args)
    {
        string? assembly = null, view = null, init = null;
        int port = 0, width = 390, height = 844;
        bool dark = false, watch = true;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly" when i + 1 < args.Length: assembly = args[++i]; break;
                case "--view" when i + 1 < args.Length: view = args[++i]; break;
                case "--init" when i + 1 < args.Length: init = args[++i]; break;
                case "--port" when i + 1 < args.Length: int.TryParse(args[++i], out port); break;
                case "--width" when i + 1 < args.Length: int.TryParse(args[++i], out width); break;
                case "--height" when i + 1 < args.Length: int.TryParse(args[++i], out height); break;
                case "--dark": dark = true; break;
                case "--no-watch": watch = false; break;
                default: return null;
            }
        }

        if (assembly is null || !File.Exists(assembly))
            return null;

        return new PreviewOptions(Path.GetFullPath(assembly), view, init, port, width, height, dark, watch);
    }
}

/// <summary>
/// Queues work onto the render loop. Copied in shape from the Silk sample rather than shared with it:
/// this host has no dependency on the samples, and the type is twenty lines.
/// </summary>
internal sealed class RenderLoopSyncContext : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (_queue)
            _queue.Enqueue((d, state));
    }

    /// <summary>Run everything queued so far. Called once per frame, before painting.</summary>
    public void Drain()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) item;
            lock (_queue)
            {
                if (_queue.Count == 0)
                    return;
                item = _queue.Dequeue();
            }
            item.Callback(item.State);
        }
    }
}
