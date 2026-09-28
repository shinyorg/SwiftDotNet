namespace SwiftDotNet.Watch;

/// <summary>Severity of a message logged through the watch host (maps to <c>os_log</c> levels).</summary>
public enum WatchLogLevel { Debug, Info, Warning, Error }

/// <summary>
/// What the managed side needs from the native watch host. The in-process implementation calls the
/// function pointers the Swift app registered (<see cref="WatchExports"/>); live mode implements it over a
/// socket; tests implement it in memory.
/// </summary>
public interface IWatchPlatform
{
    /// <summary>Hand a serialized <see cref="TreeDiffer"/> patch to the SwiftUI renderer.</summary>
    void Render(string patchJson);

    /// <summary>Ask the host to call <c>swiftdotnet_watch_drain</c> on the main thread, soon.</summary>
    void RequestMainThreadDrain();

    void Log(WatchLogLevel level, string message);

    /// <summary>Synchronous call into a native service; returns a JSON result, or null.</summary>
    string? Call(string operation, string argsJson);
}
