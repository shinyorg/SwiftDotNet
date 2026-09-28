using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

/// <summary>Root: tabs for the station list, an explore/filter grid and the vehicle status pane.</summary>
public sealed class RootScreen(StationService stations) : StationScreen(stations)
{
    string selectedTab = "stations";

    public override CarTemplate Build() => new CarTabsTemplate
    {
        SelectedTabId = selectedTab,
        OnTabSelected = id => selectedTab = id,
        Tabs =
        [
            new CarTab { Id = "stations", Title = "Stations", Image = CarIcons.Bolt, Content = StationsTab() },
            new CarTab { Id = "explore", Title = "Explore", Image = CarIcons.Explore, Content = ExploreTab() },
            new CarTab { Id = "status", Title = "Status", Image = CarIcons.Car, Content = StatusTab() }
        ]
    };

    CarListTemplate StationsTab()
    {
        var filtered = Stations.Filtered();
        var favorites = filtered.Where(Stations.IsFavorite).ToList();
        var others = filtered.Except(favorites).ToList();

        var sections = new List<CarListSection>();
        if (favorites.Count > 0 && !Stations.FavoritesOnly)
            sections.Add(new CarListSection { Header = "Favorites", Items = favorites.Select(StationRow).ToList() });
        var nearby = Stations.FavoritesOnly ? filtered : others;
        if (nearby.Count > 0 || sections.Count == 0)
            sections.Add(new CarListSection { Header = sections.Count > 0 ? "Nearby" : null, Items = nearby.Select(StationRow).ToList() });

        // more stations than the portable limit (6) - SwiftDotNet.Car truncates identically on both platforms
        return new CarListTemplate
        {
            Title = "Stations",
            Sections = sections,
            EmptyMessage = "No stations match your filters"
        };
    }

    CarGridTemplate ExploreTab()
    {
        var items = new List<CarGridItem>();
        // Category rules differ per platform: Android Auto lets charging apps search and draw maps
        // (MAP_TEMPLATES permission); CarPlay only allows CPSearchTemplate/CPMapTemplate for navigation apps.
        if (Navigator.Supports<CarSearchTemplate>())
            items.Add(new() { Title = "Search", Image = CarIcons.Search, OnSelect = () => Navigator.Push(new SearchScreen(Stations)) });
        if (Navigator.Supports<CarMapTemplate>())
            items.Add(new() { Title = "Map", Image = CarIcons.Map, OnSelect = () => Navigator.Push(new MapScreen(Stations)) });
        if (Navigator.Supports<CarPointOfInterestTemplate>())
            items.Add(new() { Title = "Nearby", Image = CarIcons.Nearby, OnSelect = () => Navigator.Push(new NearbyScreen(Stations)) });
        items.Add(Filter("Fast (50 kW+)", Stations.FastOnly, () => Stations.SetFilters(fastOnly: !Stations.FastOnly)));
        items.Add(Filter("Available", Stations.AvailableOnly, () => Stations.SetFilters(availableOnly: !Stations.AvailableOnly)));
        items.Add(Filter("Favorites", Stations.FavoritesOnly, () => Stations.SetFilters(favoritesOnly: !Stations.FavoritesOnly)));
        return new CarGridTemplate { Title = "Explore", Items = items };
    }

    static CarGridItem Filter(string title, bool on, Action toggle) => new()
    {
        Title = on ? $"{title} ✓" : title,
        Image = on ? CarIcons.Check.WithTint(CarIcons.Accent) : CarIcons.Check.WithTint(CarIcons.Muted),
        OnSelect = toggle
    };

    CarInfoTemplate StatusTab()
    {
        var reserved = Stations.ReservedStation;
        var filters = new[] { (Stations.FastOnly, "Fast"), (Stations.AvailableOnly, "Available"), (Stations.FavoritesOnly, "Favorites") }
            .Where(f => f.Item1).Select(f => f.Item2).ToList();

        return new CarInfoTemplate
        {
            Title = "Status",
            Layout = CarInfoLayout.TwoColumn,
            Items =
            [
                new CarInfoItem("Battery", $"{Stations.BatteryPercent}% · {Stations.RangeKm} km"),
                new CarInfoItem("Reservation", reserved is null ? "None" : $"{reserved.Name} until {Stations.ReservedUntil:t}"),
                new CarInfoItem("Filters", filters.Count == 0 ? "None" : string.Join(", ", filters)),
                new CarInfoItem("Favorites", $"{Stations.Stations.Count(Stations.IsFavorite)} stations")
            ],
            Actions = reserved is not null
                ? [new CarAction("Release charger", ConfirmCancel, CarActionStyle.Destructive)]
                : [new CarAction("Fastest available", ShowFastest, CarActionStyle.Primary)]
        };
    }

    void ShowFastest()
    {
        if (Stations.Fastest() is { } station)
            ShowDetails(station);
        else
            Navigator.Present(new CarAlertTemplate { Title = "No chargers available right now", Actions = [new CarAction("OK")] });
    }

    void ConfirmCancel() => Navigator.Present(new CarAlertTemplate
    {
        Title = "Release your reserved charger?",
        Actions =
        [
            new CarAction("Release", Stations.CancelReservation, CarActionStyle.Destructive),
            new CarAction("Keep", style: CarActionStyle.Cancel)
        ]
    });
}
