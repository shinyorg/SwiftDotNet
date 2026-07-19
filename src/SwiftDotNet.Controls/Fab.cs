namespace SwiftDotNet.Controls;

/// <summary>
/// A circular floating action button — an SF-Symbol icon on a tinted, shadowed disc — ported from Shiny's
/// <c>Fab</c>. Pure composite: a <see cref="ZStack"/> sized to a circle with a tap handler.
/// </summary>
public sealed class Fab : View
{
    readonly string _icon;
    readonly Action _onTap;
    double _size = 56;
    SwiftColor _background = ControlPalette.Accent(PillType.Info);
    SwiftColor _foreground = SwiftColor.Hex("#FFFFFF");
    bool _shadow = true;

    public Fab(string icon, Action onTap)
    {
        _icon = icon;
        _onTap = onTap;
    }

    public Fab Size(double size) { _size = size; return this; }
    public Fab Background(SwiftColor color) { _background = color; return this; }
    public Fab Foreground(SwiftColor color) { _foreground = color; return this; }
    public Fab Shadow(bool shadow) { _shadow = shadow; return this; }

    public override View Body
    {
        get
        {
            var disc = new ZStack(Image.System(_icon).ForegroundColor(_foreground))
                .Frame(_size, _size)
                .Background(_background)
                .CornerRadius(_size / 2);
            if (_shadow) disc = disc.Shadow(6, SwiftColor.Hex("#000000"), 0, 3);
            return disc.OnTapGesture(_onTap);
        }
    }
}
