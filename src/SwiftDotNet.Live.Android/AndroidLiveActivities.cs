using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace SwiftDotNet;

/// <summary>
/// Runs <see cref="LiveActivity{TState}"/> on Android as an ongoing, custom-content notification.
///
/// <para><b>The shape does not match Apple's and the mapping is lossy on purpose.</b> Android has no
/// Dynamic Island, so of the eight slots only three have anywhere to go: the collapsed notification takes
/// <see cref="LiveSlot.CompactLeading"/> and <see cref="LiveSlot.CompactTrailing"/> side by side, and the
/// expanded notification takes <see cref="LiveSlot.LockScreen"/>. The four expanded-island regions and
/// the minimal presentation have no analog and are ignored. That is declared behaviour, not a gap.</para>
///
/// <para><b>Custom means custom content, not a custom notification.</b> Since Android 12 a custom view is
/// always wrapped in the standard template with the app header, via
/// <c>DecoratedCustomViewStyle</c>, and the space available is roughly 48 dp collapsed and 256 dp
/// expanded. Anything designed as a full-bleed lock-screen card will be cropped.</para>
/// </summary>
public sealed class AndroidLiveActivities : ILiveActivityDriver
{
    readonly Context _context;
    readonly Dictionary<string, int> _live = new();

    public AndroidLiveActivities(Context context) => _context = context;

