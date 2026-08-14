namespace SwiftDotNet;

/// <summary>
/// Fluent presentation modifiers, so attaching a modal reads the way it does in SwiftUI — you modify the
/// view the dialog hangs off, rather than wrapping it:
/// <code>
/// new Button("Delete", () => _confirm.Value = true)
///     .ConfirmationDialog(_confirm, "Delete this item?",
///         AlertButton.Destructive("Delete", DoDelete),
///         AlertButton.Cancel())
/// </code>
/// Each one builds the same node the equivalent constructor does; they exist purely for call-site shape.
/// </summary>
public static class PresentationModifiers
{
    /// <summary>Presents <paramref name="content"/> modally over this view while the flag is true.</summary>
    public static Sheet Sheet<T>(this T view, State<bool> isPresented, View content) where T : View
        => new(isPresented, view, content);

    /// <summary>Presents a single-button ("OK") alert over this view while the flag is true.</summary>
    public static Alert Alert<T>(this T view, State<bool> isPresented, string title, string message) where T : View
        => new(isPresented, title, message, DialogButtons.DefaultOk, view);

    /// <summary>Presents an alert with an explicit button list over this view while the flag is true.</summary>
    public static Alert Alert<T>(this T view, State<bool> isPresented, string title, string message,
        params AlertButton[] buttons) where T : View
        => new(isPresented, title, message, buttons, view);

    /// <summary>
    /// Presents an action sheet / confirmation dialog of choices over this view while the flag is true.
    /// Mirrors SwiftUI's <c>.confirmationDialog</c>.
    /// </summary>
    public static ActionSheet ConfirmationDialog<T>(this T view, State<bool> isPresented, string title,
        params AlertButton[] buttons) where T : View
        => new(isPresented, title, buttons, view);

    /// <summary>An action sheet with an explanatory message under the title.</summary>
    public static ActionSheet ConfirmationDialog<T>(this T view, State<bool> isPresented, string title,
        string message, params AlertButton[] buttons) where T : View
        => new(isPresented, title, buttons, view, message);

    /// <summary>Alias of <see cref="ConfirmationDialog{T}(T, State{bool}, string, AlertButton[])"/> for UIKit-shaped call sites.</summary>
    public static ActionSheet ActionSheet<T>(this T view, State<bool> isPresented, string title,
        params AlertButton[] buttons) where T : View
        => new(isPresented, title, buttons, view);
}
