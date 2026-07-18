using System.Runtime.InteropServices;
using ObjCRuntime;
using UIKit;

namespace SwiftDotNet;

/// <summary>
/// The iOS implementation of <see cref="IBridge"/>: P/Invokes the SwiftDotNetBridge framework
/// and bridges events back from SwiftUI into managed callbacks. Internal — apps use
/// <see cref="SwiftDotNetHost"/>.
/// </summary>
internal sealed unsafe partial class IosBridge : IBridge
{
    // The bridge framework is a load-time dependency (embedded in the app bundle), so its
    // exported @_cdecl symbols are in the global namespace. Resolving via "__Internal"
    // (dlsym RTLD_DEFAULT) avoids the @rpath-vs-dlopen-leaf-name resolution problem.
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_make_host_controller")]
    private static partial IntPtr MakeHostController();

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_render", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void NativeRender(string json);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_set_event_callback")]
    private static partial void SetEventCallback(IntPtr callback);

    private static Action<string, string?>? _handler;

    /// <summary>Obtains the SwiftUI-backed root controller (owns the +1 ref returned by Swift).</summary>
    public UIViewController CreateHostController()
    {
        var ptr = MakeHostController();
        return Runtime.GetNSObject<UIViewController>(ptr)
            ?? throw new InvalidOperationException("Swift returned a null host controller.");
    }

    public void Render(string json) => NativeRender(json);

    public void SetEventHandler(Action<string, string?> handler)
    {
        _handler = handler;
        SetEventCallback((IntPtr)(delegate* unmanaged<byte*, byte*, void>)&OnEvent);
    }

    [UnmanagedCallersOnly]
    private static void OnEvent(byte* idPtr, byte* valuePtr)
    {
        var id = Marshal.PtrToStringUTF8((IntPtr)idPtr);
        var value = valuePtr == null ? null : Marshal.PtrToStringUTF8((IntPtr)valuePtr);
        if (id is not null)
            _handler?.Invoke(id, value);
    }
}
