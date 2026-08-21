using Android.App;
using Android.Content;

namespace SwiftDotNet;

/// <summary>
/// Receives taps from notifications and app widgets and routes them back into managed handlers.
///
/// This is the whole of the inbound half on Android, and it is short because the platform is generous
/// here: a <c>PendingIntent</c> fired from a notification or a widget is delivered to a
/// <see cref="BroadcastReceiver"/> in our own process, started for us if it is not running. There is no
/// App Group, no extension boundary, and no waiting for the app to be foregrounded. The contrast with
/// Apple, where a widget's <c>AppIntent</c> runs inside an extension that contains no .NET at all, is the
/// single biggest structural difference between the two platforms.
///
/// Register it in the app manifest by subclassing it.
/// </summary>
[BroadcastReceiver(Exported = false)]
public class LiveActionReceiver : BroadcastReceiver
{
    /// <summary>The intent action every surface tap is delivered under.</summary>
    public const string ActionTap = "com.swiftdotnet.live.TAP";

    internal const string ExtraKind = "sdn_kind";
    internal const string ExtraNode = "sdn_node";
    internal const string ExtraValue = "sdn_value";

    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (intent?.Action != ActionTap) return;

        var kind = intent.GetStringExtra(ExtraKind) ?? "";
        var node = intent.GetStringExtra(ExtraNode) ?? "";
        var value = intent.GetStringExtra(ExtraValue);

        var action = new SurfaceAction(kind, node, value, LiveClock.Now);

        // Dispatch if the handler is still registered; otherwise queue it. The queue matters more than it
        // looks: a surface outlives the process that published it, so a tap can arrive against a tree
        // published by a previous launch whose handlers no longer exist. That is not an error.
        if (!SwiftDotNetLive.Router.Dispatch(action))
            _ = SwiftDotNetLive.Channel?.PostActionAsync(action);
    }

    /// <summary>Builds the <see cref="PendingIntent"/> for one node of one surface.</summary>
    internal static PendingIntent? For(Context context, string kind, string nodeId, string? value)
    {
        var intent = new Intent(ActionTap).SetPackage(context.PackageName);
        intent.PutExtra(ExtraKind, kind);
        intent.PutExtra(ExtraNode, nodeId);
        if (value is not null) intent.PutExtra(ExtraValue, value);

        // The request code must differ per node, or PendingIntent.getBroadcast hands back the same
        // instance for every button in the tree (extras are not part of the equality check) and every tap
        // would fire the first node's action. A stable per-node hash is the fix.
        var requestCode = unchecked((kind + " " + nodeId).GetHashCode());

        return PendingIntent.GetBroadcast(context, requestCode, intent,
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }
}
