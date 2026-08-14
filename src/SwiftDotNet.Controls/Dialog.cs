namespace SwiftDotNet.Controls;

/// <summary>One button in a <see cref="Dialog"/>: a label, an optional emphasized/destructive style, and its action.</summary>
public sealed record DialogButton(string Label, Action Action, bool Emphasized = false, bool Destructive = false);

/// <summary>
/// In-app modal dialogs — alert / confirm / prompt / action sheet — ported from Shiny's
/// <c>DialogService</c>. Presents a centered card (or a bottom sheet, for
/// <see cref="ActionSheet"/>) over a dimmed F2 <see cref="Overlay"/> scrim. Requires an
/// <see cref="OverlayHost"/> root.
/// </summary>
/// <remarks>
/// These are drawn <em>in-app</em>, so they look identical on every backend and can be called
/// imperatively from anywhere. For the platform's own native dialog, use the declarative
/// <see cref="SwiftDotNet.Alert"/> / <see cref="SwiftDotNet.ActionSheet"/> views instead.
/// </remarks>
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

    /// <summary>
    /// A single-line text prompt. <paramref name="onResult"/> gets the entered text on confirm, or
    /// <c>null</c> on cancel — so "cancelled" and "submitted empty" stay distinguishable.
    /// </summary>
    public static void Prompt(string title, string message, Action<string?> onResult,
        string initialValue = "", string placeholder = "", string confirm = "OK", string cancel = "Cancel",
        KeyboardType keyboard = KeyboardType.Default, int? maxLength = null)
    {
        string id = "";
        var text = new State<string>(initialValue);
        var view = new PromptView(title, message, placeholder, text, keyboard, maxLength, new[]
        {
            new DialogButton(cancel, () => { Overlay.Dismiss(id); onResult(null); }),
            new DialogButton(confirm, () => { Overlay.Dismiss(id); onResult(text.Value); }, Emphasized: true),
        });
        id = Present(view);
    }

    /// <summary>
    /// A bottom-anchored list of choices. The optional <paramref name="cancel"/> label adds a detached
    /// cancel row (tapping the scrim does the same thing) and reports index <c>-1</c>;
    /// <paramref name="onResult"/> otherwise gets the index of the chosen option.
    /// </summary>
    public static void ActionSheet(string title, string[] options, Action<int> onResult,
        string message = "", string? cancel = "Cancel", int destructiveIndex = -1)
    {
        string id = "";
        var buttons = new DialogButton[options.Length];
        for (var i = 0; i < options.Length; i++)
        {
            var index = i;   // capture
            buttons[i] = new DialogButton(options[i], () => { Overlay.Dismiss(id); onResult(index); },
                Destructive: i == destructiveIndex);
        }
        var view = new ActionSheetView(title, message, buttons,
            cancel is null ? null : new DialogButton(cancel, () => { Overlay.Dismiss(id); onResult(-1); }));

        id = Overlay.Present(view, new OverlayOptions
        {
            Position = OverlayPosition.Bottom,
            DimBackground = true,
            // Cancel goes through the Cancel row, not the scrim — otherwise a scrim tap would dismiss
            // without ever reporting a result, and the caller would wait on a callback that never comes.
            TapOutsideToDismiss = false,
        });
    }

    static string Present(View view) =>
        Overlay.Present(view, new OverlayOptions
        {
            Position = OverlayPosition.Center,
            DimBackground = true,
            TapOutsideToDismiss = false,   // a modal decision must be made via a button
        });
}

/// <summary>Shared chrome for the dialog cards — the button row and the card container.</summary>
static class DialogChrome
{
    internal static SwiftColor ColorFor(DialogButton b) =>
        b.Destructive ? ControlPalette.Accent(PillType.Critical)
        : b.Emphasized ? ControlPalette.Accent(PillType.Info)
        : ControlPalette.OnSurfaceVariant;

    internal static View ButtonRow(DialogButton[] buttons)
    {
        var views = new View[buttons.Length];
        for (var i = 0; i < buttons.Length; i++)
        {
            var b = buttons[i];
            views[i] = new Text(b.Label)
                .Font(Font.Body)
                .ForegroundColor(ColorFor(b))
                .Padding(horizontal: 14, vertical: 8)
                .OnTapGesture(b.Action);
        }
        return new HStack(views).Spacing(8).Alignment(VerticalAlignment.Center);
    }

