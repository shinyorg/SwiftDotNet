namespace SwiftDotNet.Controls;

/// <summary>
/// A vertical list whose rows can be dragged to reorder — the F7(c) capability built as a pure F1-drag
/// composite (no backend work). Bind a <see cref="State{T}"/> of list; dragging a row vertically and
/// releasing moves it by the number of row-heights travelled. Uses a fixed <see cref="RowHeight"/> (no
/// per-row measurement — Plan-1 F11), which keeps the drag→index mapping exact.
/// </summary>
public sealed class ReorderableList<T> : View
{
    readonly State<List<T>> _items;
    readonly Func<T, View> _row;
    double _rowHeight = 56;

    readonly State<int> _dragIndex = State(-1);
    readonly State<double> _dragOffset = State(0.0);

    public ReorderableList(State<List<T>> items, Func<T, View> row)
    {
        _items = items;
        _row = row;
    }

    public ReorderableList<T> RowHeight(double height) { _rowHeight = height; return this; }

    public override View Body
    {
        get
        {
            var items = _items.Value;
            var rows = new View[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                var index = i;
                var item = items[i];
                var isDragging = _dragIndex.Value == index;

                var row = new HStack(
                        new Text("≡").ForegroundColor(ControlPalette.OnSurfaceVariant),
                        _row(item),
                        new Spacer())
                    .Spacing(12)
                    .Padding(horizontal: 12, vertical: 0)
                    .Frame(height: _rowHeight)
                    .Background(isDragging ? ControlPalette.SurfaceVariant : ControlPalette.Surface)
                    .Border(ControlPalette.Outline, isDragging ? 2 : 1)
                    .Offset(0, isDragging ? _dragOffset.Value : 0)
                    .OnDrag(info =>
                    {
                        if (info.Phase == GesturePhase.Began)
                        {
                            _dragIndex.Value = index;
                            _dragOffset.Value = 0;
                        }
                        else if (info.Phase == GesturePhase.Ended)
                        {
                            var delta = (int)Math.Round(info.TranslationY / _rowHeight);
                            var target = Math.Clamp(index + delta, 0, items.Count - 1);
                            if (target != index)
                            {
                                var list = new List<T>(items);
                                list.RemoveAt(index);
                                list.Insert(target, item);
                                _items.Value = list;
                            }
                            _dragIndex.Value = -1;
                            _dragOffset.Value = 0;
                        }
                        else
                        {
                            _dragOffset.Value = info.TranslationY;
                        }
                    });

                rows[i] = row;
            }
            return new VStack(rows).Spacing(0).Alignment(HorizontalAlignment.Leading);
        }
    }
}
