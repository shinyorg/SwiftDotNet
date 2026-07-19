namespace SwiftDotNet;

/// <summary>The on-screen keyboard layout for a text field (F9); mirrors UIKit/Compose keyboard types.</summary>
public enum KeyboardType { Default, Number, Decimal, Email, Phone, Url }

/// <summary>The return/submit key label for a text field (F9).</summary>
public enum ReturnKeyType { Default, Done, Go, Next, Search, Send }

/// <summary>
/// A text entry field with two-way binding. Pass a <see cref="State{T}"/> of string; edits in the
/// native field flow back and update the state, which re-renders anything derived from it. F9 fluent
/// options configure the keyboard, the return key, and a maximum length.
/// </summary>
public sealed class TextField : View
{
    readonly string _placeholder;
    readonly State<string> _text;
    KeyboardType _keyboard = KeyboardType.Default;
    ReturnKeyType _returnKey = ReturnKeyType.Default;
    int? _maxLength;

    public TextField(string placeholder, State<string> text)
    {
        _placeholder = placeholder;
        _text = text;
    }

    /// <summary>Sets the on-screen keyboard layout (F9).</summary>
    public TextField Keyboard(KeyboardType type) { _keyboard = type; return this; }

    /// <summary>Sets the return/submit key label (F9).</summary>
    public TextField ReturnKey(ReturnKeyType type) { _returnKey = type; return this; }

    /// <summary>Caps the number of characters; enforced in-binding so it holds on every backend (F9).</summary>
    public TextField MaxLength(int max) { _maxLength = max; return this; }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("TextField", path);
        node.Props["placeholder"] = _placeholder;
        node.Props["text"] = _text.Value;
        if (_keyboard != KeyboardType.Default) node.Props["keyboard"] = KeyboardToken(_keyboard);
        if (_returnKey != ReturnKeyType.Default) node.Props["returnKey"] = ReturnToken(_returnKey);
        if (_maxLength is { } max) node.Props["maxLength"] = (double)max;
        // Enforce max length in the binding so it holds even where the native field doesn't clamp.
        ctx.RegisterAction(node.Id, value =>
        {
            var v = value ?? "";
            if (_maxLength is { } m && v.Length > m) v = v[..m];
            _text.Value = v;
        });
        return node;
    }

    internal static string KeyboardToken(KeyboardType t) => t switch
    {
        KeyboardType.Number => "number",
        KeyboardType.Decimal => "decimal",
        KeyboardType.Email => "email",
        KeyboardType.Phone => "phone",
        KeyboardType.Url => "url",
        _ => "default",
    };

    internal static string ReturnToken(ReturnKeyType t) => t switch
    {
        ReturnKeyType.Done => "done",
        ReturnKeyType.Go => "go",
        ReturnKeyType.Next => "next",
        ReturnKeyType.Search => "search",
        ReturnKeyType.Send => "send",
        _ => "default",
    };
}
