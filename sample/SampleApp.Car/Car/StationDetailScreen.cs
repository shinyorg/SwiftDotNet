using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

/// <summary>Station pane with reserve (confirmed by an alert) and favorite actions.</summary>
public sealed class StationDetailScreen(StationService stations, Station station) : StationScreen(stations)
{
    public override CarTemplate Build()
    {
        var available = Stations.Available(station);
        var reservedHere = Stations.ReservedStationId == station.Id;

        var reserve = reservedHere
            ? new CarAction("Release", Stations.CancelReservation, CarActionStyle.Destructive)
            : new CarAction("Reserve", () => ConfirmReservation(station), CarActionStyle.Primary) { IsEnabled = available > 0 };

        var favorite = Stations.IsFavorite(station)
            ? new CarAction("Unfavorite", () => Stations.ToggleFavorite(station)) { Image = CarIcons.Star }
            : new CarAction("Favorite", () => Stations.ToggleFavorite(station)) { Image = CarIcons.StarOutline };

        return new CarInfoTemplate
        {
            Title = station.Name,
            Layout = CarInfoLayout.TwoColumn,
            Items =
            [
                new CarInfoItem("Address", $"{station.Address} · {station.DistanceKm:0.0} km"),
                new CarInfoItem("Available", reservedHere ? $"{available}/{station.Total} · 1 held for you" : $"{available}/{station.Total}"),
                new CarInfoItem("Power", $"{station.PowerKw} kW · {station.Network}"),
                new CarInfoItem("Price", $"${station.PricePerKwh:0.00}/kWh")
            ],
            Actions = [reserve, favorite]
        };
    }
}
