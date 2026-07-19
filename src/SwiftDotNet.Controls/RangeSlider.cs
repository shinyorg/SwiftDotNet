namespace SwiftDotNet.Controls;

/// <summary>
/// A dual-thumb range slider — ported from Shiny's <c>RangeSlider</c>. A drag moves whichever thumb is
/// nearer the touch. Fixed-width track + F1 drag (no measured geometry). Binds low/high <c>State</c>s.
/// </summary>
public sealed class RangeSlider : View
{
    readonly State<double> _low, _high;
    readonly double _min, _max;
    double _width = 240;
    SwiftColor _accent = ControlPalette.Accent(PillType.Info);

    public RangeSlider(State<double> low, State<double> high, double min = 0, double max = 1)
    {
        _low = low;
        _high = high;
        _min = min;
        _max = max;
    }

    public RangeSlider Width(double width) { _width = width; return this; }
    public RangeSlider Accent(SwiftColor color) { _accent = color; return this; }

    double Frac(double v) => _max > _min ? Math.Clamp((v - _min) / (_max - _min), 0, 1) : 0;

    public override View Body
    {
        get
        {
            var lo = Frac(_low.Value);
            var hi = Frac(_high.Value);

            var track = new Group().Frame(_width, 4).Background(ControlPalette.Outline).CornerRadius(2);
            var fill = new Group().Frame(Math.Max(0.001, (hi - lo) * _width), 4).Background(_accent).CornerRadius(2)
                .Offset(lo * _width, 0);
            var lowThumb = Thumb(lo);
            var highThumb = Thumb(hi);

            return new ZStack(track, fill, lowThumb, highThumb)
                .Alignment(Alignment.Leading)
                .Frame(_width, 28)
                .OnDrag(info =>
                {
                    var f = Math.Clamp(info.LocationX / _width, 0, 1);
                    var value = _min + f * (_max - _min);
                    // Move whichever thumb is closer to the touch.
                    if (Math.Abs(f - lo) <= Math.Abs(f - hi))
                        _low.Value = Math.Min(value, _high.Value);
                    else
                        _high.Value = Math.Max(value, _low.Value);
                });
        }
    }

    View Thumb(double frac) =>
        new Circle()
            .Frame(24, 24)
            .ForegroundColor(SwiftColor.Hex("#FFFFFF"))
            .Border(ControlPalette.Outline, 1, cornerRadius: 12)
            .Shadow(3, SwiftColor.Hex("#000000"), 0, 1)
            .Offset(frac * _width - 12, 0);
}
