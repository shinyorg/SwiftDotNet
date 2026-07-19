namespace SwiftDotNet.Controls;

/// <summary>
/// A duration picker — hours / minutes / seconds columns with +/- steppers — ported (in reduced form)
/// from Shiny's <c>DurationPicker</c>. Pure composite bound to a <see cref="State{T}"/> of
/// <see cref="TimeSpan"/>. Configure which columns show via the constructor flags.
/// </summary>
public sealed class DurationPicker : View
{
    readonly State<TimeSpan> _value;
    readonly bool _hours, _minutes, _seconds;

    public DurationPicker(State<TimeSpan> value, bool hours = true, bool minutes = true, bool seconds = false)
    {
        _value = value;
        _hours = hours;
        _minutes = minutes;
        _seconds = seconds;
    }

    public override View Body
    {
        get
        {
            var cols = new List<View>();
            if (_hours) cols.Add(Column("Hours", _value.Value.Hours, d => Bump(TimeSpan.FromHours(d))));
            if (_minutes) cols.Add(Column("Min", _value.Value.Minutes, d => Bump(TimeSpan.FromMinutes(d))));
            if (_seconds) cols.Add(Column("Sec", _value.Value.Seconds, d => Bump(TimeSpan.FromSeconds(d))));
            return new HStack(cols.ToArray()).Spacing(20).Alignment(VerticalAlignment.Center);
        }
    }

    void Bump(TimeSpan delta)
    {
        var next = _value.Value + delta;
        if (next < TimeSpan.Zero) next = TimeSpan.Zero;
        _value.Value = next;
    }

    static View Column(string label, int value, Action<int> step) =>
        new VStack(
                new Button("▲", () => step(+1)),
                new Text(value.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)).Font(Font.Title),
                new Button("▼", () => step(-1)),
                new Text(label).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant))
            .Spacing(6)
            .Alignment(HorizontalAlignment.Center);
}
