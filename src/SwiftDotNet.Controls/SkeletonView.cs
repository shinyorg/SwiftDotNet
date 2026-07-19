namespace SwiftDotNet.Controls;

/// <summary>
/// A shimmer placeholder shown while content loads — ported from Shiny's <c>SkeletonView</c>. Pure
/// composite: a rounded rectangle filled with a shimmer gradient (F5), gently pulsing via F4's repeating
/// animation. Compose several to skeleton out a card or list row.
/// </summary>
public sealed class SkeletonView : View
{
    double _width;
    double _height = 16;
    double _cornerRadius = 8;

    public SkeletonView(double width = 0, double height = 16)
    {
        _width = width;
        _height = height;
    }

    public SkeletonView Size(double width, double height) { _width = width; _height = height; return this; }
    public SkeletonView CornerRadius(double radius) { _cornerRadius = radius; return this; }

    public override View Body
    {
        get
        {
            var shimmer = new LinearGradient(
                0,
                new GradientStop(ControlPalette.Shimmer, 0),
                new GradientStop(ControlPalette.ShimmerHighlight, 0.5),
                new GradientStop(ControlPalette.Shimmer, 1));

            // A plain container (not a shape) so the gradient fills via the background decoration — shape
            // views fill with ForegroundColor and would paint over a background gradient.
            var rect = new Group().Background(shimmer).CornerRadius(_cornerRadius);
            var sized = _width > 0 ? rect.Frame(_width, _height) : rect.Frame(height: _height);
            // Best-effort pulse: self-driving loops animate opacity on backends that support them (Web);
            // a static shimmer fill elsewhere still reads as a loading placeholder.
            return sized.Animation(Anim.EaseInOut(1.0).Repeating(autoreverse: true), on: true);
        }
    }
}
