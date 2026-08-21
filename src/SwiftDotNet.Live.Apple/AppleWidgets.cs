namespace SwiftDotNet;

/// <summary>
/// Publishes a <see cref="Widget{TState}"/> to the App Group so the Swift <c>SDNTimelineProvider</c> can
/// read it.
///
/// <para><b>This is where the Apple inversion actually happens.</b> On Android a widget provider computes
/// its timeline in-process on demand; here the widget extension has no .NET, so the app renders every
/// entry for every placed family up front and writes the lot into shared storage. The provider on the far
/// side is a dumb reader.</para>
///
/// <para>Which means a widget shows only what the app has already computed. Keeping it fresh requires the
/// <i>app</i> to run periodically - a <c>BGAppRefreshTask</c>, a push, a user launch - and that is
/// deliberately not this library's job. Publish a timeline with several hours of entries so a suspended
/// app still shows something plausible; <see cref="Widget{TState}.BuildPayloadAsync"/> warns when one has
/// a single entry and no refresh point.</para>
/// </summary>
public static class AppleWidgets
{
    /// <summary>
    /// Renders and publishes a widget's timeline, then asks WidgetKit to reload it.
    /// </summary>
    /// <returns>The payload, so a caller can inspect its diagnostics.</returns>
    public static async Task<WidgetPayload> PublishAsync<TState>(
        Widget<TState> widget, CancellationToken ct = default)
    {
        var channel = SwiftDotNetLive.Channel
            ?? throw new InvalidOperationException(
                "SwiftDotNetLive.Init(appGroup) has not been called. Call it from FinishedLaunching.");

        // Ask what the user actually placed. Most apps have nothing placed, in which case there is no
        // fan-out to pay for at all.
        var placements = await channel.GetPlacementsAsync(ct).ConfigureAwait(false);
        var mine = placements.Where(p => p.Kind == widget.Kind).ToList();

        var context = new WidgetContext
        {
            Placements = mine,
            CancellationToken = ct,
        };

        var payload = await widget.BuildPayloadAsync(context, Target).ConfigureAwait(false);
        payload.Assert();

        SwiftDotNetLive.Router.Register(widget.Kind, payload.Actions);
        await channel.PublishAsync(payload.Snapshot, ct).ConfigureAwait(false);

        return payload;
    }

    static LiveTarget Target { get; } = new()
    {
        Surface = LiveSurface.Widget,
        Platform = LivePlatform.Apple,
    };
}
