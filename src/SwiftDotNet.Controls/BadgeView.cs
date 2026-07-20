namespace SwiftDotNet.Controls;

/// <summary>Which corner a <see cref="BadgeView"/> pins its badge to.</summary>
public enum BadgePosition { TopEnd, TopStart, BottomEnd, BottomStart }

/// <summary>
/// Wraps arbitrary content with a small corner badge — a dot, a count (with <c>N+</c> overflow), or a
/// short label — ported from Shiny's <c>BadgeView</c>. Pure composite: a <see cref="ZStack"/> of the
/// content plus the badge, pinned and offset to overhang the corner. Optional <see cref="Pulsing"/> ride
/// F4's repeating animation.
/// </summary>
public sealed class BadgeView : View
{
    readonly View _content;
    string? _text;
    int? _count;
    int _maxCount = 99;
    bool _isDot;
    bool _pulsing;
    BadgePosition _position = BadgePosition.TopEnd;
    SwiftColor _color = ControlPalette.Accent(PillType.Critical);

    public BadgeView(View content) => _content = content;

    /// <summary>A short text badge (e.g. "NEW").</summary>
    public BadgeView Text(string text) { _text = text; _count = null; _isDot = false; return this; }

    /// <summary>A numeric badge; values above <see cref="MaxCount"/> render as "N+".</summary>
    public BadgeView Count(int count) { _count = count; _text = null; _isDot = false; return this; }

    /// <summary>The overflow ceiling for <see cref="Count"/> (default 99).</summary>
    public BadgeView MaxCount(int max) { _maxCount = max; return this; }

    /// <summary>A plain dot with no content.</summary>
    public BadgeView Dot(bool dot = true) { _isDot = dot; return this; }

    public BadgeView Position(BadgePosition position) { _position = position; return this; }
    public BadgeView Color(SwiftColor color) { _color = color; return this; }
    public BadgeView Pulsing(bool pulsing = true) { _pulsing = pulsing; return this; }

    public override View Body
    {
        get
        {
            var badge = BuildBadge();
            // Overhang the corner: pin to the corner then nudge outward by half the badge.
            var (align, dx, dy) = _position switch
            {
                BadgePosition.TopStart => (Alignment.TopLeading, -10.0, -10.0),
                BadgePosition.BottomEnd => (Alignment.BottomTrailing, 10.0, 10.0),
                BadgePosition.BottomStart => (Alignment.BottomLeading, -10.0, 10.0),
                _ => (Alignment.TopTrailing, 10.0, -10.0),
            };
            return new ZStack(_content, badge.Offset(dx, dy)).Alignment(align);
        }
    }

    View BuildBadge()
    {
        if (_isDot)
        {
            var dot = new Circle().Frame(10, 10).ForegroundColor(_color);
            return _pulsing ? Pulse(dot) : dot;
        }

        var label = _count is { } c ? (c > _maxCount ? _maxCount + "+" : c.ToString()) : (_text ?? "");
        var pill = new Text(label)
            .Font(Font.Caption)
            .ForegroundColor(SwiftColor.Hex("#FFFFFF"))
            .Padding(horizontal: 6, vertical: 2)
            .Background(_color)
            .CornerRadius(9);
        return _pulsing ? Pulse(pill) : pill;
    }

    static T Pulse<T>(T view) where T : View =>
        view.ScaleEffect(1.0).Animation(Anim.EaseInOut(0.7).Repeating(autoreverse: true), on: true);
}
