using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

/// <summary>SF Symbol (CarPlay) ↔ vector drawable (Android Auto, Platforms/Android/Resources/drawable) pairs.</summary>
static class CarIcons
{
    public static readonly SwiftColor Free = Color.Hex("#22C55E");
    public static readonly SwiftColor Busy = Color.Hex("#F59E0B");
    public static readonly SwiftColor Full = Color.Hex("#EF4444");
    public static readonly SwiftColor Accent = Color.Hex("#14B8A6");
    public static readonly SwiftColor Muted = Color.Hex("#9CA3AF");

    public static CarImage Bolt => CarImage.Symbol("bolt.fill", "ic_bolt");
    public static CarImage Explore => CarImage.Symbol("square.grid.2x2.fill", "ic_filter");
    public static CarImage Car => CarImage.Symbol("car.fill", "ic_car");
    public static CarImage Search => CarImage.Symbol("magnifyingglass", "ic_search");
    public static CarImage Map => CarImage.Symbol("map.fill", "ic_map");
    public static CarImage Nearby => CarImage.Symbol("mappin.and.ellipse", "ic_place");
    public static CarImage Star => CarImage.Symbol("star.fill", "ic_star");
    public static CarImage StarOutline => CarImage.Symbol("star", "ic_star_outline");
    public static CarImage ZoomIn => CarImage.Symbol("plus.magnifyingglass", "ic_zoom_in");
    public static CarImage ZoomOut => CarImage.Symbol("minus.magnifyingglass", "ic_zoom_out");
    public static CarImage Recenter => CarImage.Symbol("location.fill", "ic_recenter");
    public static CarImage Check => CarImage.Symbol("checkmark.circle.fill", "ic_check");

    public static SwiftColor AvailabilityColor(int available, int total) =>
        available == 0 ? Full : available * 3 < total ? Busy : Free;

    public static CarImage StationImage(StationService service, Station station) =>
        Bolt.WithTint(AvailabilityColor(service.Available(station), station.Total));

    public static string Describe(StationService service, Station station) =>
        $"{station.DistanceKm:0.0} km · {service.Available(station)}/{station.Total} free · {station.PowerKw} kW";
}
