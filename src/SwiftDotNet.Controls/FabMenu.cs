namespace SwiftDotNet.Controls;

/// <summary>One action in a <see cref="FabMenu"/>: an icon, a label, and what to run when tapped.</summary>
public sealed record FabMenuItem(string Icon, string Label, Action Action);

/// <summary>
/// An expanding speed-dial — a main <see cref="Fab"/> that toggles a column of labeled item buttons —
/// ported from Shiny's <c>FabMenu</c>. Pure composite over a bound open/closed <c>State</c>; the main
/// icon rotates (F4) as it opens. Bottom-align it within your layout.
/// </summary>
public sealed class FabMenu : View
{
    readonly FabMenuItem[] _items;
    readonly State<bool> _open = State(false);
    string _icon = "plus";
    SwiftColor _background = ControlPalette.Accent(PillType.Info);

    public FabMenu(params FabMenuItem[] items) => _items = items;

    public FabMenu Icon(string icon) { _icon = icon; return this; }
    public FabMenu Background(SwiftColor color) { _background = color; return this; }

    public override View Body
    {
        get
        {
            var rows = new List<View>();
            if (_open.Value)
            {
                foreach (var item in _items)
                {
                    var captured = item;
                    var label = new Text(item.Label)
                        .Font(Font.Caption)
                        .ForegroundColor(ControlPalette.OnSurface)
                        .Padding(horizontal: 10, vertical: 6)
                        .Background(ControlPalette.Surface)
                        .CornerRadius(8)
                        .Shadow(3, SwiftColor.Hex("#000000"), 0, 1);

                    var mini = new Fab(item.Icon, () => { captured.Action(); _open.Value = false; })
                        .Size(44)
                        .Background(_background);

                    rows.Add(new HStack(label, mini).Spacing(10).Alignment(VerticalAlignment.Center));
                }
            }

            var main = new Fab(_icon, () => _open.Value = !_open.Value)
                .Background(_background);
            var mainView = ((View)main).Rotation(_open.Value ? 45 : 0);

            rows.Add(mainView);
            return new VStack(rows.ToArray()).Spacing(12).Alignment(HorizontalAlignment.Trailing);
        }
    }
}
