namespace SampleApp.Car.Services;

public sealed record Station(
    string Id,
    string Name,
    string Network,
    string Address,
    double Latitude,
    double Longitude,
    double DistanceKm,
    int PowerKw,
    int Total,
    decimal PricePerKwh)
{
    public bool IsFast => PowerKw >= 50;
}

/// <summary>
/// Shared state for the phone UI and the car UI. Both sides subscribe to <see cref="Changed"/>; a change made
/// on either screen (or by the simulated availability feed) re-renders the other.
/// </summary>
public sealed class StationService : IDisposable
{
    readonly Lock gate = new();
    readonly Dictionary<string, int> available;
    readonly HashSet<string> favorites = ["s2", "s5"];
    readonly Timer feed;
    readonly Random random = new(42);

    public StationService()
    {
        Stations =
        [
            new("s1", "Union Station Garage", "FLO", "65 Front St W", 43.6453, -79.3806, 0.4, 50, 6, 0.32m),
            new("s2", "Harbourfront Centre", "ChargePoint", "235 Queens Quay W", 43.6390, -79.3817, 1.1, 7, 4, 0.25m),
            new("s3", "Eaton Centre P2", "Tesla Supercharger", "220 Yonge St", 43.6544, -79.3807, 1.3, 250, 12, 0.48m),
            new("s4", "St. Lawrence Market", "Electrify Canada", "93 Front St E", 43.6487, -79.3715, 1.6, 150, 4, 0.52m),
            new("s5", "Distillery District", "FLO", "55 Mill St", 43.6503, -79.3596, 2.4, 100, 2, 0.35m),
            new("s6", "Liberty Village", "Petro-Canada", "85 Hanna Ave", 43.6395, -79.4197, 3.2, 350, 4, 0.57m),
            new("s7", "Yorkville Village", "ChargePoint", "55 Avenue Rd", 43.6708, -79.3946, 3.5, 11, 8, 0.22m),
            new("s8", "Evergreen Brick Works", "Ivy", "550 Bayview Ave", 43.6846, -79.3650, 5.8, 75, 3, 0.40m)
        ];
        available = Stations.ToDictionary(s => s.Id, s => Math.Max(0, s.Total / 2));
        available["s4"] = 0;

        // simulated live availability so the car UI visibly updates while it is open
        feed = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(8));
    }

    public IReadOnlyList<Station> Stations { get; }

    public event EventHandler? Changed;

    public bool FastOnly { get; private set; }
    public bool AvailableOnly { get; private set; }
    public bool FavoritesOnly { get; private set; }
    public int BatteryPercent { get; private set; } = 38;
    public string? ReservedStationId { get; private set; }
    public DateTimeOffset? ReservedUntil { get; private set; }

    public int RangeKm => (int)(BatteryPercent * 4.3);

    public Station? ReservedStation => Stations.FirstOrDefault(s => s.Id == ReservedStationId);

    public int Available(Station station)
    {
        lock (gate)
            return available[station.Id];
    }

    public bool IsFavorite(Station station)
    {
        lock (gate)
            return favorites.Contains(station.Id);
    }

    public IReadOnlyList<Station> Filtered() =>
        Stations
            .Where(s => !FastOnly || s.IsFast)
            .Where(s => !AvailableOnly || Available(s) > 0)
            .Where(s => !FavoritesOnly || IsFavorite(s))
            .OrderBy(s => s.DistanceKm)
            .ToList();

    public async Task<IReadOnlyList<Station>> SearchAsync(string text, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken); // pretend this hits a backend
        if (string.IsNullOrWhiteSpace(text))
            return [];
        return Stations
            .Where(s => s.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || s.Network.Contains(text, StringComparison.OrdinalIgnoreCase)
                     || s.Address.Contains(text, StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.DistanceKm)
            .ToList();
    }

    public Station? Fastest() => Filtered().Where(s => Available(s) > 0).OrderByDescending(s => s.PowerKw).FirstOrDefault();

    public void ToggleFavorite(Station station) => Mutate(() =>
    {
        if (!favorites.Remove(station.Id))
            favorites.Add(station.Id);
    });

    public void SetFilters(bool? fastOnly = null, bool? availableOnly = null, bool? favoritesOnly = null) => Mutate(() =>
    {
        FastOnly = fastOnly ?? FastOnly;
        AvailableOnly = availableOnly ?? AvailableOnly;
        FavoritesOnly = favoritesOnly ?? FavoritesOnly;
    });

    public void SetBattery(int percent) => Mutate(() => BatteryPercent = Math.Clamp(percent, 0, 100));

    public bool Reserve(Station station)
    {
        if (Available(station) == 0)
            return false;
        Mutate(() =>
        {
            if (ReservedStationId is not null)
                available[ReservedStationId]++;
            available[station.Id]--;
            ReservedStationId = station.Id;
            ReservedUntil = DateTimeOffset.Now.AddMinutes(15);
        });
        return true;
    }

    public void CancelReservation() => Mutate(() =>
    {
        if (ReservedStationId is null)
            return;
        available[ReservedStationId]++;
        ReservedStationId = null;
        ReservedUntil = null;
    });

    void Tick() => Mutate(() =>
    {
        var station = Stations[random.Next(Stations.Count)];
        if (station.Id == ReservedStationId)
            return;
        var delta = random.Next(2) == 0 ? -1 : 1;
        available[station.Id] = Math.Clamp(available[station.Id] + delta, 0, station.Total);
    });

    void Mutate(Action change)
    {
        lock (gate)
            change();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => feed.Dispose();
}
