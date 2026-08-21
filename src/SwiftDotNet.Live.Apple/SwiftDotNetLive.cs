using System.Runtime.InteropServices;

namespace SwiftDotNet;

/// <summary>
/// The iOS entry point for live surfaces. One call in <c>FinishedLaunching</c> wires the App Group
/// channel, the action router and the Swift shim.
///
/// Static for the same reason the Android facade is: the inbound half has no instance to hang off. A tap
/// arrives from the Swift shim as a C callback, potentially into a process the system launched purely to
/// run that intent, with no reference to anything the app built.
/// </summary>
public static unsafe class SwiftDotNetLive
{
    /// <summary>Dispatches inbound surface taps. Always present, even before <see cref="Init"/>.</summary>
    public static LiveActionRouter Router { get; } = new();

    /// <summary>The App Group store, once <see cref="Init"/> has run.</summary>
    public static ISurfaceChannel? Channel { get; private set; }

    /// <summary>The Live Activity driver, once <see cref="Init"/> has run.</summary>
    public static ILiveActivityDriver? Activities { get; private set; }

    /// <summary>
    /// Raised when ActivityKit issues or rotates an activity's APNs push token, as
    /// <c>(kind, hex token)</c>.
    ///
    /// This library does not send pushes - that is the app's push stack's job - but it is the only place
    /// the token is obtainable, and an activity cannot be updated from a server without it.
    /// </summary>
    public static event Action<string, string>? PushTokenReceived;

    /// <summary>
    /// Wires everything up.
    /// </summary>
    /// <param name="appGroup">
    /// The App Group id, entitled on <b>both</b> the app and the widget extension. Getting this wrong is
    /// the single most common way widget support silently does nothing.
    /// </param>
    public static void Init(string appGroup)
    {
        AppleLiveBridge.Configure(appGroup);
        Channel = new AppleSurfaceChannel(appGroup);
        Activities = new AppleLiveActivities();

        AppleLiveBridge.SetActionCallback(
            (IntPtr)(delegate* unmanaged<byte*, byte*, void>)&OnAction);
        AppleLiveBridge.SetPushTokenCallback(
            (IntPtr)(delegate* unmanaged<byte*, byte*, void>)&OnPushToken);
    }

    /// <summary>
    /// Drains taps queued while the app was suspended and dispatches them. Call on foreground.
    ///
    /// On Apple this is not an edge case: an intent can run in a process the system started specifically
    /// for it, before the app has registered a single handler, so the mailbox is the normal path rather
    /// than the fallback.
    /// </summary>
    public static Task<IReadOnlyList<SurfaceAction>> DrainPendingAsync(CancellationToken ct = default)
        => Channel is null
            ? Task.FromResult<IReadOnlyList<SurfaceAction>>(Array.Empty<SurfaceAction>())
            : Router.DrainAsync(Channel, ct);

    /// <summary>Asks WidgetKit to refresh a kind's timelines, or all of them when <c>null</c>.</summary>
    public static void ReloadWidgets(string? kind = null) => AppleLiveBridge.ReloadWidgets(kind);

    [UnmanagedCallersOnly]
    static void OnAction(byte* kindPtr, byte* nodePtr)
    {
        var kind = Marshal.PtrToStringUTF8((IntPtr)kindPtr);
        var node = Marshal.PtrToStringUTF8((IntPtr)nodePtr);
        if (kind is null || node is null) return;

        // The Swift side has already written this to the mailbox, so a miss here is not a lost tap - it
        // is a tap that will be picked up by the next DrainPendingAsync.
        Router.Dispatch(new SurfaceAction(kind, node, null, LiveClock.Now));
    }

    [UnmanagedCallersOnly]
    static void OnPushToken(byte* kindPtr, byte* tokenPtr)
    {
        var kind = Marshal.PtrToStringUTF8((IntPtr)kindPtr);
        var token = Marshal.PtrToStringUTF8((IntPtr)tokenPtr);
        if (kind is not null && token is not null) PushTokenReceived?.Invoke(kind, token);
    }
}
