namespace SwiftDotNet.Controls;

/// <summary>
/// A PIN entry — a row of filled/empty boxes backed by a number field — ported from Shiny's
/// <c>SecurityPin</c>. Binds a <see cref="State{T}"/> of string (the digits); the app observes it and
/// acts when its length reaches <see cref="Length"/>. Uses F9's number keyboard + max-length.
/// </summary>
public sealed class SecurityPin : View
{
    readonly State<string> _pin;
    int _length = 4;

    public SecurityPin(State<string> pin, int length = 4)
    {
        _pin = pin;
        _length = length;
    }

    public SecurityPin Length(int length) { _length = length; return this; }

    public override View Body
    {
        get
        {
            var filled = _pin.Value.Length;
            var boxes = new View[_length];
            for (var i = 0; i < _length; i++)
            {
                var isFilled = i < filled;
                boxes[i] = new ZStack(new Text(isFilled ? "●" : "").Font(Font.Title).ForegroundColor(ControlPalette.OnSurface))
                    .Frame(44, 54)
                    .Background(ControlPalette.SurfaceVariant)
                    .CornerRadius(10)
                    .Border(i == filled ? ControlPalette.Accent(PillType.Info) : ControlPalette.Outline, 2, cornerRadius: 10);
            }

            return new VStack(
                    new HStack(boxes).Spacing(10),
                    new TextField("Enter PIN", _pin).Keyboard(KeyboardType.Number).MaxLength(_length))
                .Spacing(14)
                .Alignment(HorizontalAlignment.Center);
        }
    }
}
