using Microsoft.Extensions.DependencyInjection;
using SwiftDotNet;
using SwiftDotNet.AndroidAuto;

using SampleApp.Car.Services;

namespace SampleApp.Car;

/// <summary>
/// Renders every car screen through the Android Auto template builders on the phone (no head unit needed).
/// Run from the phone UI, or: the phone UI button
/// and read `adb logcat -s CarDiag`.
/// </summary>
public static class CarTemplateCheck
{
    public static string Run(IServiceProvider services)
    {
        var stations = services.GetRequiredService<StationService>();
        var sample = stations.Stations[0];
        var results = AndroidCarDiagnostics.Validate(
            Android.App.Application.Context,
            services,
            [
                () => new RootScreen(stations),
                () => new StationDetailScreen(stations, sample),
                () => new SearchScreen(stations),
                () => new NearbyScreen(stations),
                () => new MapScreen(stations)
            ],
            [
                new CarAlertTemplate { Title = "Hold a charger?", Actions = [new CarAction("Reserve", style: CarActionStyle.Primary), new CarAction("Cancel", style: CarActionStyle.Cancel)] },
                new CarActionSheetTemplate { Title = sample.Name, Message = "Reserve or view details", Actions = [new CarAction("Reserve"), new CarAction("Details")] }
            ]);

        var lines = results.Select(r => $"{(r.Success ? "OK  " : "FAIL")} {r.Screen}{(r.Tab is null ? "" : $"[{r.Tab}]")} → {r.Detail}").ToList();
        foreach (var line in lines)
            Android.Util.Log.Info("CarDiag", line);
        Android.Util.Log.Info("CarDiag", $"{results.Count(r => r.Success)}/{results.Count} passed");
        return string.Join('\n', lines);
    }
}
