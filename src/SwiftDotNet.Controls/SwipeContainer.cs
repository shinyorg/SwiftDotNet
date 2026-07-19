namespace SwiftDotNet.Controls;

/// <summary>A trailing swipe action: a label, a tint, and what to run when tapped.</summary>
public sealed record SwipeAction(string Label, SwiftColor Color, Action Action);

/// <summary>
/// Wraps a row so a left-swipe reveals trailing action buttons (swipe-to-delete) — the F7(b) capability,
/// built as a pure F1-drag composite (no backend changes). The content shifts under the drag and snaps
/// open/closed on release; tapping an action runs it and closes. Give it a <see cref="RowHeight"/> that
/// matches the wrapped row.
/// </summary>
public sealed class SwipeContainer : View
{
    readonly View _content;
    readonly SwipeAction[] _actions;
    readonly State<double> _offset = State(0.0);
    double _actionWidth = 84;
    double _rowHeight = 44;

    public SwipeContainer(View content, params SwipeAction[] trailingActions)
    {
        _content = content;
        _actions = trailingActions;
    }

    public SwipeContainer ActionWidth(double width) { _actionWidth = width; return this; }
    public SwipeContainer RowHeight(double height) { _rowHeight = height; return this; }

    public override View Body
    {
        get
        {
            var revealed = _actions.Length * _actionWidth;

            // Action buttons, pinned to the trailing edge behind the content.
            var buttons = new View[_actions.Length];
            for (var i = 0; i < _actions.Length; i++)
            {
                var a = _actions[i];
                buttons[i] = new ZStack(new Text(a.Label).Font(Font.Caption).ForegroundColor(SwiftColor.Hex("#FFFFFF")))
                    .Frame(_actionWidth, _rowHeight)
                    .Background(a.Color)
                    .OnTapGesture(() => { a.Action(); _offset.Value = 0; });
            }
            var actionLayer = new HStack(new Spacer(), new HStack(buttons).Spacing(0)).Spacing(0);

            // Content on top, shifted by the drag; an opaque surface so it hides the buttons when closed.
            var top = new ZStack(_content)
                .Frame(height: _rowHeight)
                .Background(ControlPalette.Surface)
                .Offset(_offset.Value, 0)
                .OnDrag(info =>
                {
                    if (info.Phase == GesturePhase.Ended)
                        _offset.Value = _offset.Value < -revealed / 2 ? -revealed : 0;   // snap
                    else
                        _offset.Value = Math.Clamp(info.TranslationX, -revealed, 0);
                });

            return new ZStack(actionLayer, top);
        }
    }
}