    /// <inheritdoc />
    public Task<string> StartAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default)
    {
        var id = Post(activity, state);
        return Task.FromResult(id);
    }

    /// <inheritdoc />
    public Task UpdateAsync<TState>(LiveActivity<TState> activity, TState state, CancellationToken ct = default)
    {
        // Re-posting under the same notification id is an update, not a second notification. The channel's
        // low importance is what keeps this from buzzing on every content change.
        Post(activity, state);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task EndAsync<TState>(LiveActivity<TState> activity, TState? finalState = default, CancellationToken ct = default)
    {
        if (finalState is not null)
        {
            // A final state renders the dismissal presentation, so it is posted once more as a
            // non-ongoing notification the user can swipe away.
            Post(activity, finalState, ongoing: false);
        }
        else if (_live.Remove(activity.Kind, out var id))
        {
            Manager?.Cancel(id);
        }

        SwiftDotNetLive.Router.Forget(activity.Kind);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ActiveAsync(string kind, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(
            _live.TryGetValue(kind, out var id) ? new[] { id.ToString() } : Array.Empty<string>());

    string Post<TState>(LiveActivity<TState> activity, TState state, bool ongoing = true)
    {
        var target = new LiveTarget
        {
            Surface = LiveSurface.Activity,
            Platform = LivePlatform.Android,
            AndroidMinSdk = (int)Build.VERSION.SdkInt,
        };

        var payload = activity.BuildPayload(state, target, LiveClock.Now);
        payload.Assert();

        SwiftDotNetLive.Router.Register(activity.Kind, payload.Actions);

        var interpreter = new RemoteViewsInterpreter(_context)
        {
            Kind = activity.Kind,
            ClickIntentFor = nodeId => LiveActionReceiver.For(_context, activity.Kind, nodeId, null),
        };

        var collapsed = BuildCollapsed(payload, interpreter);
        var expanded = BuildTree(payload, LiveSlot.LockScreen, interpreter);

        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(_context, SwiftDotNetLive.ActivityChannelId)
            : new Notification.Builder(_context);

        builder.SetSmallIcon(SwiftDotNetLive.SmallIcon)
               .SetOngoing(ongoing)
               .SetOnlyAlertOnce(true)
               .SetStyle(new Notification.DecoratedCustomViewStyle());

        if (collapsed is not null) builder.SetCustomContentView(collapsed);
        if (expanded is not null) builder.SetCustomBigContentView(expanded);

        var id = IdFor(activity.Kind);
        Manager?.Notify(id, builder.Build());
        return id.ToString();
    }

    /// <summary>
    /// The collapsed row: leading and trailing side by side, mirroring the Dynamic Island's compact
    /// presentation. Built here rather than in the vocabulary because it is a *composition* of two slots
    /// that only Android needs.
    /// </summary>
    RemoteViews? BuildCollapsed(LiveActivityPayload payload, RemoteViewsInterpreter interpreter)
    {
        var leading = BuildTree(payload, LiveSlot.CompactLeading, interpreter);
        var trailing = BuildTree(payload, LiveSlot.CompactTrailing, interpreter);

        if (leading is null && trailing is null) return null;
        if (trailing is null) return leading;
        if (leading is null) return trailing;

        var row = new RemoteViews(_context.PackageName, Resource.Layout.sdn_hstack);
        row.AddView(Resource.Id.sdn_root, leading);
        row.AddView(Resource.Id.sdn_root, trailing);
        return row;
    }

    RemoteViews? BuildTree(LiveActivityPayload payload, string slot, RemoteViewsInterpreter interpreter)
    {
        if (!payload.Snapshot.Trees.TryGetValue(slot, out var json)) return null;
        return LiveWireReader.TryParse(json, out var node) && node is not null
            ? interpreter.Build(node)
            : null;
    }

    /// <summary>
    /// Posts an Android 16 <b>Live Update</b>: an ongoing, progress-centric notification the system
    /// promotes to a status-bar chip and gives lock-screen prominence.
    ///
    /// <para>This takes a <see cref="LiveUpdate"/> rather than a tree because a Live Update is
    /// <i>templated</i> - segments, points, a tracker icon - with no view hierarchy to supply. See
    /// <see cref="LiveUpdate"/> for why that is modelled as data.</para>
    ///
    /// <para><b>Honest limitation:</b> the promotion itself, <c>Notification.Builder.requestPromotedOngoing</c>,
    /// is not present in this Android SDK binding (36.1.69), so it is invoked through JNI when running on
    /// API 36+ and skipped otherwise. Without it the notification is still correct - ongoing, with real
    /// progress - it simply does not get the status-bar chip. Replace the JNI call with the binding when
    /// one ships.</para>
    /// </summary>
    public void PostLiveUpdate(string kind, LiveUpdate update)
    {
        var builder = Build.VERSION.SdkInt >= BuildVersionCodes.O
            ? new Notification.Builder(_context, SwiftDotNetLive.ActivityChannelId)
            : new Notification.Builder(_context);

        builder.SetSmallIcon(SwiftDotNetLive.SmallIcon)
               .SetOngoing(true)
               .SetOnlyAlertOnce(true)
               .SetContentTitle(update.Title)
               .SetProgress(100, (int)(Math.Clamp(update.Progress, 0, 1) * 100), update.Indeterminate);

        if (!string.IsNullOrEmpty(update.Text)) builder.SetContentText(update.Text);

        TryPromote(builder);

        Manager?.Notify(IdFor(kind), builder.Build());
    }

    static void TryPromote(Notification.Builder builder)
    {
        if (Build.VERSION.SdkInt < (BuildVersionCodes)36) return;

        try
        {
            // The binding for requestPromotedOngoing(boolean) does not exist yet, so this reaches the
            // method through JNI. Wrapped because a missing method must degrade to "no chip", never crash
            // an app that is merely showing progress.
            var handle = Android.Runtime.JNIEnv.GetMethodID(
                Android.Runtime.JNIEnv.GetObjectClass(builder.Handle),
                "requestPromotedOngoing", "(Z)Landroid/app/Notification$Builder;");
            if (handle != IntPtr.Zero)
                Android.Runtime.JNIEnv.CallObjectMethod(builder.Handle, handle, new Android.Runtime.JValue(true));
        }
        catch (Java.Lang.Throwable)
        {
            // Not promotable on this device. The notification still posts.
        }
    }

    NotificationManager? Manager =>
        (NotificationManager?)_context.GetSystemService(Android.Content.Context.NotificationService);

    /// <summary>A stable notification id per kind, so a re-post updates rather than stacking.</summary>
    int IdFor(string kind)
    {
        if (_live.TryGetValue(kind, out var id)) return id;
        id = unchecked(kind.GetHashCode() & 0x7FFFFFFF);
        _live[kind] = id;
        return id;
    }
}
