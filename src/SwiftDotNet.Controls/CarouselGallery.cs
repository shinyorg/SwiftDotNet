namespace SwiftDotNet.Controls;

/// <summary>
/// A swipeable, paged gallery with page dots — ported (as a composite) from Shiny's <c>CarouselGallery</c>.
/// Wraps the built-in paged <see cref="TabView"/>, so the swipe paging and indicator are native. Bind the
/// current page to a <c>State</c>; use <see cref="Images"/> for the common photo-carousel case (F3 raster).
/// </summary>
public sealed class CarouselGallery : View
{
    readonly View[] _pages;
    readonly State<int> _index;
    bool _hideIndicator;
    double? _height;

    public CarouselGallery(State<int> index, params View[] pages)
    {
        _index = index;
        _pages = pages;
    }

    /// <summary>A photo carousel from image URLs (each filling the page).</summary>
    public static CarouselGallery Images(State<int> index, params string[] urls) =>
        new(index, urls.Select(u => (View)Image.FromUrl(u).ContentMode(ImageContentMode.Fill)).ToArray());

    public CarouselGallery HideIndicator() { _hideIndicator = true; return this; }
    public CarouselGallery Height(double height) { _height = height; return this; }

    public override View Body
    {
        get
        {
            var tv = new TabView(_pages).Paged().SelectedIndex(_index);
            if (_hideIndicator) tv = tv.HidePageIndicator();
            return _height is { } h ? (View)tv.Frame(height: h) : tv;
        }
    }
}
