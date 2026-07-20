namespace SwiftDotNet.Controls;

/// <summary>
/// A frosted-glass panel — content over a blurred, translucent backdrop — ported from Shiny's
/// <c>FrostedGlassView</c>. Uses F6's <c>.Material</c>: a real backdrop blur on Web/SwiftUI and a
/// translucent tint elsewhere.
///
/// <para><b>It only reads as glass over something.</b> A material blurs what is *behind* it, so placing one
/// on a plain background looks like an empty box. Put it over an image, a gradient, or scrolling content —
/// see <c>Over(...)</c>, which stacks the panel on a backdrop for you.</para>
/// </summary>
public sealed class FrostedGlassView : View
{
    readonly View _content;
    View? _backdrop;
    MaterialStyle _style = MaterialStyle.Regular;
    double _cornerRadius = 16;
    bool _dark;

    public FrostedGlassView(View content) => _content = content;

    /// <summary>Stacks the glass panel over <paramref name="backdrop"/> so the blur has something to work on.</summary>
    public FrostedGlassView Over(View backdrop) { _backdrop = backdrop; return this; }

    public FrostedGlassView Style(MaterialStyle style) { _style = style; return this; }
    public FrostedGlassView CornerRadius(double radius) { _cornerRadius = radius; return this; }
    public FrostedGlassView Dark(bool dark = true) { _dark = dark; return this; }

    public override View Body
    {
        get
        {
            // A hairline highlight border sells the glass edge even where the backend can only tint.
            var panel = new ZStack(_content)
                .Padding(16)
                .Material(_style, _dark)
                .CornerRadius(_cornerRadius)
                .Border(_dark ? SwiftColor.Hex("#5A5A60") : SwiftColor.Hex("#FFFFFF"), 1, cornerRadius: _cornerRadius);

            return _backdrop is { } b ? new ZStack(b, panel) : panel;
        }
    }
}
