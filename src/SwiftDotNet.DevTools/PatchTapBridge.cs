using System.Text;

namespace SwiftDotNet;

/// <summary>
/// An <see cref="IBridge"/> that forwards everything crossing the bridge to a <see cref="DevToolsServer"/>
/// and then hands it to the real bridge unchanged.
///
/// This is the whole reason the inspector needs no changes to Core. <c>SwiftApp.Render()</c> ends at
/// <c>_bridge.Render(patch.ToJson())</c>, and <see cref="IBridge"/> has exactly two members — so a
/// decorator sees the complete conversation between the framework and its host. And because *every*
/// backend consumes that same patch stream, one inspector covers SwiftUI, Compose, GTK, WinUI, the DOM
/// and Skia without knowing which one it is looking at.
/// </summary>
public sealed class PatchTapBridge(IBridge inner, DevToolsServer server) : IBridge
{
    long _sequence;

    /// <summary>The patch JSON of the most recent render, for a client that attaches late.</summary>
    public string? LastPatch { get; private set; }

    public void Render(string json)
    {
        var seq = Interlocked.Increment(ref _sequence);
        LastPatch = json;

        // The real bridge goes first. A dev tool must never sit between the app and the screen: if the
        // socket write blocks or throws, the UI still updates on time.
        inner.Render(json);

        if (server.HasClients)
            server.Broadcast(DevToolsProtocol.ServerFrames.Patch, Encoding.UTF8.GetBytes($"{seq}\n{json}"));
    }

    public void SetEventHandler(Action<string, string?> handler)
    {
        inner.SetEventHandler((nodeId, value) =>
        {
            if (server.HasClients)
                server.Broadcast(DevToolsProtocol.ServerFrames.Event, $"{nodeId}\t{value}");
            handler(nodeId, value);
        });
    }
}
