using System.Runtime.InteropServices;

namespace SwiftDotNet;

/// <summary>
/// Activates the native AVFoundation + Vision renderer for the <see cref="SwiftDotNet.Controls.CameraView"/>
/// control on Apple platforms. Call <see cref="Register"/> once at app startup, before the first render, so
/// <c>CameraView</c> nodes render as a real camera preview instead of the "unknown view" placeholder.
/// Mirrors <c>AppleMaps.Register</c>.
/// </summary>
public static partial class AppleCamera
{
    // The framework is embedded in the app bundle, so its @_cdecl export resolves globally (dlsym),
    // matching how the core bridge's own P/Invokes are declared.
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_register_camera")]
    private static partial void RegisterNative();

    /// <summary>Register the AVFoundation camera renderer. Idempotent; safe to call once at startup.</summary>
    public static void Register() => RegisterNative();
}
