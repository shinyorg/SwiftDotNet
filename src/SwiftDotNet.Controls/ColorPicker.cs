using System.Globalization;

namespace SwiftDotNet.Controls;

/// <summary>
/// An in-app color picker — a rainbow <b>hue</b> bar plus a <b>brightness</b> bar, with a live swatch and
/// hex readout. Ported (in reduced form) from Shiny's <c>ColorPicker</c>. Built from F5 gradients + F1
/// drags; each bar's fixed width maps the drag location straight to a value with no measured geometry.
/// (A full saturation/brightness *field* needs alpha-capable gradient stops — a small F5 follow-up.)
/// </summary>
public sealed class ColorPicker : View
{
    readonly State<double> _hue;          // 0–360, two-way bound
    readonly State<double> _brightness;   // 0–1
    Action<string>? _onHex;
    double _width = 240;

    public ColorPicker(State<double> hue, State<double>? brightness = null)
    {
        _hue = hue;
        _brightness = brightness ?? new State<double>(1.0);
    }

    /// <summary>Fires with the selected color as a <c>#RRGGBB</c> string as the hue changes.</summary>
    public ColorPicker OnColorChanged(Action<string> onHex) { _onHex = onHex; return this; }

    public ColorPicker Width(double width) { _width = width; return this; }

    public override View Body
    {
        get
        {
            var rainbow = new LinearGradient(0,
                new GradientStop(SwiftColor.Hex("#FF0000"), 0.0),
                new GradientStop(SwiftColor.Hex("#FFFF00"), 0.17),
                new GradientStop(SwiftColor.Hex("#00FF00"), 0.33),
                new GradientStop(SwiftColor.Hex("#00FFFF"), 0.5),
                new GradientStop(SwiftColor.Hex("#0000FF"), 0.67),
                new GradientStop(SwiftColor.Hex("#FF00FF"), 0.83),
                new GradientStop(SwiftColor.Hex("#FF0000"), 1.0));

            // Plain containers fill via the background decoration; shape views fill via ForegroundColor
            // and would paint over a background gradient.
            var bar = new ZStack()
                .Background(rainbow)
                .Frame(_width, 24)
                .CornerRadius(12)
                .OnDrag(info =>
                {
                    var frac = Math.Clamp(info.LocationX / _width, 0, 1);
                    _hue.Value = frac * 360;
                    _onHex?.Invoke(HsbToHex(_hue.Value, 1, _brightness.Value));
                });

            var thumbX = _hue.Value / 360.0 * _width;
            var thumb = new Circle()
                .Frame(22, 22)
                .ForegroundColor(SwiftColor.Hex("#FFFFFF"))
                .Border(ControlPalette.Outline, 2, cornerRadius: 11)
                .Offset(thumbX - 11, 0);

            // Brightness: black → the fully-saturated hue, dragged the same way.
            var brightGradient = new LinearGradient(0,
                new GradientStop(SwiftColor.Hex("#000000"), 0),
                new GradientStop(SwiftColor.Hex(HsbToHex(_hue.Value, 1, 1)), 1));

            var brightBar = new ZStack()
                .Background(brightGradient)
                .Frame(_width, 24)
                .CornerRadius(12)
                .OnDrag(info => _brightness.Value = Math.Clamp(info.LocationX / _width, 0, 1));

            var brightThumb = new Circle()
                .Frame(22, 22)
                .ForegroundColor(SwiftColor.Hex("#FFFFFF"))
                .Border(ControlPalette.Outline, 2, cornerRadius: 11)
                .Offset(_brightness.Value * _width - 11, 0);

            var hex = HsbToHex(_hue.Value, 1, _brightness.Value);
            var swatch = new ZStack()
                .Frame(_width, 44)
                .Background(SwiftColor.Hex(hex))
                .CornerRadius(10)
                .Border(ControlPalette.Outline, 1, cornerRadius: 10);

            return new VStack(
                    new ZStack(bar, thumb).Alignment(Alignment.Leading),
                    new ZStack(brightBar, brightThumb).Alignment(Alignment.Leading),
                    swatch,
                    new Text(hex).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant))
                .Spacing(12);
        }
    }

    /// <summary>HSB→#RRGGBB. h in 0–360, s/b in 0–1.</summary>
    public static string HsbToHex(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60.0) % 2 - 1));
        var m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return "#" + Byte(r + m) + Byte(g + m) + Byte(b + m);
    }

    static string Byte(double v) => ((int)Math.Round(Math.Clamp(v, 0, 1) * 255)).ToString("X2", CultureInfo.InvariantCulture);
}
