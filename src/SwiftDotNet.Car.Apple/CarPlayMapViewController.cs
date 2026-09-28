using CoreGraphics;
using SwiftDotNet.Graphics;
using UIKit;

namespace SwiftDotNet.CarPlay;

/// <summary>
/// Root view controller of the CarPlay <c>CPWindow</c> — the map base layer beneath <c>CPMapTemplate</c>.
/// Paints the active <see cref="CarMapTemplate.Draw"/> through <see cref="AppleCanvas"/>, the same renderer
/// seam the self-drawing engine uses, so a map drawn for Android Auto draws identically here.
/// </summary>
sealed class CarPlayMapViewController : UIViewController
{
    readonly CarMapView mapView = new() { BackgroundColor = UIColor.Black };

    public override void LoadView() => View = mapView;

    public CarMapTemplate? Model
    {
        get => mapView.Model;
        set
        {
            mapView.Model = value;
            mapView.SetNeedsDisplay();
        }
    }

    public void Redraw() => mapView.SetNeedsDisplay();

    sealed class CarMapView : UIView
    {
        readonly AppleFontProvider fonts = new();

        public CarMapTemplate? Model { get; set; }

        public override void Draw(CGRect rect)
        {
            if (Model is not { } model || UIGraphics.GetCurrentContext() is not { } ctx)
                return;
            var size = new Size((float)Bounds.Width, (float)Bounds.Height);
            model.Draw(new CarMapDrawContext(new AppleCanvas(ctx, Bounds), fonts, size));
        }
    }
}
