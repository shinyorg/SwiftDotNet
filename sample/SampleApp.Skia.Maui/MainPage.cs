#if !NO_SHINY
using Shiny.BluetoothLE;
#endif
using SwiftDotNet;
using SwiftDotNet.Sample;
// Disambiguate the SwiftDotNet DSL types from MAUI's same-named types (aliases beat global usings).
using SdnView = SwiftDotNet.View;
using Font = SwiftDotNet.Font;
using Color = SwiftDotNet.Color;

namespace SampleApp.Skia.Maui;

/// <summary>
/// Hosts the SwiftDotNet/Skia self-drawn UI in a MAUI page, and resolves a Shiny plugin
/// (<see cref="IBleManager"/>) from the same MAUI DI container — proving the Skia UI and Shiny's
/// cross-platform plugins compose in one app.
/// </summary>
public class MainPage : ContentPage
{
    public MainPage()
    {
        Title = "SwiftDotNet · Skia + Shiny";

        // .NET 10's ContentPage defaults to edge-to-edge on every platform, and the Skia canvas draws the
        // *whole* UI itself — so without this the scene paints under the status bar, notch/Dynamic Island
        // and home indicator. Fully qualified because Core's own SwiftDotNet.SafeAreaRegions (the iOS/
        // Android `.SafeAreaPadding` API) collides with MAUI's by simple name, and this is MAUI's.
        SafeAreaEdges = new Microsoft.Maui.SafeAreaEdges(Microsoft.Maui.SafeAreaRegions.Container);

#if NO_SHINY
        var status = "(Shiny disabled — host-render proof)";
#else
        var ble = IPlatformApplication.Current?.Services.GetService<IBleManager>();
        var status = ble is null
            ? "IBleManager NOT resolved"
            : $"IBleManager resolved ✓  ({ble.GetType().Name})";
#endif

        // The whole screen is one self-drawn Skia canvas. Render the full shared sample app (the same
        // ContentView every other backend hosts) so this sample exercises real UI — sliders, panels and
        // the rest — rather than a static proof card. Pass `?status=` style state via the view itself.
        var swiftApp = SwiftProgram.CreateSwiftApp();
        Content = new SwiftDotNetSkiaView(swiftApp.CreateRoot(), swiftApp.Services);

        // Keep the Shiny resolution result visible in the page title — the point of this sample is that
        // the Skia UI and Shiny's plugins come out of the same MAUI ServiceProvider.
        Title = $"SwiftDotNet · Skia — {status}";
    }
}

/// <summary>A SwiftDotNet view that shows the resolved Shiny service — rendered by the Skia engine.</summary>
sealed class ShinyStatusView : SdnView
{
    readonly string _status;
    public ShinyStatusView(string status) => _status = status;

    public override SdnView Body => new VStack(
        new Text("SwiftDotNet · Skia").Font(Font.LargeTitle).ForegroundColor(Color.Accent),
        new Text("Self-drawn UI + Shiny plugins — one MAUI app").Font(Font.Caption).ForegroundColor(Color.Secondary),
        new Divider().Frame(width: 320),
        new Text("Shiny (shared DI container):").Font(Font.Headline),
        new Text(_status).Font(Font.Body).ForegroundColor(Color.Green),
        new Text("The Skia engine and Shiny both resolve from the MAUI ServiceProvider — .UseShiny() next to .UseSkiaSharp().")
            .Font(Font.Caption).ForegroundColor(Color.Secondary)
    ).Spacing(14).Padding(28);
}
