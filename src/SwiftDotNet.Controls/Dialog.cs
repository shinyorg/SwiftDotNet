namespace SwiftDotNet.Controls;

/// <summary>One button in a <see cref="Dialog"/>: a label, an optional emphasized/destructive style, and its action.</summary>
public sealed record DialogButton(string Label, Action Action, bool Emphasized = false, bool Destructive = false);

/// <summary>
/// In-app modal dialogs — alert / confirm / prompt — ported from Shiny's <c>DialogService</c>. Presents a
/// centered card over a dimmed F2 <see cref="Overlay"/> scrim. Requires an <see cref="OverlayHost"/> root.
/// </summary>
public static class Dialog
{
    /// <summary>A single-button acknowledgement.</summary>
    public static void Alert(string title, string message, string ok = "OK", Action? onOk = null)
    {
        string id = "";
        var view = new DialogView(title, message, new[]
        {
            new DialogButton(ok, () => { Overlay.Dismiss(id); onOk?.Invoke(); }, Emphasized: true),
        });
        id = Present(view);
    }

    /// <summary>A two-button confirmation; <paramref name="onResult"/> gets true for confirm, false for cancel.</summary>
    public static void Confirm(string title, string message, Action<bool> onResult,
        string confirm = "OK", string cancel = "Cancel", bool destructive = false)
    {
        string id = "";
        var view = new DialogView(title, message, new[]
        {
            new DialogButton(cancel, () => { Overlay.Dismiss(id); onResult(false); }),
            new DialogButton(confirm, () => { Overlay.Dismiss(id); onResult(true); }, Emphasized: true, Destructive: destructive),
        });
        id = Present(view);
    }

    static string Present(View view) =>
        Overlay.Present(view, new OverlayOptions
        {
            Position = OverlayPosition.Center,
            DimBackground = true,
            TapOutsideToDismiss = false,   // a modal decision must be made via a button
        });
}

/// <summary>The visual for a <see cref="Dialog"/> — a titled card with a message and a button row.</summary>
sealed class DialogView : View
{
    readonly string _title;
    readonly string _message;
    readonly DialogButton[] _buttons;

    public DialogView(string title, string message, DialogButton[] buttons)
    {
        _title = title;
        _message = message;
        _buttons = buttons;
    }

    public override View Body
    {
        get
        {
            var buttonViews = new View[_buttons.Length];
            for (var i = 0; i < _buttons.Length; i++)
            {
                var b = _buttons[i];
                var color = b.Destructive ? ControlPalette.Accent(PillType.Critical)
                          : b.Emphasized ? ControlPalette.Accent(PillType.Info)
                          : ControlPalette.OnSurfaceVariant;
                buttonViews[i] = new Text(b.Label)
                    .Font(Font.Body)
                    .ForegroundColor(color)
                    .Padding(horizontal: 14, vertical: 8)
                    .OnTapGesture(b.Action);
            }

            return new VStack(
                    new Text(_title).Font(Font.Headline).ForegroundColor(ControlPalette.OnSurface),
                    new Text(_message).Font(Font.Body).ForegroundColor(ControlPalette.OnSurfaceVariant),
                    new HStack(buttonViews).Spacing(8).Alignment(VerticalAlignment.Center))
                .Spacing(12)
                .Padding(20)
                .Background(ControlPalette.Surface)
                .CornerRadius(16)
                .Shadow(20, SwiftColor.Hex("#000000"), 0, 8)
                .Frame(width: 300);
        }
    }
}
