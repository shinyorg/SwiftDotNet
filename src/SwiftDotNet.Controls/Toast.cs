namespace SwiftDotNet.Controls;

/// <summary>
/// Code-invoked toast notifications — ported from Shiny's <c>Toast</c>/<c>Toaster</c>. Presents a small
/// auto-dismissing card at the bottom of the screen over the F2 <see cref="Overlay"/> layer (no scrim,
/// non-blocking). Requires the app root to be wrapped in an <see cref="OverlayHost"/>.
/// </summary>
public static class Toast
{
    /// <summary>Show <paramref name="message"/> for <paramref name="seconds"/>, tinted by <paramref name="style"/>.</summary>
    public static void Show(string message, double seconds = 2.5, PillType style = PillType.None)
    {
        var id = Overlay.Present(
            new ToastView(message, style),
            new OverlayOptions { DimBackground = false, TapOutsideToDismiss = false, Position = OverlayPosition.Bottom });
        _ = DismissAfter(id, seconds);
    }

    static async Task DismissAfter(string id, double seconds)
    {
        // Overlay.Dismiss → RequestRender marshals onto the captured UI context, so this is safe off-thread.
        await Task.Delay(TimeSpan.FromSeconds(seconds)).ConfigureAwait(false);
        Overlay.Dismiss(id);
    }
}

/// <summary>The visual for a <see cref="Toast"/> — a rounded, shadowed card lifted off the bottom edge.</summary>
sealed class ToastView : View
{
    readonly string _message;
    readonly PillType _style;

    public ToastView(string message, PillType style) { _message = message; _style = style; }

    public override View Body
    {
        get
        {
            var (bg, fg, _) = _style == PillType.None
                ? (SwiftColor.Hex("#323232"), SwiftColor.Hex("#FFFFFF"), SwiftColor.Hex("#000000"))
                : ControlPalette.Pill(_style);
            var card = new Text(_message)
                .Font(Font.Body)
                .ForegroundColor(fg)
                .Padding(horizontal: 16, vertical: 12)
                .Background(bg)
                .CornerRadius(12)
                .Shadow(8, SwiftColor.Hex("#000000"), 0, 4);
            // Lift off the bottom edge with an offset (a second Padding would overwrite the card's own).
            return card.Offset(0, -40);
        }
    }
}
