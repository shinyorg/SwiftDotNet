namespace SwiftDotNet.Controls;

/// <summary>
/// An enhanced text entry — a caption label above the field and a clear (✕) button — ported (in reduced
/// form) from Shiny's <c>TextEntry</c>. Uses F9's keyboard-type configuration. Pure composite.
/// </summary>
public sealed class TextEntry : View
{
    readonly string _label;
    readonly State<string> _text;
    KeyboardType _keyboard = KeyboardType.Default;
    int? _maxLength;

    public TextEntry(string label, State<string> text)
    {
        _label = label;
        _text = text;
    }

    public TextEntry Keyboard(KeyboardType type) { _keyboard = type; return this; }
    public TextEntry MaxLength(int max) { _maxLength = max; return this; }

    public override View Body
    {
        get
        {
            var input = new TextField(_label, _text).Keyboard(_keyboard);
            if (_maxLength is { } max) input = input.MaxLength(max);

            var row = _text.Value.Length > 0
                ? new HStack(input, new Button("✕", () => _text.Value = "")).Spacing(8)
                : new HStack(input).Spacing(8);

            return new VStack(
                    new Text(_label).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant),
                    row)
                .Spacing(4)
                .Alignment(HorizontalAlignment.Leading);
        }
    }
}
