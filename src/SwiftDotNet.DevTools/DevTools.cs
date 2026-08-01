namespace SwiftDotNet;

/// <summary>
/// The one call a host makes to expose its patch stream to an IDE:
///
/// <code>
/// SwiftApp.Run(root, DevTools.Wrap(bridge, "skia"), services);
/// </code>
///
/// <b>Off by default.</b> With no <c>SWIFTDOTNET_DEVTOOLS_PORT</c> in the environment,
/// <see cref="Wrap"/> hands back the bridge it was given — no listener, no threads, no allocation. That
/// is what makes it safe to leave the call in a host's startup path permanently: the IDE turns it on by
/// setting the variable when it launches the app, and nothing else ever does.
///
/// Deliberately *not* gated on <c>#if DEBUG</c>, for the same reason
/// <see cref="SwiftDotNet.HotReload"/> isn't: that bakes the decision into the shipped package instead
/// of leaving it to the consumer's build and their environment.
/// </summary>
public static class DevTools
{
    /// <summary>The port to listen on. <c>0</c> asks the OS for a free one, reported on stdout.</summary>
    public const string PortVariable = "SWIFTDOTNET_DEVTOOLS_PORT";

    static DevToolsServer? _server;

    /// <summary>The running server, or null when dev tools are off.</summary>
    public static DevToolsServer? Server => _server;

    /// <summary>True when an IDE asked for the dev-tools channel.</summary>
    public static bool IsEnabled => _server is not null;

    /// <summary>
    /// Wrap <paramref name="bridge"/> in a tap when the environment asks for one, otherwise return it
    /// untouched.
    /// </summary>
    /// <param name="bridge">The real backend bridge.</param>
    /// <param name="backend">Backend name for the <c>hello</c> frame — "skia", "gtk", "apple", …</param>
    public static IBridge Wrap(IBridge bridge, string backend)
    {
        ArgumentNullException.ThrowIfNull(bridge);

        var requested = Environment.GetEnvironmentVariable(PortVariable);
        if (string.IsNullOrWhiteSpace(requested) || !int.TryParse(requested, out var port) || port < 0)
            return bridge;

        try
        {
            var server = new DevToolsServer(port)
            {
                Greeting = $"backend={backend};pid={Environment.ProcessId};protocol=1",
            };
            _server = server;

            // Printed unconditionally, because the IDE may have passed 0 and this line is how it learns
            // the port. Also the only sign of life when someone sets the variable by hand.
            Console.WriteLine($"[swiftdotnet] dev tools listening on 127.0.0.1:{server.Port}");

            return new PatchTapBridge(bridge, server);
        }
        catch (Exception ex)
        {
            // A dev tool that stops an app from starting has failed at its job. Say so and carry on.
            Console.Error.WriteLine($"[swiftdotnet] dev tools disabled: {ex.Message}");
            return bridge;
        }
    }

    /// <summary>Push a line into the IDE's log view. No-op when dev tools are off.</summary>
    public static void Log(string message)
        => _server?.Broadcast(DevToolsProtocol.ServerFrames.Log, message);

    /// <summary>Stop the server and drop every client. Mainly for tests.</summary>
    public static void Shutdown()
    {
        _server?.Dispose();
        _server = null;
    }
}
