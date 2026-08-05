using System.Globalization;

namespace SwiftDotNet;

/// <summary>The cell a <see cref="Grid"/> child ended up in, after explicit pins and flow are resolved.</summary>
public readonly struct GridSpan
{
    public int Column { get; }
    public int Row { get; }
    public int ColumnSpan { get; }
    public int RowSpan { get; }

    public GridSpan(int column, int row, int columnSpan, int rowSpan)
    {
        Column = column;
        Row = row;
        ColumnSpan = columnSpan;
        RowSpan = rowSpan;
    }
}

/// <summary>
/// The parts of grid layout that every backend needs and none of them should reinvent: parsing the
/// <c>columnTracks</c>/<c>rowTracks</c> wire strings, and resolving which cell each child occupies once
/// explicit <c>.GridCell(...)</c> pins and multi-cell spans are taken into account.
///
/// Track <em>sizing</em> deliberately lives in each backend: GTK/WinUI/TUI/Web hand their tracks to a
/// native grid that already does it, and only the self-drawing Skia renderer computes widths itself
/// (mirrored in the Swift and Compose shims).
/// </summary>
public static class GridEngine
{
    /// <summary>
    /// Parses a comma-joined track spec (<c>"fixed:80,star:1,auto,flex:40:inf"</c>). A null/empty spec —
    /// or any token that doesn't parse — yields <paramref name="fallbackCount"/> equal star tracks, which
    /// is the plain <c>new Grid(n, …)</c> shape.
    /// </summary>
    public static GridTrack[] ParseTracks(string? spec, int fallbackCount)
    {
        if (string.IsNullOrEmpty(spec)) return Uniform(fallbackCount);

        var parts = spec.Split(',');
        var tracks = new GridTrack[parts.Length];
        for (var i = 0; i < parts.Length; i++) tracks[i] = ParseTrack(parts[i]);
        return tracks;
    }

    /// <summary><paramref name="count"/> equal <see cref="GridTrack.Star()"/> tracks (at least one).</summary>
    public static GridTrack[] Uniform(int count)
    {
        var tracks = new GridTrack[Math.Max(1, count)];
        for (var i = 0; i < tracks.Length; i++) tracks[i] = GridTrack.Star();
        return tracks;
    }

    static GridTrack ParseTrack(string token)
    {
        var colon = token.IndexOf(':');
        var kind = colon < 0 ? token : token[..colon];
        var rest = colon < 0 ? "" : token[(colon + 1)..];

        switch (kind)
        {
            case "fixed":
                return GridTrack.Fixed(Num(rest));
            case "star":
                return GridTrack.Star(rest.Length == 0 ? 1 : Num(rest));
            case "flex":
            {
                var sep = rest.IndexOf(':');
                var min = sep < 0 ? rest : rest[..sep];
                var max = sep < 0 ? "inf" : rest[(sep + 1)..];
                return GridTrack.Flexible(Num(min), max == "inf" ? null : Num(max));
            }
            default:
                return GridTrack.Auto;
        }
    }

    static double Num(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;

    /// <summary>
    /// Assigns every child a cell. Children with an explicit column/row are pinned there first; the rest
    /// flow left-to-right, top-to-bottom into the first cell where their whole span is free. A span wider
    /// than the grid is clamped to the remaining columns so a child can never fall off the edge.
    /// </summary>
    /// <param name="columns">Column count (at least 1).</param>
    /// <param name="requested">Per child, what <c>.GridCell</c>/<c>.GridSpan</c> asked for — null column
    /// or row means "flow me".</param>
    /// <param name="rowCount">The number of rows the result actually needs.</param>
    public static GridSpan[] Place(
        int columns,
        IReadOnlyList<(int? Column, int? Row, int ColumnSpan, int RowSpan)> requested,
        out int rowCount)
    {
        columns = Math.Max(1, columns);
        var result = new GridSpan[requested.Count];
        var placed = new bool[requested.Count];
        var occupied = new List<bool[]>();

        // Pass 1 — pin the explicitly placed children, so the flowing ones can route around them.
        for (var i = 0; i < requested.Count; i++)
        {
            var r = requested[i];
            if (r.Column is not { } c || r.Row is not { } row) continue;
            c = Math.Clamp(c, 0, columns - 1);
            var cs = Math.Clamp(r.ColumnSpan, 1, columns - c);
            var rs = Math.Max(1, r.RowSpan);
            Occupy(occupied, columns, c, Math.Max(0, row), cs, rs);
            result[i] = new GridSpan(c, Math.Max(0, row), cs, rs);
            placed[i] = true;
        }

        // Pass 2 — flow the rest into the first free cell that fits.
        var cursorRow = 0;
        var cursorCol = 0;
        for (var i = 0; i < requested.Count; i++)
        {
            if (placed[i]) continue;
            var r = requested[i];
            var cs = Math.Clamp(r.ColumnSpan, 1, columns);
            var rs = Math.Max(1, r.RowSpan);

            while (true)
            {
                if (cursorCol + cs > columns) { cursorCol = 0; cursorRow++; continue; }
                if (!IsFree(occupied, columns, cursorCol, cursorRow, cs, rs)) { cursorCol++; continue; }
                break;
            }

            Occupy(occupied, columns, cursorCol, cursorRow, cs, rs);
            result[i] = new GridSpan(cursorCol, cursorRow, cs, rs);
            cursorCol += cs;
        }

        rowCount = occupied.Count;
        return result;
    }

    static void EnsureRows(List<bool[]> occupied, int columns, int through)
    {
        while (occupied.Count <= through) occupied.Add(new bool[columns]);
    }

    static bool IsFree(List<bool[]> occupied, int columns, int col, int row, int colSpan, int rowSpan)
    {
        for (var r = row; r < row + rowSpan; r++)
        {
            if (r >= occupied.Count) continue;   // rows past the end are empty by definition
            for (var c = col; c < col + colSpan && c < columns; c++)
                if (occupied[r][c]) return false;
        }
        return true;
    }

    static void Occupy(List<bool[]> occupied, int columns, int col, int row, int colSpan, int rowSpan)
    {
        EnsureRows(occupied, columns, row + rowSpan - 1);
        for (var r = row; r < row + rowSpan; r++)
            for (var c = col; c < col + colSpan && c < columns; c++)
                occupied[r][c] = true;
    }
}
