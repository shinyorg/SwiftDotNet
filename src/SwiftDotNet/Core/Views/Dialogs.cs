using System.Text;

namespace SwiftDotNet;

/// <summary>
/// How a dialog button reads and behaves. Mirrors SwiftUI's <c>ButtonRole</c> — every backend maps this
/// to its own emphasis convention (tinted vs. red vs. plain, and which button Esc/back triggers).
/// </summary>
public enum DialogRole
{
    /// <summary>An ordinary choice. Emphasized where the platform emphasizes a default button.</summary>
    Default,
    /// <summary>Backs out without acting. Triggered by Esc / system back / scrim tap.</summary>
    Cancel,
    /// <summary>An irreversible choice. Rendered in the platform's destructive tint (red).</summary>
    Destructive,
}

/// <summary>
/// One button on an <see cref="Alert"/> or <see cref="ActionSheet"/>. The <paramref name="Action"/> runs
/// after the dialog dismisses, so it is safe to present another dialog from it.
/// </summary>
/// <remarks>
/// Not to be confused with <c>SwiftDotNet.Controls.DialogButton</c>, which is the in-app
/// (overlay-drawn) equivalent for the Controls library's imperative <c>Dialog</c> service.
/// </remarks>
public sealed record AlertButton(string Label, DialogRole Role = DialogRole.Default, Action? Action = null)
{
    /// <summary>
    /// An ordinary-role button with an action. The common case, and the one the positional record
    /// constructor reads worst for — <c>new AlertButton("Copy", DoCopy)</c> beats naming the role.
    /// </summary>
    public AlertButton(string label, Action? action) : this(label, DialogRole.Default, action) { }

    /// <summary>The default single-button acknowledgement — a cancel-role "OK".</summary>
    public static AlertButton Ok(Action? action = null) => new("OK", DialogRole.Cancel, action);

    /// <summary>A cancel-role button.</summary>
    public static AlertButton Cancel(string label = "Cancel", Action? action = null)
        => new(label, DialogRole.Cancel, action);

    /// <summary>A destructive-role button, rendered in the platform's red tint.</summary>
    public static AlertButton Destructive(string label, Action? action = null)
        => new(label, DialogRole.Destructive, action);
}

/// <summary>
/// The flat wire encoding for a dialog's button list, and the shared dismissal binding.
///
/// <see cref="Node"/> props are scalars (string/double/bool) by design, so the buttons ship as one
/// delimited string: <c>label,role;label,role</c>, with <c>\</c> escaping the two delimiters and itself.
/// Every backend parses the same string and emits the tapped button's <b>index</b> back as the event
/// payload; <c>"false"</c> means "dismissed without choosing" (scrim tap, Esc, system back).
/// </summary>
public static class DialogButtons
{
    /// <summary>The default button list when a caller supplies none — a single "OK".</summary>
    public static readonly IReadOnlyList<AlertButton> DefaultOk = new[] { AlertButton.Ok() };

    /// <summary>The wire token for a <see cref="DialogRole"/>.</summary>
    public static string Token(this DialogRole role) => role switch
    {
        DialogRole.Cancel => "cancel",
        DialogRole.Destructive => "destructive",
        _ => "default",
    };

    /// <summary>The role for a wire token; unknown tokens fall back to <see cref="DialogRole.Default"/>.</summary>
    public static DialogRole RoleFor(string? token) => token switch
    {
        "cancel" => DialogRole.Cancel,
        "destructive" => DialogRole.Destructive,
        _ => DialogRole.Default,
    };

