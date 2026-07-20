namespace SwiftDotNet.Controls;

/// <summary>A <see cref="DataGrid{T}"/> column: a header, a cell-text projection, and optional width/sort key.</summary>
public sealed class DataGridColumn<T>
{
    public string Title { get; }
    public Func<T, string> Value { get; }
    public double? Width { get; }
    public Func<T, IComparable>? SortKey { get; }

    public DataGridColumn(string title, Func<T, string> value, double? width = null, Func<T, IComparable>? sortKey = null)
    {
        Title = title;
        Value = value;
        Width = width;
        SortKey = sortKey ?? (x => value(x));
    }
}

/// <summary>
/// A sortable data grid — ported (in reduced form) from Shiny's <c>DataGrid</c>. Pure composite: a header
/// row of tappable columns over scrolling data rows; tapping a header sorts by that column (toggling
/// ascending/descending). Small-to-medium N — true virtualization awaits Plan-1 F7(d).
/// </summary>
public sealed class DataGrid<T> : View
{
    readonly IReadOnlyList<T> _items;
    readonly DataGridColumn<T>[] _columns;
    readonly State<int> _sortColumn = State(-1);
    readonly State<bool> _ascending = State(true);

    public DataGrid(IReadOnlyList<T> items, params DataGridColumn<T>[] columns)
    {
        _items = items;
        _columns = columns;
    }

    public override View Body
    {
        get
        {
            // Header — each column tappable to sort (toggles direction if already the sort column).
            var header = new View[_columns.Length];
            for (var i = 0; i < _columns.Length; i++)
            {
                var col = _columns[i];
                var idx = i;
                var arrow = _sortColumn.Value == idx ? (_ascending.Value ? " ▲" : " ▼") : "";
                header[i] = Cell(new Text(col.Title + arrow).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant), col.Width)
                    .OnTapGesture(() =>
                    {
                        if (_sortColumn.Value == idx) _ascending.Value = !_ascending.Value;
                        else { _sortColumn.Value = idx; _ascending.Value = true; }
                    });
            }

            // Sort the rows in C#.
            IEnumerable<T> ordered = _items;
            if (_sortColumn.Value >= 0 && _sortColumn.Value < _columns.Length && _columns[_sortColumn.Value].SortKey is { } key)
                ordered = _ascending.Value ? _items.OrderBy(key) : _items.OrderByDescending(key);
            var rows = ordered.ToList();

            var rowViews = new View[rows.Count];
            for (var r = 0; r < rows.Count; r++)
            {
                var item = rows[r];
                var cells = new View[_columns.Length];
                for (var c = 0; c < _columns.Length; c++)
                    cells[c] = Cell(new Text(_columns[c].Value(item)).Font(Font.Body), _columns[c].Width);
                rowViews[r] = new HStack(cells).Spacing(8)
                    .Padding(horizontal: 12, vertical: 8)
                    .Background(r % 2 == 0 ? ControlPalette.Surface : ControlPalette.SurfaceVariant);
            }

            return new VStack(
                    new HStack(header).Spacing(8).Padding(horizontal: 12, vertical: 8).Background(ControlPalette.SurfaceVariant),
                    // .Align(Leading) makes the row stack fill the grid's width. Without it a backend
                    // that centres scroll-view content (Skia does) offsets the rows relative to the
                    // header, so the columns stop lining up.
                    new ScrollView(new VStack(rowViews).Spacing(0)
                        .Alignment(HorizontalAlignment.Leading)
                        .Align(Alignment.Leading)))
                .Spacing(0)
                .Alignment(HorizontalAlignment.Leading)
                .Border(ControlPalette.Outline, 1, cornerRadius: 8);
        }
    }

    static View Cell(View content, double? width) =>
        width is { } w ? new HStack(content).Frame(width: w) : new HStack(content, new Spacer()).Align(Alignment.Leading);
}
