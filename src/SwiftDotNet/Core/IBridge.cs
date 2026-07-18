namespace SwiftDotNet;

/// <summary>
/// The seam between platform-neutral C# and the native SwiftUI host. The iOS implementation
/// P/Invokes the Swift bridge; a test/mock implementation can capture JSON instead.
/// </summary>
public interface IBridge
{
    /// <summary>Push a freshly serialized render tree to the host.</summary>
    void Render(string json);

    /// <summary>
    /// Register the callback the host invokes when an event fires. First arg is the node id;
    /// second is an optional value payload (TextField text, Toggle "true"/"false", null for a Button).
    /// </summary>
    void SetEventHandler(Action<string, string?> handler);
}
