namespace SwiftDotNet.Controls;

/// <summary>
/// A custom track/fill/thumb slider — ported from Shiny's <c>Slider</c>. Built from an F1 drag over a
/// fixed-width track (the drag location maps straight to the value, so no measured geometry is needed).
/// Binds a <see cref="State{T}"/> of double in <c>[min, max]</c>.
/// </summary>
public sealed class Slider : View
{
    readonly State<double> _value;
    readonly double _min, _max;
    double _width = 240;
    SwiftColor _accent = ControlPalette.Accent(PillType.Info);

    public Slider(State<double> value, double min = 0, double max = 1)
    {
        _value = value;
        _min = min;
        _max = max;
    }

    public Slider Width(double width) { _width = width; return this; }
    public Slider Accent(SwiftColor color) { _accent = color; return this; }

    public override View Body
    {
        get
        {
            var frac = _max > _min ? Math.Clamp((_value.Value - _min) / (_max - _min), 0, 1) : 0;

            var track = new Group().Frame(_width, 4).Background(ControlPalette.Outline).CornerRadius(2);
            var fill = new Group().Frame(Math.Max(0.001, frac * _width), 4).Background(_accent).CornerRadius(2);
            var thumb = new Circle()
                .Frame(24, 24)
                .ForegroundColor(SwiftColor.Hex("#FFFFFF"))
                .Border(ControlPalette.Outline, 1, cornerRadius: 12)
                .Shadow(3, SwiftColor.Hex("#000000"), 0, 1)
                .Offset(frac * _width - 12, 0);

            return new ZStack(track, fill, thumb)
                .Alignment(Alignment.Leading)
                .Frame(_width, 28)
                .OnDrag(info =>
                {
                    var f = Math.Clamp(info.LocationX / _width, 0, 1);
                    _value.Value = _min + f * (_max - _min);
                });
        }
    }
}
