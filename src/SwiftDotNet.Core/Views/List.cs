namespace SwiftDotNet;

/// <summary>
/// A scrolling collection, rendered as a native SwiftUI <c>List</c>. Compose rows directly, or
/// build them from data with <see cref="ForEach{T}"/>.
/// </summary>
public sealed class List : View
{
    readonly View[] _rows;

    public List(params View[] rows) => _rows = rows;

    /// <summary>Data-driven rows: <c>List.ForEach(items, x =&gt; new Text(x.Name))</c>.</summary>
    public static List ForEach<T>(IEnumerable<T> items, Func<T, View> row)
        => new(items.Select(row).ToArray());

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var node = ctx.NewNode("List", path);
        for (var i = 0; i < _rows.Length; i++)
            node.Children.Add(_rows[i].ToNode(ctx, path + "." + i));
        return node;
    }
}
