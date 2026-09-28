using System.Text.Json;
using SwiftDotNet.Hosting;

namespace SwiftDotNet.Watch;

/// <summary>Scene phase of the watch app, as reported by SwiftUI's <c>scenePhase</c>.</summary>
public enum WatchAppPhase { Active = 0, Inactive = 1, Background = 2 }

/// <summary>
/// The managed half of a watch app. It runs the ordinary SwiftDotNet render loop (<see cref="SwiftApp"/>)
/// against a bridge that hands each patch to the Swift host, installs the main-thread pump, and routes the
/// host's UI events, native events and scene-phase changes back in.
///
/// Apps don't call <see cref="Start(SwiftDotNetApp, IWatchPlatform)"/> themselves: the watch SDK generates a
/// <c>swiftdotnet_watch_bootstrap</c> export that does it with <c>SwiftProgram.CreateSwiftApp()</c>.
/// </summary>
public static class WatchHost
{
    static readonly Dictionary<string, List<Action<JsonElement>>> s_eventHandlers = new(StringComparer.Ordinal);
    static IWatchPlatform? s_platform;
    static MainThreadSynchronizationContext? s_syncContext;
    static WatchBridge? s_bridge;
    static int s_generation;

    /// <summary>Raised on the main thread when the app becomes active, inactive or backgrounded.</summary>
    public static event Action<WatchAppPhase>? PhaseChanged;

    /// <summary>The current scene phase.</summary>
    public static WatchAppPhase Phase { get; private set; } = WatchAppPhase.Active;

    /// <summary>True once the host has started and until it is reset.</summary>
    public static bool IsRunning => s_bridge is not null;

    /// <summary>True on the watch's main (SwiftUI) thread, or when no host is running.</summary>
    public static bool IsMainThread => s_syncContext?.IsMainThread ?? true;

    /// <summary>Logs dispatches, native calls and renders. Enable with <c>SWIFTDOTNET_WATCH_TRACE=1</c>.</summary>
    public static bool TraceEnabled { get; set; } =
        Environment.GetEnvironmentVariable("SWIFTDOTNET_WATCH_TRACE") is "1" or "true";

    /// <summary>Incremented on every start; services use it to re-subscribe to native events after a restart.</summary>
    internal static int Generation => Volatile.Read(ref s_generation);

    internal static IWatchPlatform Platform =>
        s_platform ?? throw new InvalidOperationException("The SwiftDotNet watch host has not been started.");

    /// <summary>Start <paramref name="app"/>'s root view. Must be called on the main thread.</summary>
    public static void Start(SwiftDotNetApp app, IWatchPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(app);
        Attach(platform);
        // Created after Attach: the root's OnCreated may already call native services (Preferences, …).
        SwiftApp.Run(app.CreateRoot(), s_bridge!, app.Services);
    }

    /// <summary>Start <paramref name="root"/> directly. Must be called on the main thread.</summary>
    public static void Start(View root, IWatchPlatform platform, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        Attach(platform);
        SwiftApp.Run(root, s_bridge!, services);
    }

    static void Attach(IWatchPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        Interlocked.Increment(ref s_generation);
        s_platform = platform;
        s_syncContext = new MainThreadSynchronizationContext(platform, Environment.CurrentManagedThreadId);
        // SwiftApp captures SynchronizationContext.Current at Run, so this must be installed first: it is
        // what turns a State<T> assignment from a timer or an await into a posted, coalesced render.
        SynchronizationContext.SetSynchronizationContext(s_syncContext);
        s_bridge = new WatchBridge(platform);
    }

