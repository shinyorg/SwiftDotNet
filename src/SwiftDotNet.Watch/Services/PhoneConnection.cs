using System.Text.Json;

namespace SwiftDotNet.Watch;

/// <summary>
/// Talks to the companion iPhone app over WatchConnectivity (<c>WCSession</c>). Messages are property-list
/// dictionaries; <see cref="MessageReceived"/> hands them over as JSON. Handlers run on the main thread, so
/// they can assign <see cref="State{T}"/> directly.
/// </summary>
/// <remarks>
/// Untested against a paired phone (the simulator pairing flow was not exercised) — see
/// docs/backends/watchos.md. Shiny.Wearables' typed message router is not ported; route on a key yourself.
/// </remarks>
public static class PhoneConnection
{
    static int s_initializedGeneration = -1;

    /// <summary>Raised on the main thread for every message, application context or user info the phone sends.</summary>
    public static event Action<JsonElement>? MessageReceived;

    /// <summary>Raised when reachability or activation changes.</summary>
    public static event Action? StateChanged;

    /// <summary>Activates the session. Safe to call repeatedly; every send calls it for you.</summary>
    public static void Activate()
    {
        var current = WatchHost.Generation;
        if (Interlocked.Exchange(ref s_initializedGeneration, current) == current)
            return;
        WatchHost.OnNativeEvent("phone.message", e => MessageReceived?.Invoke(e));
        WatchHost.OnNativeEvent("phone.state", _ => StateChanged?.Invoke());
        WatchHost.CallNative("phone.activate");
    }

    public static bool IsSupported => State("supported");

    /// <summary>The phone app is running and reachable for live messages (<see cref="SendMessage"/>).</summary>
    public static bool IsReachable => State("reachable");

    /// <summary>The companion iPhone app is installed.</summary>
    public static bool IsCompanionAppInstalled => State("companionInstalled");

    /// <summary>Sends a live message. Delivered only while the phone app is reachable; otherwise dropped (logged).</summary>
    public static void SendMessage(IReadOnlyDictionary<string, object?> message)
    {
        Activate();
        WatchHost.CallNative("phone.send", message);
    }

    /// <summary>Replaces the state the phone sees next — latest value wins, delivered even if the phone app isn't running.</summary>
    public static void UpdateApplicationContext(IReadOnlyDictionary<string, object?> context)
    {
        Activate();
        WatchHost.CallNative("phone.context", context);
    }

    /// <summary>Queues a message for guaranteed, in-order delivery whenever the phone app next runs.</summary>
    public static void TransferUserInfo(IReadOnlyDictionary<string, object?> userInfo)
    {
        Activate();
        WatchHost.CallNative("phone.transfer", userInfo);
    }

    static bool State(string key) =>
        WatchHost.CallNative("phone.state") is { ValueKind: JsonValueKind.Object } s
        && s.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
}
