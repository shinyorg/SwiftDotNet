namespace SwiftDotNet.Controls;

/// <summary>
/// A searchable country selector — ported from Shiny's <c>CountryPicker</c>. A trigger row shows the
/// selected country; tapping it presents a searchable list over the F2 <see cref="Overlay"/> layer.
/// Pure composite. Requires an <see cref="OverlayHost"/> root.
/// </summary>
public sealed class CountryPicker : View
{
    readonly State<Country?> _selected;
    IReadOnlyList<Country> _source = CountryData.All;

    public CountryPicker(State<Country?> selected) => _selected = selected;

    /// <summary>Supply a custom country list (defaults to <see cref="CountryData.All"/>).</summary>
    public CountryPicker Source(IReadOnlyList<Country> source) { _source = source; return this; }

    public override View Body
    {
        get
        {
            var label = _selected.Value is { } c ? $"{c.Flag}  {c.Name}" : "Select country";
            return new HStack(
                    new Text(label).ForegroundColor(_selected.Value is null ? ControlPalette.OnSurfaceVariant : ControlPalette.OnSurface),
                    new Spacer(),
                    new Text("▾").ForegroundColor(ControlPalette.OnSurfaceVariant))
                .Padding(horizontal: 14, vertical: 12)
                .Background(ControlPalette.SurfaceVariant)
                .CornerRadius(10)
                .OnTapGesture(Open);
        }
    }

    void Open()
    {
        string id = "";
        var sheet = new CountryPickerSheet(_source, c => { _selected.Value = c; Overlay.Dismiss(id); });
        id = Overlay.Present(sheet, new OverlayOptions { Position = OverlayPosition.Center, DimBackground = true, TapOutsideToDismiss = true });
    }
}

/// <summary>The searchable list presented by <see cref="CountryPicker"/>.</summary>
sealed class CountryPickerSheet : View
{
    readonly IReadOnlyList<Country> _source;
    readonly Action<Country> _pick;
    readonly State<string> _search = State("");

    public CountryPickerSheet(IReadOnlyList<Country> source, Action<Country> pick)
    {
        _source = source;
        _pick = pick;
    }

    public override View Body
    {
        get
        {
            var q = _search.Value;
            var matches = _source
                .Where(c => q.Length == 0 || c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || c.DialCode.Contains(q))
                .Take(8)
                .ToList();

            var rows = new View[matches.Count];
            for (var i = 0; i < matches.Count; i++)
            {
                var c = matches[i];
                rows[i] = new HStack(
                        new Text($"{c.Flag}  {c.Name}"),
                        new Spacer(),
                        new Text(c.DialCode).ForegroundColor(ControlPalette.OnSurfaceVariant))
                    .Padding(horizontal: 12, vertical: 10)
                    .OnTapGesture(() => _pick(c));
            }

            return new VStack(
                    new Text("Country").Font(Font.Headline),
                    new TextField("Search…", _search),
                    new VStack(rows).Spacing(0).Alignment(HorizontalAlignment.Leading))
                .Spacing(12)
                .Padding(18)
                .Background(ControlPalette.Surface)
                .CornerRadius(16)
                .Shadow(20, SwiftColor.Hex("#000000"), 0, 8)
                .Frame(width: 320);
        }
    }
}