    /// <summary>Encodes a button list to the flat wire string.</summary>
    public static string Encode(IReadOnlyList<AlertButton> buttons)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < buttons.Count; i++)
        {
            if (i > 0) sb.Append(';');
            Escape(sb, buttons[i].Label);
            sb.Append(',').Append(buttons[i].Role.Token());
        }
        return sb.ToString();
    }

    static void Escape(StringBuilder sb, string s)
    {
        foreach (var c in s)
        {
            if (c is '\\' or ',' or ';') sb.Append('\\');
            sb.Append(c);
        }
    }

    /// <summary>
    /// Parses the wire string back into labels and roles. Malformed entries are skipped rather than
    /// thrown on — a bad button must not take down a render. An empty/absent string yields a single "OK",
    /// so a backend can always render <em>something</em> dismissable.
    /// </summary>
    public static List<(string Label, DialogRole Role)> Parse(string? encoded)
    {
        var result = new List<(string, DialogRole)>();
        if (string.IsNullOrEmpty(encoded)) return new() { ("OK", DialogRole.Cancel) };

        var label = new StringBuilder();
        var role = new StringBuilder();
        var inRole = false;
        var escaped = false;

        void Flush()
        {
            if (label.Length > 0 || role.Length > 0)
                result.Add((label.ToString(), RoleFor(role.ToString())));
            label.Clear();
            role.Clear();
            inRole = false;
        }

        foreach (var c in encoded)
        {
            if (escaped) { (inRole ? role : label).Append(c); escaped = false; continue; }
            switch (c)
            {
                case '\\': escaped = true; break;
                case ',' when !inRole: inRole = true; break;
                case ';': Flush(); break;
                default: (inRole ? role : label).Append(c); break;
            }
        }
        Flush();

        return result.Count > 0 ? result : new() { ("OK", DialogRole.Cancel) };
    }

    /// <summary>
    /// The index of the first <see cref="DialogRole.Cancel"/> button, or -1. Backends that have a
    /// dedicated cancel slot (Compose's <c>dismissButton</c>, WinUI's close button, a bottom sheet's
    /// detached Cancel row) use this to pull that button out of the main list.
    /// </summary>
    public static int CancelIndex(List<(string Label, DialogRole Role)> buttons)
        => buttons.FindIndex(b => b.Role == DialogRole.Cancel);

    /// <summary>
    /// The dismissal binding every dialog view shares. Backends emit the tapped button's index, or
    /// <c>"false"</c> for a choice-free dismissal (and <c>"true"</c> when a host echoes back that it
    /// presented). The flag is cleared <em>before</em> the action runs, so an action may present again.
    /// </summary>
    internal static void Bind(RenderContext ctx, string nodeId, State<bool> isPresented, IReadOnlyList<AlertButton> buttons)
        => ctx.RegisterAction(nodeId, value =>
        {
            if (value == "true") { isPresented.Value = true; return; }
            isPresented.Value = false;
            if (value is null || value == "false") return;
            if (int.TryParse(value, out var i) && i >= 0 && i < buttons.Count) buttons[i].Action?.Invoke();
        });
}

/// <summary>
/// Presents a modal alert — title, message, and one or more <see cref="AlertButton"/>s — when the bound
/// flag is true. The SwiftUI analog is <c>.alert(_:isPresented:actions:message:)</c>; each backend
/// renders its own native alert. Child 0 is the body the alert is attached to.
/// </summary>
public sealed class Alert : View
{
    readonly State<bool> _isPresented;
    readonly string _title;
    readonly string _message;
    readonly IReadOnlyList<AlertButton> _buttons;
    readonly View _body;

    /// <summary>A single-button ("OK") alert.</summary>
    public Alert(State<bool> isPresented, string title, string message, View body)
        : this(isPresented, title, message, DialogButtons.DefaultOk, body) { }

    /// <summary>An alert with an explicit button list, in the order they should read.</summary>
    public Alert(State<bool> isPresented, string title, string message, IReadOnlyList<AlertButton> buttons, View body)
    {
        _isPresented = isPresented;
        _title = title;
        _message = message;
        _buttons = buttons.Count > 0 ? buttons : DialogButtons.DefaultOk;
        _body = body;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("Alert", path);
        node.Props["presented"] = _isPresented.Value;
        node.Props["title"] = _title;
        node.Props["message"] = _message;
        node.Props["buttons"] = DialogButtons.Encode(_buttons);
        DialogButtons.Bind(ctx, node.Id, _isPresented, _buttons);
        node.Children.Add(_body.ToNode(ctx, path + ".0"));
        return node;
    }
}

/// <summary>
/// Presents a list of choices over the content when the bound flag is true — SwiftUI's
/// <c>.confirmationDialog</c> / UIKit's action sheet / Compose's bottom sheet. Unlike
/// <see cref="Alert"/> it is built for <em>many</em> options and puts the cancel button in the
/// platform's conventional detached slot. Child 0 is the body the sheet is attached to.
/// </summary>
public sealed class ActionSheet : View
{
    readonly State<bool> _isPresented;
    readonly string _title;
    readonly string _message;
    readonly IReadOnlyList<AlertButton> _buttons;
    readonly View _body;

    public ActionSheet(State<bool> isPresented, string title, IReadOnlyList<AlertButton> buttons, View body, string message = "")
    {
        _isPresented = isPresented;
        _title = title;
        _message = message;
        _buttons = buttons.Count > 0 ? buttons : DialogButtons.DefaultOk;
        _body = body;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("ActionSheet", path);
        node.Props["presented"] = _isPresented.Value;
        node.Props["title"] = _title;
        node.Props["message"] = _message;
        node.Props["buttons"] = DialogButtons.Encode(_buttons);
        DialogButtons.Bind(ctx, node.Id, _isPresented, _buttons);
        node.Children.Add(_body.ToNode(ctx, path + ".0"));
        return node;
    }
}
