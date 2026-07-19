namespace SwiftDotNet.Controls;

/// <summary>
/// A Pinterest-style masonry grid — ported (as a composite) from Shiny's <c>StaggeredGrid</c>. Items are
/// distributed across a fixed number of columns so variable-height content packs into ragged columns.
/// Distribution is round-robin (no per-item measurement — Plan-1 F11 — so columns balance by count, not
/// pixel height); pass an item height hint via <see cref="ByHeight"/> for height-balanced packing.
/// </summary>
public sealed class StaggeredGrid : View
{
    readonly View[] _items;
    int _columns;
    double _spacing = 8;
    Func<int, double>? _heightHint;

    public StaggeredGrid(int columns, params View[] items)
    {
        _columns = Math.Max(1, columns);
        _items = items;
    }

    public StaggeredGrid Spacing(double spacing) { _spacing = spacing; return this; }

    /// <summary>Balance columns by cumulative height using a per-index height hint (masonry packing).</summary>
    public StaggeredGrid ByHeight(Func<int, double> heightForIndex) { _heightHint = heightForIndex; return this; }

    public override View Body
    {
        get
        {
            var columns = new List<View>[_columns];
            for (var i = 0; i < _columns; i++) columns[i] = new List<View>();

            if (_heightHint is { } h)
            {
                // Greedy: place each item in the currently-shortest column.
                var heights = new double[_columns];
                for (var i = 0; i < _items.Length; i++)
                {
                    var shortest = 0;
                    for (var c = 1; c < _columns; c++) if (heights[c] < heights[shortest]) shortest = c;
                    columns[shortest].Add(_items[i]);
                    heights[shortest] += h(i) + _spacing;
                }
            }
            else
            {
                for (var i = 0; i < _items.Length; i++) columns[i % _columns].Add(_items[i]);
            }

            var columnViews = new View[_columns];
            for (var c = 0; c < _columns; c++)
                columnViews[c] = new VStack(columns[c].ToArray()).Spacing(_spacing).Alignment(HorizontalAlignment.Leading);

            return new HStack(columnViews).Spacing(_spacing).Alignment(VerticalAlignment.Top);
        }
    }
}
