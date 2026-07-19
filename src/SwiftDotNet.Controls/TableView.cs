namespace SwiftDotNet.Controls;

/// <summary>A titled group of rows in a <see cref="TableView"/> (Shiny's <c>TableSection</c>).</summary>
public sealed class TableSection
{
    internal string? Header { get; }
    internal View[] Rows { get; }

    public TableSection(string? header, params View[] rows)
    {
        Header = header;
        Rows = rows;
    }

    public TableSection(params View[] rows) : this(null, rows) { }
}

/// <summary>
/// A grouped, settings-style table — ported from Shiny's <c>TableView</c>. A composite over the built-in
/// <see cref="Form"/>/<see cref="Section"/>, so grouped separators and native row chrome come for free on
/// every backend. Fill sections with <see cref="Cell"/> rows.
/// </summary>
public sealed class TableView : View
{
    readonly TableSection[] _sections;

    public TableView(params TableSection[] sections) => _sections = sections;

    public override View Body
    {
        get
        {
            var sections = new View[_sections.Length];
            for (var i = 0; i < _sections.Length; i++)
            {
                var s = _sections[i];
                sections[i] = s.Header is { } h ? new Section(h, s.Rows) : new Section(s.Rows);
            }
            return new Form(sections);
        }
    }
}
