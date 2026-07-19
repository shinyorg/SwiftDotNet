using System.Runtime.InteropServices;

namespace SwiftDotNet;

/// <summary>
/// Activates the native MapKit renderer for the <see cref="Map"/> control on Apple platforms. Call
/// <see cref="Register"/> once at app startup, before the first render, so <c>Map</c> nodes render as a
/// real MapKit map instead of the "unknown view" placeholder.
/// </summary>
public static partial class AppleMaps
{
    // The framework is embedded in the app bundle, so its @_cdecl export resolves globally (dlsym),
    // matching how the core bridge's own P/Invokes are declared.
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_register_maps")]
    private static partial void RegisterNative();

    /// <summary>Register the MapKit renderer. Idempotent; safe to call once at startup.</summary>
    public static void Register() => RegisterNative();
}
