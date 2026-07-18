using SwiftDotNet;
using SwiftDotNet.Sample;

namespace SampleApp;

/// <summary>
/// The single place the sample declares its root view. Every platform's thin entry point calls this, so
/// the app's UI is registered exactly once and shared by iOS, macOS, tvOS, Android and Windows.
/// </summary>
public static class AppRoot
{
    public static View Create() => new ContentView();
}
