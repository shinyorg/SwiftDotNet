using SwiftDotNet;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;

// Native Wayland host for the Skia self-drawing backend. No GTK, no GLFW, no X11 — this talks xdg-shell to
// the compositor directly, draws into a wl_shm buffer with SkiaSharp, and draws its own titlebar because
// GNOME will not draw one for it.
//
//   dotnet run --project sample/SampleApp.Wayland
//
// Requires a Wayland session (WAYLAND_DISPLAY set), libwayland-client and libxkbcommon.

SkiaSampleRenderers.RegisterAll();

var app = SwiftProgram.CreateSwiftApp();

WaylandSkiaHost.Run(
    app.CreateRoot,
    app.Services,
    new WaylandHostOptions
    {
        Title = "SwiftDotNet · Wayland",
        AppId = "net.swiftdotnet.sample",
        Width = 440,
        Height = 820,
    });
