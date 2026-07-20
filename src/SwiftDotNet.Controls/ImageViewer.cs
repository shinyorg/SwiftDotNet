namespace SwiftDotNet.Controls;

/// <summary>
/// A tappable thumbnail that opens a full-screen, pinch-to-zoom / pan image viewer — ported from Shiny's
/// <c>ImageViewer</c>. Uses F3 raster images, F1 pinch+drag, and the F2 <see cref="Overlay"/> layer.
/// Requires an <see cref="OverlayHost"/> root. Double-tap or tap the backdrop to close.
/// </summary>
public sealed class ImageViewer : View
{
    readonly string _kind;   // "url" | "file" | "bytes"
    readonly string _value;
    double _thumbSize = 120;

    ImageViewer(string kind, string value) { _kind = kind; _value = value; }

    public static ImageViewer FromUrl(string url) => new("url", url);
    public static ImageViewer FromFile(string path) => new("file", path);
    public static ImageViewer FromBytes(byte[] bytes) => new("bytes", Convert.ToBase64String(bytes));

    public ImageViewer ThumbnailSize(double size) { _thumbSize = size; return this; }

    internal Image BuildImage() => _kind switch
    {
        "url" => Image.FromUrl(_value),
        "file" => Image.FromFile(_value),
        _ => Image.FromBytesBase64(_value),
    };

    public override View Body =>
        // A neutral placeholder sits *behind* the image, so a slow/failed load shows an image well
        // instead of the bare SF-symbol fallback glyph.
        new ZStack(
                new ZStack(new Text("🖼").Font(Font.Title).Opacity(0.35))
                    .Frame(_thumbSize, _thumbSize)
                    .Background(ControlPalette.SurfaceVariant)
                    .CornerRadius(10),
                BuildImage()
                    .ContentMode(ImageContentMode.Fill)
                    .Frame(_thumbSize, _thumbSize)
                    .CornerRadius(10))
            .OnTapGesture(Open);

    void Open()
    {
        string id = "";
        var full = new FullScreenImageView(_kind, _value, () => Overlay.Dismiss(id));
        id = Overlay.Present(full, new OverlayOptions
        {
            Position = OverlayPosition.Center,
            DimBackground = false,   // the viewer paints its own full-black backdrop
            TapOutsideToDismiss = false,
        });
    }
}

/// <summary>The full-screen zoom/pan surface presented by <see cref="ImageViewer"/>.</summary>
sealed class FullScreenImageView : View
{
    readonly string _kind, _value;
    readonly Action _dismiss;
    readonly State<double> _scale = State(1.0);
    readonly State<double> _ox = State(0.0), _oy = State(0.0);
    readonly State<double> _baseX = State(0.0), _baseY = State(0.0);

    public FullScreenImageView(string kind, string value, Action dismiss)
    {
        _kind = kind;
        _value = value;
        _dismiss = dismiss;
    }

    Image BuildImage() => _kind switch
    {
        "url" => Image.FromUrl(_value),
        "file" => Image.FromFile(_value),
        _ => Image.FromBytesBase64(_value),
    };

    public override View Body
    {
        get
        {
            var backdrop = new Rectangle().ForegroundColor(SwiftColor.Hex("#000000")).OnTapGesture(_dismiss);

            var image = BuildImage()
                .ContentMode(ImageContentMode.Fit)
                .ScaleEffect(_scale.Value)
                .Offset(_ox.Value, _oy.Value)
                .OnMagnify(factor => _scale.Value = Math.Clamp(factor, 1, 5))
                .OnDrag(info =>
                {
                    _ox.Value = _baseX.Value + info.TranslationX;
                    _oy.Value = _baseY.Value + info.TranslationY;
                    if (info.Phase == GesturePhase.Ended) { _baseX.Value = _ox.Value; _baseY.Value = _oy.Value; }
                });

            return new ZStack(backdrop, image);
        }
    }
}
