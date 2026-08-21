using Android.App;
using Android.Content;
using Android.OS;

namespace SwiftDotNet;

/// <summary>
/// The Android entry point for live surfaces: one call in <c>Application.OnCreate</c> wires the channel,
/// the action router and the notification channel, and everything else resolves from here.
///
/// It is deliberately static, which is unusual for this repo. The reason is that the inbound half has no
/// instance to hang off: a <see cref="LiveActionReceiver"/> is constructed by the *system*, in a process
/// the system may have started specifically to deliver that broadcast, with no reference to anything the
/// app built. It has to find the router through a static, or not at all.
/// </summary>
public static class SwiftDotNetLive
{
    /// <summary>The default notification channel id used for Live Activities.</summary>
    public const string ActivityChannelId = "swiftdotnet.live";

    /// <summary>Dispatches inbound surface taps. Always present, even before <see cref="Init"/>.</summary>
    public static LiveActionRouter Router { get; } = new();

    /// <summary>The shared store, once <see cref="Init"/> has run.</summary>
    public static ISurfaceChannel? Channel { get; private set; }

    /// <summary>The application context, once <see cref="Init"/> has run.</summary>
    public static Context? Context { get; private set; }

    /// <summary>The drawable used as every live notification's small icon. Required by the platform.</summary>
    public static int SmallIcon { get; set; }

    /// <summary>The Live Activity driver, once <see cref="Init"/> has run.</summary>
    public static ILiveActivityDriver? Activities { get; private set; }

    /// <summary>
    /// Wires everything up. Call from <c>Application.OnCreate</c> so the receiver can find the router even
    /// when the system starts the process cold to deliver a tap.
    /// </summary>
    /// <param name="context">Any context; the application context is retained.</param>
    /// <param name="smallIcon">The drawable every live notification uses as its small icon.</param>
    /// <param name="channelName">User-visible name of the notification channel Live Activities post to.</param>
    public static void Init(Context context, int smallIcon, string channelName = "Live Activities")
    {
        var app = context.ApplicationContext ?? context;
        Context = app;
        SmallIcon = smallIcon;
        Channel = new AndroidSurfaceChannel(app);
        Activities = new AndroidLiveActivities(app);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var manager = (NotificationManager?)app.GetSystemService(Android.Content.Context.NotificationService);
            // IMPORTANCE_LOW: a Live Activity is ambient status, not an interruption. Posting it at
            // default importance would buzz the device on every content update, which is exactly what a
            // frequently-updating surface must never do.
            var channel = new NotificationChannel(ActivityChannelId, channelName, NotificationImportance.Low);
            channel.SetShowBadge(false);
            manager?.CreateNotificationChannel(channel);
        }
    }

    /// <summary>
    /// Drains any taps that were queued while no handler was registered and dispatches them.
    /// Call on app foreground.
    /// </summary>
    public static Task<IReadOnlyList<SurfaceAction>> DrainPendingAsync(CancellationToken ct = default)
        => Channel is null
            ? Task.FromResult<IReadOnlyList<SurfaceAction>>(Array.Empty<SurfaceAction>())
            : Router.DrainAsync(Channel, ct);

    internal static Context RequireContext() => Context
        ?? throw new InvalidOperationException(
            "SwiftDotNetLive.Init(context, smallIcon) has not been called. Call it from Application.OnCreate.");
}
