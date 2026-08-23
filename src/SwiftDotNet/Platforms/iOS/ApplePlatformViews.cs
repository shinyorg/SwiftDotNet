using System.Runtime.InteropServices;
using UIKit;

namespace SwiftDotNet;

/// <summary>
/// Lets a SwiftUI-hosted tree show a real <see cref="UIView"/> the DSL has no node type for — most usefully
/// a .NET MAUI control realised through <c>MauiEmbedding</c>, but anything with a <c>UIView</c> works.
///
/// <para>This is the Apple half of the platform-view story, and it is a different mechanism from the
/// self-drawing one. A canvas backend floats the control *over* its own pixels
/// (<c>IPlatformViewHost</c>); SwiftUI has no canvas to float anything over, so the view is handed to a
/// <c>UIViewRepresentable</c> inside the SwiftUI tree instead and SwiftUI lays it out like any other
/// child.</para>
///
/// <code>
/// // AppDelegate, before the first render:
/// MauiEmbedding.Initialize(this, window);
/// ApplePlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as UIView);
/// </code>
///
/// <para>Until <see cref="Register(Func{string, UIView?})"/> is called, a <c>MauiView</c> node renders the
/// same ⚠️ placeholder an unregistered custom renderer does — never a crash.</para>
///
/// <para><b>Ownership:</b> the pointer handed to Swift is <em>unretained</em>. Managed code owns the view
/// and must keep it alive for as long as the node is on screen; Swift only borrows it.</para>
/// </summary>
public static unsafe partial class ApplePlatformViews
{
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_set_platform_view_provider")]
    private static partial void SetPlatformViewProvider(IntPtr provider);

    static Func<string, IntPtr>? _provider;

    /// <summary>Supply native views by node key, as raw handles.</summary>
    public static void Register(Func<string, IntPtr> provider)
    {
        _provider = provider;
        SetPlatformViewProvider((IntPtr)(delegate* unmanaged<byte*, IntPtr>)&Provide);
    }

    /// <summary>Supply native views by node key. The common form — a null result renders nothing.</summary>
    public static void Register(Func<string, UIView?> provider)
        => Register(key => provider(key)?.Handle ?? IntPtr.Zero);

    /// <summary>Stop serving platform views; nodes fall back to the placeholder.</summary>
    public static void Unregister()
    {
        _provider = null;
        SetPlatformViewProvider(IntPtr.Zero);
    }

    [UnmanagedCallersOnly]
    private static IntPtr Provide(byte* keyPtr)
    {
        var key = Marshal.PtrToStringUTF8((IntPtr)keyPtr);
        if (key is null || _provider is null) return IntPtr.Zero;
        // A factory is app code running on the UI thread inside a SwiftUI layout pass; letting an exception
        // unwind into Swift would terminate the process rather than surface anything useful.
        try { return _provider(key); }
        catch { return IntPtr.Zero; }
    }
}
