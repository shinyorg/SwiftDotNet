using Microsoft.Extensions.DependencyInjection;
using SampleApp.Car;
using SampleApp.Car.Services;
using SwiftDotNet;
using SwiftDotNet.Hosting;

namespace SampleApp.Car;

/// <summary>
/// The composition root, shared by the phone head <em>and</em> the car entry points (the CarPlay scene and
/// the Android Auto service both resolve from this container). That sharing is the point of the sample:
/// <see cref="StationService"/> is one singleton, so the phone and the car are two views of one state.
/// </summary>
public static class SwiftProgram
{
    /// <param name="platform">Head-specific registrations — the iOS head's <c>b =&gt; b.UseCarPlay()</c>.</param>
    public static SwiftDotNetApp CreateSwiftApp(Action<SwiftDotNetAppBuilder>? platform = null)
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(sp => new PhoneView(sp.GetRequiredService<StationService>(), sp.GetRequiredService<ICarConnection>()));
        builder.UseCar<VoltCarApp>(o =>
        {
            // Match the CarPlay entitlement (com.apple.developer.carplay-charging) and the Android intent-filter category.
            o.Category = CarAppCategory.Charging;
            o.Log = m => Console.WriteLine(m);
        });
        builder.Services.AddSingleton<StationService>();
        platform?.Invoke(builder);
        return builder.Build();
    }
}
