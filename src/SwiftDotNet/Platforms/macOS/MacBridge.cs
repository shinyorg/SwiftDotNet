using System.Runtime.InteropServices;
using AppKit;
using ObjCRuntime;

namespace SwiftDotNet;

/// <summary>
/// The macOS implementation of <see cref="IBridge"/>: P/Invokes the SwiftDotNetBridge framework
/// (the SAME Swift interpreter as iOS — SwiftUI is AppKit-backed on macOS) and bridges events back
/// from SwiftUI into managed callbacks. Internal — apps use <see cref="SwiftDotNetHost"/>.
/// </summary>
internal sealed unsafe partial class MacBridge : IBridge
{
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_make_host_controller")]
    private static partial IntPtr MakeHostController();

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_render", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void NativeRender(string json);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_set_event_callback")]
    private static partial void SetEventCallback(IntPtr callback);

    private static Action<string, string?>? _handler;

    /// <summary>Obtains the SwiftUI-backed root controller (an <c>NSHostingController</c> from Swift).</summary>
    public NSViewController CreateHostController()
    {
        var ptr = MakeHostController();
        return Runtime.GetNSObject<NSViewController>(ptr)
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
