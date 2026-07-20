using Microsoft.Extensions.DependencyInjection;
using SwiftDotNet.Hosting;

namespace SwiftDotNet.Sample;

/// <summary>
/// The sample's composition root — the SwiftDotNet analog of MAUI's <c>MauiProgram.cs</c>, and the one
/// place the app declares its services, logging and root view. Every platform head calls it and does
/// nothing else:
/// <code>
/// protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
/// </code>
/// </summary>
public static class SwiftProgram
{
    /// <param name="platform">
    /// Platform-only registrations the shared project can't reference — e.g. the iOS head opting into
    /// the native MapKit renderer with <c>b =&gt; b.UseAppleMaps()</c>.
    /// </param>
    public static SwiftDotNetApp CreateSwiftApp(Action<SwiftDotNetAppBuilder>? platform = null)
    {
        var builder = SwiftDotNetApp.CreateBuilder();

        // Explicit factory rather than UseSwiftApp<T>(): no runtime constructor discovery, so it is
        // trim/AOT-clean under iOS (see the DI plan's §10).
        builder.UseSwiftApp(_ => new SampleRootView());

        AddSharedServices(builder.Services);
        platform?.Invoke(builder);

        return builder.Build();
    }

    /// <summary>
    /// The registrations shared by every backend. Split out so Blazor can contribute them to its <b>own</b>
    /// container rather than building a second one (see the Web sample's <c>Program.cs</c>).
    /// </summary>
    public static void AddSharedServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Demonstrates the three delivery styles the DI plan settled on:
        //   [Inject] partial properties (SampleDiView), Service<T>() in an inline child, and an
        //   IViewLifecycle observer that sees every retained view.
        services.AddSingleton<IGreetingService, GreetingService>();
        services.AddSingleton<IViewLifecycle, ConsoleViewLifecycle>();
    }
}

/// <summary>A trivial app service, injected into <see cref="SampleDiView"/>.</summary>
public interface IGreetingService
{
    string Greet();
}

sealed class GreetingService : IGreetingService
{
    public string Greet() => $"Injected at {DateTime.Now:HH:mm:ss}";
}

/// <summary>A lifecycle observer — every registered one is called for every retained view.</summary>
sealed class ConsoleViewLifecycle : IViewLifecycle
{
    public void OnCreated(View view) => Console.WriteLine($"[lifecycle] created {view.GetType().Name}");
    public void OnAppearing(View view) => Console.WriteLine($"[lifecycle] appearing {view.GetType().Name}");
    public void OnDisappearing(View view) => Console.WriteLine($"[lifecycle] disappearing {view.GetType().Name}");
    public void OnDestroyed(View view) => Console.WriteLine($"[lifecycle] destroyed {view.GetType().Name}");
}
