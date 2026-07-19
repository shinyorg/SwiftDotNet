namespace SwiftDotNet;

/// <summary>
/// A scrolling collection, rendered as a native SwiftUI <c>List</c>. Compose rows directly, or
/// build them from data with <see cref="ForEach{T}(IEnumerable{T}, Func{T, View})"/>.
///
/// <para>The keyed overload <see cref="ForEach{T}(IEnumerable{T}, Func{T, object}, Func{T, View})"/>
/// stamps a stable identity on each row (SwiftUI's <c>Identifiable</c> shape). Rows then keep their
/// native element across inserts/removals/reorders — hosts reconcile children by key instead of
/// tearing the list down and rebuilding it, so scroll position, focus, and animations survive and
/// the platform's row recycling/virtualization kicks in.</para>
///
/// <para>Row content is just a <c>Func&lt;T, View&gt;</c> — the analog of a SwiftUI <c>@ViewBuilder</c>
/// and of MAUI's <c>DataTemplate</c>. A "template selector" is simply branching inside that closure
/// (e.g. <c>x =&gt; x.IsHeader ? new Header(x) : new Row(x)</c>).</para>
/// </summary>
public sealed class List : View
{
    // Rows are produced lazily (the thunk runs at BuildNode time) so the generic item type can be
    // captured without List itself being generic, and so a future windowed source can materialize a
    // slice instead of the whole set.
    readonly Func<IReadOnlyList<Row>> _rows;
    readonly bool _keyed;

    internal readonly record struct Row(string? Key, View View);

    public List(params View[] rows)
    {
        var arr = rows;
        _rows = () => Array.ConvertAll(arr, v => new Row(null, v));
        _keyed = false;
    }

    List(Func<IReadOnlyList<Row>> rows, bool keyed)
    {
        _rows = rows;
        _keyed = keyed;
    }

    // ---- layout options (fluent) --------------------------------------------
    int _columns = 1;      // >1 → grid
    bool _horizontal;      // horizontal orientation

    /// <summary>Lay the rows out as a grid with <paramref name="span"/> columns (SwiftUI <c>LazyVGrid</c>).</summary>
    public List Columns(int span) { _columns = Math.Max(1, span); return this; }

    /// <summary>Scroll/arrange horizontally instead of vertically.</summary>
    public List Horizontal() { _horizontal = true; return this; }

    // ---- header / footer / empty slots --------------------------------------
    View? _header, _footer, _empty;

    /// <summary>A view pinned above the rows.</summary>
    public List Header(View view) { _header = view; return this; }

    /// <summary>A view below the rows.</summary>
    public List Footer(View view) { _footer = view; return this; }

    /// <summary>Shown in place of the rows when the collection is empty.</summary>
    public List Empty(View view) { _empty = view; return this; }

    // ---- selection (keyed by the row's id) ----------------------------------
    string? _selectionMode;
    Func<string, bool>? _isSelected;
    Action<string>? _onSelect;

    /// <summary>Single selection bound to the selected row's key (tap toggles). Requires a keyed list.</summary>
    public List Selection(State<string?> selected)
    {
        _selectionMode = "single";
        _isSelected = key => selected.Value == key;
        _onSelect = key => selected.Value = selected.Value == key ? null : key;
        return this;
    }

    /// <summary>Multiple selection bound to a set of selected row keys (tap toggles). Requires a keyed list.</summary>
    public List Selection(State<HashSet<string>> selected)
    {
        _selectionMode = "multiple";
        _isSelected = key => selected.Value.Contains(key);
        _onSelect = key =>
        {
            var next = new HashSet<string>(selected.Value);   // new set so State sees a change
            if (!next.Add(key)) next.Remove(key);
            selected.Value = next;
        };
        return this;
    }

    // ---- pull-to-refresh / incremental load ---------------------------------
    // Refresh and load-more ride the List node's event with reserved sentinel values so they don't
    // collide with selection keys (all three share one registered action).
    public const string RefreshValue = "refresh";
    public const string LoadMoreValue = "loadMore";

