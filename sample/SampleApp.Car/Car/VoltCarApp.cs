using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

/// <summary>The car experience. One class, both CarPlay and Android Auto.</summary>
public sealed class VoltCarApp(StationService stations) : CarApp
{
    public override CarScreen CreateRootScreen(IServiceProvider services) => new RootScreen(stations);
}

/// <summary>
/// Base for screens that re-render whenever the shared <see cref="StationService"/> changes
/// (phone edits, reservations, the live availability feed).
/// </summary>
public abstract class StationScreen(StationService stations) : CarScreen
{
    protected StationService Stations { get; } = stations;

    protected override void OnAttached() => Stations.Changed += OnStationsChanged;

    protected override void OnDetached() => Stations.Changed -= OnStationsChanged;

    void OnStationsChanged(object? sender, EventArgs e) => Invalidate(); // thread-safe; marshals to the car UI thread

    protected void ShowDetails(Station station) => Navigator.Push(new StationDetailScreen(Stations, station));

    protected void ConfirmReservation(Station station) => Navigator.Present(new CarAlertTemplate
    {
        Title = $"Hold a charger at {station.Name} for 15 min?",
        Actions =
        [
            new CarAction("Reserve", () => Stations.Reserve(station), CarActionStyle.Primary),
            new CarAction("Cancel", style: CarActionStyle.Cancel)
        ]
    });

    protected CarListItem StationRow(Station station) => new()
    {
        Id = station.Id,
        Title = station.Name,
        Detail = CarIcons.Describe(Stations, station),
        Image = CarIcons.StationImage(Stations, station),
        Accessory = CarAccessory.Disclosure,
        OnSelect = () => ShowDetails(station)
    };
}
