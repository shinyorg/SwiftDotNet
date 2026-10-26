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

/// <summary>
/// Optional companion to <see cref="IBridge"/>: a bridge that can render some composite views with a
/// platform-native container instead of their C# <c>Body</c>. Today the only such view is
/// <see cref="TwoPaneView"/> (SwiftUI <c>ArrangementView</c> on iOS 27.1+, Compose
/// <c>ListDetailPaneScaffold</c> on Android). A bridge that doesn't implement this gets the composite —
/// which every backend can draw — so implementing it is never required.
/// </summary>
public interface IBridgeCapabilities
{
    /// <summary>True when the host renders a node of type <paramref name="viewType"/> natively.</summary>
    bool SupportsNativeView(string viewType);
}
