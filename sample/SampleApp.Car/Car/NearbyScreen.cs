using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

/// <summary>
/// Host-drawn map with pins: CPPointOfInterestTemplate on CarPlay, PlaceListMapTemplate on Android Auto.
/// No OnSelect is set, so CarPlay shows its detail card with the two buttons and Android Auto presents an
/// action sheet with the same two actions.
/// </summary>
public sealed class NearbyScreen(StationService stations) : StationScreen(stations)
{
    public override CarTemplate Build() => new CarPointOfInterestTemplate
    {
        Title = "Nearby chargers",
        Points = Stations.Filtered().Select(s => new CarPointOfInterest
        {
            Title = s.Name,
            Subtitle = $"{Stations.Available(s)}/{s.Total} free · {s.PowerKw} kW",
            Summary = $"{s.Network} · ${s.PricePerKwh:0.00}/kWh",
            Latitude = s.Latitude,
            Longitude = s.Longitude,
            DistanceKm = s.DistanceKm,
            PinColor = CarIcons.AvailabilityColor(Stations.Available(s), s.Total),
            PrimaryAction = new CarAction("Reserve", () => ConfirmReservation(s), CarActionStyle.Primary) { IsEnabled = Stations.Available(s) > 0 },
            SecondaryAction = new CarAction("Details", () => ShowDetails(s))
        }).ToList()
    };
}