    /// <summary>Runs <paramref name="action"/> on the main thread (inline when already there).</summary>
    public static void RunOnMainThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (s_syncContext is null || s_syncContext.IsMainThread)
            action();
        else
            s_syncContext.Post(_ => action(), null);
    }

    /// <summary>Subscribes to an event raised by the native host (e.g. <c>phone.message</c>, <c>http.response</c>).</summary>
    public static IDisposable OnNativeEvent(string name, Action<JsonElement> handler)
    {
        lock (s_eventHandlers)
        {
            if (!s_eventHandlers.TryGetValue(name, out var list))
                s_eventHandlers[name] = list = [];
            list.Add(handler);
        }
        return new Unsubscriber(() =>
        {
            lock (s_eventHandlers)
                if (s_eventHandlers.TryGetValue(name, out var list))
                    list.Remove(handler);
        });
    }

    /// <summary>
    /// Synchronous call into a native service — a built-in one (<c>haptic</c>, <c>prefs.get</c>, …) or one
    /// your Swift registered with <c>registry.service("op")</c>. Returns the parsed JSON result, or null.
    /// </summary>
    public static JsonElement? CallNative(string operation, object? args = null)
    {
        var argsJson = JsonValueWriter.ToJson(args);
        Trace($"call {operation} {Truncate(argsJson)}");
        var json = Platform.Call(operation, argsJson);
        if (string.IsNullOrEmpty(json))
            return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    public static void Log(WatchLogLevel level, string message)
    {
        if (s_platform is not null)
            s_platform.Log(level, message);
        else
            Console.WriteLine($"[{level}] {message}");
    }

    // ---- called by the native host (WatchExports, or the live-mode server) ----------------------------

    /// <summary>A UI event from the SwiftUI renderer (node id + optional value), then an immediate render.</summary>
    internal static void DispatchEvent(string id, string? value)
    {
        Trace($"event {id} {Truncate(value)}");
        try { s_bridge?.Dispatch(id, value); }
        catch (Exception ex) { Log(WatchLogLevel.Error, ex.ToString()); }
        // Flush the render the handler just queued now, rather than a runloop turn later, so a controlled
        // control (TextField, Toggle, the crown) never shows a frame of stale state.
        Drain();
    }

    internal static void RaiseNativeEvent(string name, string? json)
    {
        Trace($"native event {name} {Truncate(json)}");
        Action<JsonElement>[] handlers;
        lock (s_eventHandlers)
        {
            if (!s_eventHandlers.TryGetValue(name, out var list) || list.Count == 0)
                return;
            handlers = [.. list];
        }

        JsonElement payload;
        using (var doc = JsonDocument.Parse(string.IsNullOrEmpty(json) ? "null" : json))
            payload = doc.RootElement.Clone();

        foreach (var h in handlers)
        {
            try { h(payload); }
            catch (Exception ex) { Log(WatchLogLevel.Error, ex.ToString()); }
        }
        Drain();
    }

    internal static void SetPhase(WatchAppPhase phase)
    {
        if (Phase == phase)
            return;
        Phase = phase;
        try { PhaseChanged?.Invoke(phase); }
        catch (Exception ex) { Log(WatchLogLevel.Error, ex.ToString()); }
        Drain();
    }

    /// <summary>Runs queued main-thread work — posted renders, <c>await</c> continuations, <see cref="RunOnMainThread"/>.</summary>
    internal static void Drain() =>
        s_syncContext?.Drain(ex => Log(WatchLogLevel.Error, ex.ToString()));

    /// <summary>Tears the host down (tests, and live mode between sessions).</summary>
    internal static void Reset()
    {
        s_platform = null;
        s_syncContext = null;
        s_bridge = null;
        Phase = WatchAppPhase.Active;
        PhaseChanged = null;
        lock (s_eventHandlers)
            s_eventHandlers.Clear();
        SynchronizationContext.SetSynchronizationContext(null);
    }

    internal static void Trace(string message)
    {
        if (TraceEnabled)
            Log(WatchLogLevel.Debug, "[trace] " + message);
    }

    static string Truncate(string? s) => s is null ? "null" : s.Length <= 160 ? s : s[..160] + "…";

    sealed class Unsubscriber(Action dispose) : IDisposable
    {
        int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                dispose();
        }
    }
}

/// <summary>The <see cref="IBridge"/> a watch app runs on: patches go to the Swift host, events come back in.</summary>
sealed class WatchBridge(IWatchPlatform platform) : IBridge
{
    Action<string, string?>? _handler;

    public void Render(string json)
    {
        WatchHost.Trace($"render {json.Length} bytes");
        platform.Render(json);
    }

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    public void Dispatch(string id, string? value) => _handler?.Invoke(id, value);
}