    Func<Task>? _onRefresh;
    Action? _onReachEnd;
    double _reachEndThreshold;

    /// <summary>Pull-to-refresh: runs <paramref name="onRefresh"/> when the user pulls the list down.</summary>
    public List Refreshable(Func<Task> onRefresh) { _onRefresh = onRefresh; return this; }

    /// <summary>Incremental load: fires <paramref name="onLoadMore"/> when scrolled within
    /// <paramref name="threshold"/> px of the end. Wire it to append more rows.</summary>
    public List OnReachEnd(Action onLoadMore, double threshold = 200) { _onReachEnd = onLoadMore; _reachEndThreshold = threshold; return this; }

    /// <summary>Data-driven rows: <c>List.ForEach(items, x =&gt; new Text(x.Name))</c>. Rows are matched
    /// positionally — good for static/append-only data. Use the keyed overload for dynamic collections.</summary>
    public static List ForEach<T>(IEnumerable<T> items, Func<T, View> row)
        => new(() => items.Select(x => new Row(null, row(x))).ToArray(), keyed: false);

    /// <summary>Keyed, identity-preserving rows: <c>List.ForEach(items, x =&gt; x.Id, x =&gt; new Text(x.Name))</c>.
    /// The <paramref name="id"/> selector gives each row a stable key so hosts can recycle native elements
    /// and apply granular updates across inserts, removals, and reordering.</summary>
    public static List ForEach<T>(IEnumerable<T> items, Func<T, object> id, Func<T, View> row)
        => new(() => items.Select(x => new Row(id(x)?.ToString() ?? "", row(x))).ToArray(), keyed: true);

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("List", path);
        if (_keyed) node.Props["keyed"] = true;
        if (_columns > 1) { node.Props["layout"] = "grid"; node.Props["columns"] = (double)_columns; }
        if (_horizontal) node.Props["axis"] = "horizontal";
        if (_selectionMode is not null) node.Props["selectionMode"] = _selectionMode;
        if (_onRefresh is not null) node.Props["refreshable"] = true;
        if (_onReachEnd is not null) node.Props["reachEndThreshold"] = _reachEndThreshold;

        // One action carries every List-level event (selection key / refresh / load-more), dispatched by
        // the value the host emits — so selection and refresh don't clobber each other's registration.
        if (_selectionMode is not null || _onRefresh is not null || _onReachEnd is not null)
            ctx.RegisterAction(node.Id, val =>
            {
                switch (val)
                {
                    case RefreshValue: _ = _onRefresh?.Invoke(); break;
                    case LoadMoreValue: _onReachEnd?.Invoke(); break;
                    default: if (val is { } key) _onSelect?.Invoke(key); break;
                }
            });

        // Header / rows (or empty) / footer are all ordered children — every host paints list children
        // top-to-bottom, so no host changes are needed. Slot children carry a "role" prop so the differ's
        // key-sequence check ignores them (keeping the keyed fast-path) and hosts can style them.
        var slot = 0;
        void Add(View view, string? role, string? key)
        {
            // Child ids stay positional (path + "." + slot) so every host's integer-indexed Find keeps
            // working; row identity for reconciliation rides along as the "key" prop.
            var child = view.ToNode(ctx, path + "." + slot++);
            if (role is not null) child.Props["role"] = role;
            if (key is not null) child.Props["key"] = key;
            if (key is not null && _isSelected?.Invoke(key) == true) child.Props["selected"] = true;
            node.Children.Add(child);
        }

        if (_header is not null) Add(_header, "header", null);

        var rows = _rows();
        if (rows.Count == 0 && _empty is not null)
            Add(_empty, "empty", null);
        else
            for (var i = 0; i < rows.Count; i++)
                Add(rows[i].View, null, rows[i].Key);

        if (_footer is not null) Add(_footer, "footer", null);

        return node;
    }
}
