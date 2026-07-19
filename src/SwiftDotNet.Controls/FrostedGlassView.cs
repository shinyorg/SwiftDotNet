namespace SwiftDotNet.Controls;

/// <summary>
/// A frosted-glass panel — content over a blurred, translucent backdrop — ported from Shiny's
/// <c>FrostedGlassView</c>. Uses F6's <c>.Material</c>: a real backdrop blur on Web/SwiftUI and a
/// translucent tint elsewhere. Pure composite.
/// </summary>
public sealed class FrostedGlassView : View
{
    readonly View _content;
    MaterialStyle _style = MaterialStyle.Regular;
    double _cornerRadius = 16;
    bool _dark;

    public FrostedGlassView(View content) => _content = content;

    public FrostedGlassView Style(MaterialStyle style) { _style = style; return this; }
    public FrostedGlassView CornerRadius(double radius) { _cornerRadius = radius; return this; }
    public FrostedGlassView Dark(bool dark = true) { _dark = dark; return this; }

    public override View Body =>
        new Group(_content)
            .Padding(16)
            .Material(_style, _dark)
            .CornerRadius(_cornerRadius);
}
