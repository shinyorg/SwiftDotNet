namespace SampleApp.Car;

// Inside the namespace so SwiftDotNet.Button wins over the Android.Widget global using the Android head gets.
using SampleApp.Car.Services;
using SwiftDotNet;

/// <summary>
/// The phone UI, over the same <see cref="StationService"/> the car screens use: toggling a favorite, a
/// filter, the battery or a reservation here re-renders the car UI, and vice versa.
/// </summary>
/// <remarks>
/// The service is plain C# with a <c>Changed</c> event, not <see cref="State{T}"/>, because the car screens
/// (which are not views) watch it too. Bumping <see cref="_version"/> from that event is all the phone needs:
/// assigning a <see cref="State{T}"/> from the feed's timer thread is safe — the render is marshalled to the
/// UI thread for us.
/// </remarks>
public sealed class PhoneView : View
{
    readonly StationService _stations;
    readonly ICarConnection _car;
    readonly State<int> _version = State(0);

    public PhoneView(StationService stations, ICarConnection car)
    {
        _stations = stations;
        _car = car;
        _stations.Changed += (_, _) => _version.Value++;
        _car.StateChanged += (_, _) => _version.Value++;
    }

    public override View? Body =>
        new NavigationStack(
            new Form(
                new Section("Car",
                    new Text(CarStatus()).Font(Font.Caption),
                    new Text(_stations.ReservedStation is { } r
                        ? $"Reserved: {r.Name} until {_stations.ReservedUntil:t}"
                        : "No reservation").Font(Font.Caption)
#if ANDROID
                    , new Button("Validate Android Auto templates", () => _check.Value = CarTemplateCheck.Run(Service<IServiceProvider>()))
                    , new Text(_check.Value).Font(Font.Caption)
#endif
                ),
                new Section("Battery",
                    new HStack(
                        new Text($"{_stations.BatteryPercent}% · {_stations.RangeKm} km"),
                        new Spacer(),
                        new Button("−10", () => _stations.SetBattery(_stations.BatteryPercent - 10)),
                        new Button("+10", () => _stations.SetBattery(_stations.BatteryPercent + 10)))),
                new Section("Filters",
                    Filter("Fast (50 kW+)", _stations.FastOnly, () => _stations.SetFilters(fastOnly: !_stations.FastOnly)),
                    Filter("Available now", _stations.AvailableOnly, () => _stations.SetFilters(availableOnly: !_stations.AvailableOnly)),
                    Filter("Favorites only", _stations.FavoritesOnly, () => _stations.SetFilters(favoritesOnly: !_stations.FavoritesOnly))),
                new Section("Stations", _stations.Filtered().Select(StationRow).ToArray())
            ).NavigationTitle("VoltFinder"));

#if ANDROID
    readonly State<string> _check = State("");
#endif

    string CarStatus() => _car.IsConnected
        ? $"🚗 Connected to {(_car.Platform == CarPlatform.CarPlay ? "CarPlay" : "Android Auto")} · {_car.Navigator?.Stack.Count} screen(s) deep"
        : "🚗 No car connected — open the CarPlay simulator or the Android Auto DHU";

    static View Filter(string title, bool on, Action toggle) =>
        new HStack(new Text(title), new Spacer(), new Button(on ? "On" : "Off", toggle));

    View StationRow(Station station)
    {
        var free = _stations.Available(station);
        var reservedHere = _stations.ReservedStationId == station.Id;
        return new HStack(
            new Button(_stations.IsFavorite(station) ? "★" : "☆", () => _stations.ToggleFavorite(station)),
            new VStack(
                new Text(station.Name).Font(Font.Headline),
                new Text(CarIcons.Describe(_stations, station)).Font(Font.Caption)
                    .ForegroundColor(CarIcons.AvailabilityColor(free, station.Total))),
            new Spacer(),
            new Button(reservedHere ? "Release" : "Reserve", () =>
            {
                if (reservedHere)
                    _stations.CancelReservation();
                else
                    _stations.Reserve(station);
            }).Disabled(!reservedHere && free == 0));
    }
}
