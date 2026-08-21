using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The validator is the substitute for the compiler errors these platforms decline to give us: every
/// constraint it checks fails silently on device. So each test here stands in for a bug that would
/// otherwise be found by a tester, not by a build.
/// </summary>
public class LiveValidatorTests
{
    static LiveTarget AppleWidget(WidgetFamily family) => new()
    {
        Surface = LiveSurface.Widget,
        Platform = LivePlatform.Apple,
        Family = family,
    };

    [Fact]
    public void RejectsAButtonOnAnAppleWidget()
    {
        var payload = LiveWire.Build(
            new LiveVStack(new LiveButton("Snooze", () => { })), LiveSurface.Widget);

        var found = LiveValidator.Validate(payload, AppleWidget(WidgetFamily.Small));

        // A widget's AppIntent runs inside the widget extension, which contains no .NET - so the handler
        // could never run. It compiles, ships, and does nothing.
        var error = Assert.Single(found, d => d.Code == "SDNL003");
        Assert.Equal(LiveSeverity.Error, error.Severity);
        Assert.Contains("LiveLink", error.Message);
    }

    [Fact]
    public void AllowsAButtonOnAnAppleLiveActivity()
    {
        var payload = LiveWire.Build(new LiveButton("Cancel", () => { }));

        // The same node is fine here, because a Live Activity uses LiveActivityIntent, which runs in the
        // app's process. The distinction is the entire interactive story on Apple.
        Assert.DoesNotContain(LiveValidator.Validate(payload, LiveTarget.AppleActivity), d => d.Code == "SDNL003");
    }

    [Fact]
    public void RequiresAnAccessibilityLabelOnABitmap()
    {
        var payload = LiveWire.Build(new LiveBitmap(new byte[] { 1, 2, 3 }, 100, 50));

        var error = Assert.Single(LiveValidator.Validate(payload, LiveTarget.AndroidNotification),
            d => d.Code == "SDNL004");
        Assert.Equal(LiveSeverity.Error, error.Severity);
    }

    [Fact]
    public void AcceptsALabelledBitmap()
    {
        var payload = LiveWire.Build(
            new LiveBitmap(new byte[] { 1, 2, 3 }, 100, 50).AccessibilityLabel("Route map"));

        Assert.DoesNotContain(LiveValidator.Validate(payload, LiveTarget.AndroidNotification),
            d => d.Code == "SDNL004");
    }

    [Fact]
    public void RejectsAnOversizedBitmap()
    {
        var payload = LiveWire.Build(
            new LiveBitmap(new byte[] { 1 }, 4000, 4000).AccessibilityLabel("huge"));

        Assert.Contains(LiveValidator.Validate(payload, LiveTarget.AndroidNotification),
            d => d.Code == "SDNL005" && d.Severity == LiveSeverity.Error);
    }

    [Fact]
    public void RejectsAStackInAnInlineAccessory()
    {
        var payload = LiveWire.Build(
            new LiveVStack(new LiveText("a"), new LiveText("b")), LiveSurface.Widget);

        // An inline accessory is one line of text beside the lock-screen clock, not a small widget.
        Assert.Contains(LiveValidator.Validate(payload, AppleWidget(WidgetFamily.AccessoryInline)),
            d => d.Code == "SDNL006" && d.Severity == LiveSeverity.Error);
    }

    [Fact]
    public void AcceptsTextInAnInlineAccessory()
    {
        var payload = LiveWire.Build(new LiveText("18 min"), LiveSurface.Widget);
        Assert.DoesNotContain(LiveValidator.Validate(payload, AppleWidget(WidgetFamily.AccessoryInline)),
            d => d.Code == "SDNL006");
    }

    [Fact]
    public void WarnsThatFrameIsANoOpBelowApi31()
    {
        var payload = LiveWire.Build(new LiveText("sized").Frame(width: 100));

        var target = LiveTarget.AndroidNotification with { AndroidMinSdk = 24 };
        var warning = Assert.Single(LiveValidator.Validate(payload, target), d => d.Code == "SDNL007");
        Assert.Equal(LiveSeverity.Warning, warning.Severity);
    }

    [Fact]
    public void DoesNotWarnAboutFrameAtApi31()
    {
        var payload = LiveWire.Build(new LiveText("sized").Frame(width: 100));
        Assert.DoesNotContain(LiveValidator.Validate(payload, LiveTarget.AndroidNotification),
            d => d.Code == "SDNL007");
    }

    [Fact]
    public void ReportsThatAGradientFlattensOnAndroid()
    {
        var payload = LiveWire.Build(
            new LiveText("x").Background(new LinearGradient(Color.Red, Color.Blue)));

        Assert.Contains(LiveValidator.Validate(payload, LiveTarget.AndroidNotification),
            d => d.Code == "SDNL010" && d.Severity == LiveSeverity.Info);
    }

    [Fact]
    public void AssertThrowsOnlyOnErrors()
    {
        var withWarning = LiveWire.Build(new LiveText("sized").Frame(width: 100));
        LiveValidator.Assert(withWarning, LiveTarget.AndroidNotification with { AndroidMinSdk = 24 });

        var withError = LiveWire.Build(new LiveBitmap(new byte[] { 1 }, 10, 10));
        Assert.Throws<InvalidOperationException>(
            () => LiveValidator.Assert(withError, LiveTarget.AndroidNotification));
    }

    [Fact]
    public void SortsErrorsAheadOfWarnings()
    {
        var payload = LiveWire.Build(
            new LiveVStack(
                new LiveText("x").Frame(width: 10),
                new LiveBitmap(new byte[] { 1 }, 10, 10)));

        var found = LiveValidator.Validate(payload, LiveTarget.AndroidNotification with { AndroidMinSdk = 24 });
        Assert.Equal(LiveSeverity.Error, found[0].Severity);
    }
}
