namespace SwiftDotNet;

/// <summary>
/// Entry point for hosting a SwiftDotNet view hierarchy in a .NET GTK4 app (Linux, and anywhere
/// GTK4 is available). Runs the GTK application loop:
/// <code>
/// return SwiftDotNetHost.Run(new ContentView());
/// </code>
/// </summary>
public static class SwiftDotNetHost
{
    public static int Run(View root, string applicationId = "com.swiftdotnet.gtk")
    {
        var app = Gtk.Application.New(applicationId, Gio.ApplicationFlags.DefaultFlags);

        app.OnActivate += (sender, _) =>
        {
            var bridge = new GtkBridge();
            var window = Gtk.ApplicationWindow.New((Gtk.Application)sender);
            window.Title = "SwiftDotNet";
            window.SetDefaultSize(440, 820);
            window.Child = bridge.Host;

            // Start the render loop; from here C# state changes drive real GTK widgets.
            SwiftApp.Run(root, bridge);

            window.Present();
        };

        return app.RunWithSynchronizationContext(null);
    }
}
