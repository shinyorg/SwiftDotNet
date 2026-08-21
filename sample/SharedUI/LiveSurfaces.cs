using SwiftDotNet;

namespace SwiftDotNet.Sample;

/// <summary>The content state of the sample delivery activity. Kept tiny: it rides inside a 4 KB payload.</summary>
public sealed record DeliveryState(string Courier, DateTimeOffset Eta, double Fraction, bool Delivered);

/// <summary>
/// The sample Live Activity, mirroring what a delivery app actually shows.
///
/// Worth reading for three things that are not obvious from the API shape:
///
/// <list type="number">
///   <item><description><b><see cref="LiveTimer"/> instead of a formatted string.</b> The ETA counts down
///   by itself on both platforms, so the activity needs <i>no updates at all</i> between real progress
///   events. Publishing a new tree every second would burn the OS update budget and change nothing a
///   user could not already see.</description></item>
///   <item><description><b>Each slot is its own composition.</b> The minimal presentation is one glyph and
///   the compact trailing is one countdown -- they are not the lock-screen tree scaled down, and
///   pretending otherwise produces a Dynamic Island that is illegible.</description></item>
///   <item><description><b>The button is only legal here.</b> The same <see cref="LiveButton"/> in a
///   widget is rejected by the validator (SDNL003), because a widget's intent runs in the extension where
///   there is no .NET. Live Activities escape that through <c>LiveActivityIntent</c>.</description></item>
/// </list>
/// </summary>
public sealed class DeliveryActivity : LiveActivity<DeliveryState>
{
    /// <summary>Set when the user taps Cancel, from whichever process delivered the tap.</summary>
    public bool CancelRequested { get; private set; }

    public override string Kind => "delivery";

    public override LiveView LockScreen(DeliveryState s) =>
        new LiveVStack(
                new LiveHStack(
                        new LiveImage(s.Delivered ? "checkmark.circle.fill" : "shippingbox.fill")
                            .ForegroundColor(s.Delivered ? Color.Green : Color.Accent),
                        new LiveText(s.Courier).Font(Font.Headline),
                        new LiveSpacer(),
                        Eta(s))
                    .Spacing(8),
                new LiveProgress(s.Fraction).Tint(s.Delivered ? Color.Green : Color.Accent))
            .Spacing(10)
            .Padding(14)
            .OnTapUrl("swiftdotnet://delivery");

    public override LiveView? CompactLeading(DeliveryState s) =>
        new LiveImage("shippingbox.fill").ForegroundColor(Color.Accent);

    public override LiveView? CompactTrailing(DeliveryState s) => Eta(s);

    public override LiveView? Minimal(DeliveryState s) => new LiveImage("shippingbox.fill");

    public override LiveExpanded? Expanded(DeliveryState s) =>
        new LiveExpanded()
            .Leading(new LiveImage("shippingbox.fill").ForegroundColor(Color.Accent))
            .Trailing(Eta(s))
            .Center(new LiveText(s.Courier).Font(Font.Headline))
            .Bottom(s.Delivered
                ? new LiveText("Delivered").ForegroundColor(Color.Green)
                : new LiveButton("Cancel", () => CancelRequested = true));

    /// <summary>Once delivered there is nothing to count down to, so the timer becomes a label.</summary>
    static LiveView Eta(DeliveryState s) => s.Delivered
        ? new LiveText("Done").Font(Font.Caption)
        : new LiveTimer(s.Eta).Font(Font.Caption).ForegroundColor(Color.Secondary);
}

/// <summary>
/// The sample widget.
///
/// The interesting part is <see cref="TimelineAsync"/>, not <see cref="Body"/>. On Apple this runs in the
/// <b>app</b> -- never in the widget extension, which contains no .NET -- and everything it returns is
/// pre-rendered into the App Group for a Swift provider to read back. Which means the widget can only show
/// what the app has already computed, so the timeline deliberately runs several hours ahead: a suspended
/// app still has something plausible on screen.
///
/// Note there is no <see cref="LiveButton"/> anywhere below. A widget's only interaction on Apple is a
/// deep link, and the validator enforces that rather than letting the button ship inert.
/// </summary>
public sealed class ForecastWidget : Widget<ForecastWidget.Reading>
{
    /// <summary>One hour of the sample forecast.</summary>
    public readonly record struct Reading(string Place, int Degrees, string Symbol);

    public override string Kind => "forecast";

    public override IReadOnlyList<WidgetFamily> Families { get; } = new[]
    {
        WidgetFamily.Small,
        WidgetFamily.Medium,
        WidgetFamily.AccessoryInline,
    };

    public override LiveView Body(Reading r, WidgetFamily family) => family switch
    {
        // One line of text beside the lock-screen clock. Anything else here is an SDNL006 error, because
        // an inline accessory is a different medium rather than a smaller widget.
        WidgetFamily.AccessoryInline => new LiveText($"{r.Degrees}° {r.Place}"),

        WidgetFamily.Small => new LiveVStack(
                    new LiveImage(r.Symbol).ForegroundColor(Color.Accent),
                    new LiveText($"{r.Degrees}°").Font(Font.LargeTitle),
                    new LiveText(r.Place).Font(Font.Caption).ForegroundColor(Color.Secondary))
                .Spacing(4)
                .Padding(12),

        _ => new LiveHStack(
                    new LiveImage(r.Symbol).ForegroundColor(Color.Accent),
                    new LiveVStack(
                            new LiveText(r.Place).Font(Font.Headline),
                            new LiveText($"{r.Degrees}°").Font(Font.Title))
                        .Alignment(LiveAlignment.Leading)
                        .Spacing(2),
                    new LiveSpacer())
                .Spacing(12)
                .Padding(14),
    };

    public override Task<WidgetTimeline<Reading>> TimelineAsync(WidgetContext context)
    {
        var now = DateTimeOffset.UtcNow;
        var timeline = new WidgetTimeline<Reading>();

        // Six hours of entries, then ask to be refreshed. The tail is the safety margin: a widget does not
        // refresh itself, and if the app has not run since this was published, the last entry is what stays
        // on screen. A single entry with no RefreshAfter would earn an SDNL021 for exactly that reason.
        for (var hour = 0; hour < 6; hour++)
        {
            timeline.Entry(
                now.AddHours(hour),
                new Reading("Ottawa", 18 - hour, hour < 3 ? "sun.max.fill" : "cloud.fill"));
        }

        return Task.FromResult(timeline.RefreshAfter(now.AddHours(5)));
    }
}
