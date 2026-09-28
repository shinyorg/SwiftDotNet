using Microsoft.Extensions.DependencyInjection;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using Xunit;

namespace SwiftDotNet.Tests.Car;

/// <summary>
/// The phone head and the car entry points must share one container — that is what keeps the phone UI and
/// the car screens two views of one state. These pin the pieces that make it so.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class CarHostingTests
{
    sealed class NoCarApp : CarApp
    {
        public override CarScreen CreateRootScreen(IServiceProvider services) => new CarTemplateScreen(CarListTemplate.Create("Root", []));
    }

    static SwiftDotNetApp BuildApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(_ => new Text("phone"));
        builder.UseCar<NoCarApp>(o => o.Category = CarAppCategory.Charging);
        return builder.Build();
    }

    [Fact]
    public void GetOrCreate_builds_once_and_every_head_gets_the_same_app()
    {
        var secondFactoryCalls = 0;
        var first = SwiftDotNetApp.GetOrCreate(BuildApp);
        var second = SwiftDotNetApp.GetOrCreate(() => { secondFactoryCalls++; return BuildApp(); });

        Assert.Same(first, second);
        Assert.Same(first, SwiftDotNetApp.Current);
        Assert.Equal(0, secondFactoryCalls);
    }

    [Fact]
    public void CarHost_falls_back_to_the_process_wide_app_container()
    {
        var app = SwiftDotNetApp.GetOrCreate(BuildApp);
        var previous = CarHost.Services;
        try
        {
            CarHost.Services = null;
            Assert.Same(app.Services, CarHost.Resolve(createApp: null));
        }
        finally
        {
            CarHost.Services = previous;
        }
    }

    [Fact]
    public void UseCar_registers_the_app_options_and_a_shared_connection()
    {
        var services = BuildApp().Services;

        Assert.IsType<NoCarApp>(services.GetRequiredService<CarApp>());
        Assert.Equal(CarAppCategory.Charging, services.GetRequiredService<CarOptions>().Category);
        Assert.Same(services.GetRequiredService<CarConnection>(), services.GetRequiredService<ICarConnection>());
    }

    [Fact]
    public void CarSession_start_marks_the_phone_side_connection_connected()
    {
        var services = BuildApp().Services;
        var connection = services.GetRequiredService<ICarConnection>();
        var changes = 0;
        connection.StateChanged += (_, _) => changes++;

        var navigator = CarSession.Start(CarPlatform.CarPlay, services, new FakeRenderer(), platformLimits: null, dispatch: a => a());
        Assert.True(connection.IsConnected);
        Assert.Equal(CarPlatform.CarPlay, connection.Platform);
        Assert.Same(navigator, connection.Navigator);

        CarSession.Stop(services, navigator);
        Assert.False(connection.IsConnected);
        Assert.Equal(2, changes);
    }

    [Theory]
    [InlineData("#22C55E", 0x22, 0xC5, 0x5E)]
    [InlineData("green", 0x34, 0xC7, 0x59)]
    [InlineData("red", 0xFF, 0x3B, 0x30)]
    public void Tint_tokens_resolve_to_the_same_rgb_the_engine_uses(string token, int r, int g, int b)
    {
        var color = CarColors.Resolve(token.StartsWith('#') ? SwiftColor.Hex(token) : token switch
        {
            "green" => SwiftColor.Green,
            _ => SwiftColor.Red,
        });
        Assert.Equal((r, g, b, 255), (color.R, color.G, color.B, color.A));
    }
}
