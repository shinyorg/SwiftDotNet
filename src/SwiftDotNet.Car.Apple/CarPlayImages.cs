using Foundation;
using UIKit;

namespace SwiftDotNet.CarPlay;

static class CarPlayImages
{
    public static UIImage? ToUIImage(this CarImage? image)
    {
        if (image is null)
            return null;

        var ui = image switch
        {
            CarSymbolImage s => UIImage.GetSystemImage(s.IosSymbol),
            CarResourceImage r => UIImage.FromBundle(r.Name),
            CarBytesImage b => UIImage.LoadFromData(NSData.FromArray(b.Data)),
            _ => null
        };

        if (ui is not null && image.Tint is { } tint)
            ui = Bake(ui, ToUIColor(tint));

        return ui;
    }

    public static UIColor ToUIColor(SwiftColor color)
    {
        var c = CarColors.Resolve(color);
        return UIColor.FromRGBA(c.R, c.G, c.B, c.A);
    }

    /// <summary>
    /// CarPlay re-renders images in its own process and drops the tint of symbol images, so tinted images are
    /// rasterized with their color baked in.
    /// </summary>
    static UIImage Bake(UIImage image, UIColor color)
    {
        var tinted = image.ApplyTintColor(color, UIImageRenderingMode.AlwaysOriginal);
        var format = new UIGraphicsImageRendererFormat { Scale = UIScreen.MainScreen.Scale, Opaque = false };
        using var renderer = new UIGraphicsImageRenderer(tinted.Size, format);
        return renderer.CreateImage(_ => tinted.Draw(new CoreGraphics.CGPoint(0, 0))).ImageWithRenderingMode(UIImageRenderingMode.AlwaysOriginal);
    }
}