    internal static View Card(View content, double width = 300) =>
        content
            .Padding(20)
            .Background(ControlPalette.Surface)
            .CornerRadius(16)
            .Shadow(20, SwiftColor.Hex("#000000"), 0, 8)
            .Frame(width: width);
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

    public override View Body =>
        DialogChrome.Card(
            new VStack(
                    new Text(_title).Font(Font.Headline).ForegroundColor(ControlPalette.OnSurface),
                    new Text(_message).Font(Font.Body).ForegroundColor(ControlPalette.OnSurfaceVariant),
                    DialogChrome.ButtonRow(_buttons))
                .Spacing(12));
}

/// <summary>
/// The visual for <see cref="Dialog.Prompt"/> — the alert card plus a bound <see cref="TextField"/>.
/// The <see cref="State{T}"/> is owned by the caller (<c>Dialog.Prompt</c>) rather than this view so
/// the confirm button's closure can read the final text without reaching back into the view.
/// </summary>
sealed class PromptView : View
{
    readonly string _title;
    readonly string _message;
    readonly string _placeholder;
    readonly State<string> _text;
    readonly KeyboardType _keyboard;
    readonly int? _maxLength;
    readonly DialogButton[] _buttons;

    public PromptView(string title, string message, string placeholder, State<string> text,
        KeyboardType keyboard, int? maxLength, DialogButton[] buttons)
    {
        _title = title;
        _message = message;
        _placeholder = placeholder;
        _text = text;
        _keyboard = keyboard;
        _maxLength = maxLength;
        _buttons = buttons;
    }

    public override View Body
    {
        get
        {
            var entry = new TextField(_placeholder, _text).Keyboard(_keyboard);
            if (_maxLength is { } max) entry = entry.MaxLength(max);

            var rows = new List<View>
            {
                new Text(_title).Font(Font.Headline).ForegroundColor(ControlPalette.OnSurface),
            };
            if (_message.Length > 0)
                rows.Add(new Text(_message).Font(Font.Body).ForegroundColor(ControlPalette.OnSurfaceVariant));
            rows.Add(entry
                .Padding(horizontal: 10, vertical: 8)
                .Background(ControlPalette.SurfaceVariant)
                .CornerRadius(8));
            rows.Add(DialogChrome.ButtonRow(_buttons));

            return DialogChrome.Card(new VStack(rows.ToArray()).Spacing(12));
        }
    }
}

/// <summary>
/// The visual for <see cref="Dialog.ActionSheet"/> — a bottom-anchored option list with the cancel row
/// detached below it, the iOS convention. Full-width rather than the alert card's fixed 300pt so the
/// options stay readable on a phone.
/// </summary>
sealed class ActionSheetView : View
{
    readonly string _title;
    readonly string _message;
    readonly DialogButton[] _options;
    readonly DialogButton? _cancel;

    public ActionSheetView(string title, string message, DialogButton[] options, DialogButton? cancel)
    {
        _title = title;
        _message = message;
        _options = options;
        _cancel = cancel;
    }

    public override View Body
    {
        get
        {
            var rows = new List<View>();
            if (_title.Length > 0)
                rows.Add(new Text(_title).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant));
            if (_message.Length > 0)
                rows.Add(new Text(_message).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant));

            foreach (var option in _options)
                rows.Add(Row(option));

            var group = new VStack(rows.ToArray())
                .Spacing(4)
                .Padding(12)
                .Background(ControlPalette.Surface)
                .CornerRadius(14);

            if (_cancel is null) return group.Padding(12).Frame(width: 340);

            return new VStack(
                    group,
                    new VStack(Row(_cancel))
                        .Padding(12)
                        .Background(ControlPalette.Surface)
                        .CornerRadius(14))
                .Spacing(8)
                .Padding(12)
                .Frame(width: 340);
        }
    }

    static View Row(DialogButton b) =>
        new Text(b.Label)
            .Font(Font.Body)
            .ForegroundColor(DialogChrome.ColorFor(b))
            .Padding(horizontal: 0, vertical: 12)
            .Frame(width: 300)
            .OnTapGesture(b.Action);
}
