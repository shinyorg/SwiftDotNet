#if !NO_SHINY
using Shiny;
#endif
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace SampleApp.Skia.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp();                 // registers the SkiaSharp handlers (SKCanvasView)

#if !NO_SHINY
        // Shiny plugins register into the SAME MAUI service collection the Skia UI resolves from.
        builder.Services.AddBluetoothLE();   // Shiny.BluetoothLE — a cross-platform native plugin
        builder.UseShiny();                  // Shiny.Hosting.Maui — lifecycle + Host.Services
#endif

        return builder.Build();
    }
}
