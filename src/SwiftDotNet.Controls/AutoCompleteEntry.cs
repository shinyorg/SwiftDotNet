namespace SwiftDotNet.Controls;

/// <summary>
/// A text field with a live suggestion dropdown — ported from Shiny's <c>AutoCompleteEntry</c>. A pure
/// composite: a bound <see cref="TextField"/> plus an inline list of matching suggestions that fills the
/// bound text when tapped. Case-insensitive contains-match, capped to <see cref="MaxSuggestions"/>.
/// </summary>
public sealed class AutoCompleteEntry : View
{
    readonly State<string> _text;
    readonly IReadOnlyList<string> _source;
    string _placeholder = "";
    int _maxSuggestions = 6;
    Action<string>? _onSelected;

    public AutoCompleteEntry(State<string> text, IReadOnlyList<string> source)
    {
        _text = text;
        _source = source;
    }

    public AutoCompleteEntry Placeholder(string placeholder) { _placeholder = placeholder; return this; }
    public AutoCompleteEntry MaxSuggestions(int max) { _maxSuggestions = max; return this; }
    public AutoCompleteEntry OnSelected(Action<string> handler) { _onSelected = handler; return this; }

    public override View Body
    {
        get
        {
            var query = _text.Value;
            var rows = new List<View> { new TextField(_placeholder, _text) };

            if (query.Length > 0)
            {
                var matches = _source
                    .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                                !s.Equals(query, StringComparison.OrdinalIgnoreCase))
                    .Take(_maxSuggestions)
                    .ToList();

                if (matches.Count > 0)
                {
                    var items = new View[matches.Count];
                    for (var i = 0; i < matches.Count; i++)
                    {
                        var s = matches[i];
                        items[i] = new HStack(new Text(s), new Spacer())
                            .Padding(horizontal: 12, vertical: 10)
                            .OnTapGesture(() => { _text.Value = s; _onSelected?.Invoke(s); });
                    }
                    rows.Add(new VStack(items)
                        .Spacing(0)
                        .Background(ControlPalette.Surface)
                        .CornerRadius(10)
                        .Border(ControlPalette.Outline, 1, cornerRadius: 10));
                }
            }

            return new VStack(rows.ToArray()).Spacing(4).Alignment(HorizontalAlignment.Leading);
        }
    }
}
