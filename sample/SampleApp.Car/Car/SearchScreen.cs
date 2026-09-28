using SampleApp.Car.Services;
using SwiftDotNet;

namespace SampleApp.Car;

public sealed class SearchScreen(StationService stations) : StationScreen(stations)
{
    public override CarTemplate Build() => new CarSearchTemplate
    {
        Hint = "Station, network or street",
        Search = async (text, ct) =>
        {
            var results = await Stations.SearchAsync(text, ct);
            return results.Select(StationRow).ToList();
        }
    };
}
