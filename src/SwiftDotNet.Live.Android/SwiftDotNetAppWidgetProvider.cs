using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace SwiftDotNet;

/// <summary>
/// Base <c>AppWidgetProvider</c> for a widget declared in C#.
///
/// <para>Non-generic on purpose. Android instantiates a provider itself, by class name from the manifest,
/// so it cannot be <c>SwiftDotNetAppWidgetProvider&lt;TState&gt;</c> - the system has no way to supply a
/// type argument. A subclass therefore owns its own <see cref="Widget{TState}"/> and forwards to it from
/// <see cref="BuildAsync"/>, which keeps the generic where it belongs (the app's code) and off the type
/// the platform has to construct.</para>
///
/// <para>This runs <b>in the app's own process</b>, which is the fact the whole Android design leans on:
/// unlike Apple, the timeline is computed here and now, in managed code, with access to the app's
/// services. There is no App Group, no extension, and no pre-rendering.</para>
///
/// <example>
/// <code>
/// [BroadcastReceiver(Label = "Weather", Exported = true)]
/// [IntentFilter(new[] { AppWidgetManager.ActionAppwidgetUpdate })]
/// [MetaData("android.appwidget.provider", Resource = "@xml/weather_widget")]
/// public class WeatherWidgetProvider : SwiftDotNetAppWidgetProvider
/// {
///     readonly WeatherWidget _widget = new();
///     protected override string Kind =&gt; _widget.Kind;
///     protected override Task&lt;WidgetPayload&gt; BuildAsync(WidgetContext ctx, LiveTarget target)
///         =&gt; _widget.BuildPayloadAsync(ctx, target);
/// }
/// </code>
/// </example>
/// </summary>
public abstract class SwiftDotNetAppWidgetProvider : AppWidgetProvider
{
    /// <summary>The surface id, matching the widget's <see cref="Widget{TState}.Kind"/>.</summary>
    protected abstract string Kind { get; }

    /// <summary>Builds the payload. Usually one line forwarding to the app's <see cref="Widget{TState}"/>.</summary>
    protected abstract Task<WidgetPayload> BuildAsync(WidgetContext context, LiveTarget target);

    /// <summary>Logical size used when rendering with <see cref="LiveRenderMode.Bitmap"/>.</summary>
    protected virtual LiveRenderMode RenderMode => LiveRenderMode.Native;

    /// <inheritdoc />
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null) return;

        // A BroadcastReceiver is torn down the moment OnReceive/OnUpdate returns, so any await after that
        // point runs against a dead process. GoAsync holds the receiver alive until Finish is called -
        // this is the single most common way an async widget provider silently does nothing.
        var pending = GoAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await UpdateAsync(context, appWidgetManager, appWidgetIds).ConfigureAwait(false);
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    /// <inheritdoc />
    public override void OnAppWidgetOptionsChanged(
        Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions)
    {
        // A resize changes the family, and the family changes the tree - so a resize is a re-render, not
        // a no-op. Forwarding to OnUpdate keeps one path.
        if (context is not null && appWidgetManager is not null)
            OnUpdate(context, appWidgetManager, new[] { appWidgetId });
    }

    async Task UpdateAsync(Context context, AppWidgetManager manager, int[] ids)
    {
        var placements = new List<SurfacePlacement>(ids.Length);
        foreach (var id in ids)
        {
            var options = manager.GetAppWidgetOptions(id);
            placements.Add(new SurfacePlacement(Kind, FamilyOf(options), id.ToString()));
        }

        var target = new LiveTarget
        {
            Surface = LiveSurface.Widget,
            Platform = LivePlatform.Android,
            AndroidMinSdk = (int)Build.VERSION.SdkInt,
        };

        var payload = await BuildAsync(new WidgetContext { Placements = placements }, target)
            .ConfigureAwait(false);
        payload.Assert();

        SwiftDotNetLive.Router.Register(Kind, payload.Actions);

        // Publish as well as render: a snapshot on disk is what lets a later cold-start broadcast render
        // without re-running the app's timeline code.
        if (SwiftDotNetLive.Channel is { } channel)
            await channel.PublishAsync(payload.Snapshot).ConfigureAwait(false);

        var now = LiveClock.Now;

        foreach (var placement in placements)
        {
            if (placement.Family is not { } family) continue;
            var json = payload.TreeFor(family, now);
            if (json is null || !LiveWireReader.TryParse(json, out var node) || node is null) continue;

            var views = RenderMode == LiveRenderMode.Bitmap
                ? LiveBitmapRenderer.RenderToRemoteViews(context, node, WidthOf(family), HeightOf(family))
                : new RemoteViewsInterpreter(context)
                {
                    Kind = Kind,
                    ClickIntentFor = nodeId => LiveActionReceiver.For(context, Kind, nodeId, null),
                }.Build(node);

            manager.UpdateAppWidget(int.Parse(placement.NativeId), views);
        }
    }

    static WidgetFamily FamilyOf(Bundle? options) => WidgetFamilies.FromOptions(
        options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0,
        options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0);

    // Nominal logical sizes for the bitmap route. Android reports a real range per instance, but a
    // bitmap has to be rendered at *some* size, and the family's nominal shape is the honest choice -
    // the ImageView scales it to whatever the launcher actually allocates.
    static float WidthOf(WidgetFamily family) => family switch
    {
        WidgetFamily.Small => 155f,
        WidgetFamily.Large => 329f,
        WidgetFamily.ExtraLarge => 634f,
        _ => 329f,
    };

    static float HeightOf(WidgetFamily family) => family switch
    {
        WidgetFamily.Small => 155f,
        WidgetFamily.Large => 345f,
        WidgetFamily.ExtraLarge => 345f,
        _ => 155f,
    };
}
