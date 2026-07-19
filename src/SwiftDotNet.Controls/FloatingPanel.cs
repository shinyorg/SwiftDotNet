namespace SwiftDotNet.Controls;

/// <summary>
/// A bottom sheet with a drag handle — ported (in reduced form) from Shiny's <c>FloatingPanel</c>.
/// Presented over the F2 <see cref="Overlay"/> layer; a downward drag (F1) past a threshold dismisses it.
/// Full multi-detent resizing awaits proportional geometry (Plan-1 F11) — this v1 is show/dismiss.
/// Requires an <see cref="OverlayHost"/> root.
/// </summary>
public static class FloatingPanel
{
    /// <summary>Present <paramref name="content"/> as a bottom sheet; returns an id for <see cref="Dismiss"/>.</summary>
    public static string Present(View content, bool dimBackground = true)
    {
        string id = "";
        var panel = new FloatingPanelView(content, () => Dismiss(id));
        id = Overlay.Present(panel, new OverlayOptions
        {
            Position = OverlayPosition.Bottom,
            DimBackground = dimBackground,
            TapOutsideToDismiss = true,
        });
        return id;
    }

    public static void Dismiss(string id) => Overlay.Dismiss(id);
}

sealed class FloatingPanelView : View
{
    readonly View _content;
    readonly Action _onDismiss;

    public FloatingPanelView(View content, Action onDismiss)
    {
        _content = content;
        _onDismiss = onDismiss;
    }

    public override View Body
    {
        get
        {
            // A grab handle above the content; dragging the sheet down past ~80pt dismisses it.
            var handle = new RoundedRectangle(3).Frame(40, 5).ForegroundColor(ControlPalette.Outline);
            return new VStack(handle, _content)
                .Spacing(12)
                .Padding(16)
                .Background(ControlPalette.Surface)
                .CornerRadius(20)
                .Shadow(24, SwiftColor.Hex("#000000"), 0, -4)
                .OnDrag(info =>
                {
                    if (info.Phase == GesturePhase.Ended && info.TranslationY > 80)
                        _onDismiss();
                });
        }
    }
}
