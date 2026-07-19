namespace SwiftDotNet.Controls;

/// <summary>
/// A full-screen blocking loading overlay — ported from Shiny's <c>LoadingOverlay</c>. <see cref="Show"/>
/// presents a centered spinner + message over a dimmed F2 scrim and returns a handle; call
/// <see cref="Hide"/> with it when the work completes. Requires an <see cref="OverlayHost"/> root.
/// </summary>
public static class Loading
{
    /// <summary>Show the overlay; returns an id to pass to <see cref="Hide"/>.</summary>
    public static string Show(string message = "Loading…") =>
        Overlay.Present(
            new LoadingView(message),
            new OverlayOptions { Position = OverlayPosition.Center, DimBackground = true, TapOutsideToDismiss = false });

    /// <summary>Dismiss a previously shown overlay.</summary>
    public static void Hide(string id) => Overlay.Dismiss(id);
}

sealed class LoadingView : View
{
    readonly string _message;
    public LoadingView(string message) => _message = message;

    public override View Body =>
        new VStack(
                new ProgressView(),   // indeterminate native spinner
                new Text(_message).Font(Font.Body).ForegroundColor(ControlPalette.OnSurface))
            .Spacing(14)
            .Padding(24)
            .Background(ControlPalette.Surface)
            .CornerRadius(16)
            .Shadow(20, SwiftColor.Hex("#000000"), 0, 8);
}
