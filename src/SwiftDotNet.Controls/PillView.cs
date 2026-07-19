namespace SwiftDotNet.Controls;

/// <summary>
/// A status chip — a rounded, tinted capsule with a short label — ported from Shiny's <c>PillView</c>.
/// A pure composite (a <see cref="Text"/> with padding + tinted background + border), so it renders on
/// every backend with no native code. The <see cref="PillType"/> picks the semantic color set.
/// </summary>
public sealed class PillView : View
{
    string _text;
    PillType _type;

    public PillView(string text = "", PillType type = PillType.None)
    {
        _text = text;
        _type = type;
    }

    /// <summary>The pill label.</summary>
    public PillView Text(string text) { _text = text; return this; }

    /// <summary>The semantic role (drives background/text/border colors).</summary>
    public PillView Type(PillType type) { _type = type; return this; }

    public override View Body
    {
        get
        {
            var (bg, fg, accent) = ControlPalette.Pill(_type);
            return new Text(_text)
                .Font(Font.Caption)
                .ForegroundColor(fg)
                .Padding(horizontal: 12, vertical: 4)
                .Background(bg)
                .CornerRadius(12)
                .Border(accent, 1, cornerRadius: 12);
        }
    }
}
