using System.Runtime.InteropServices;

namespace SwiftDotNet;

/// <summary>
/// P/Invoke into the SwiftDotNetWidgets shim. Internal - apps use <see cref="SwiftDotNetLive"/>.
/// </summary>
internal static unsafe partial class AppleLiveBridge
{
    // The shim is a load-time dependency embedded in the app bundle, so its exported @_cdecl symbols are
    // in the global namespace. Resolving via "__Internal" (dlsym RTLD_DEFAULT) avoids the
    // @rpath-vs-dlopen-leaf-name resolution problem, exactly as the main bridge does.
    const string Lib = "__Internal";

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_configure", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Configure(string appGroup);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_set_action_callback")]
    internal static partial void SetActionCallback(IntPtr callback);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_set_push_token_callback")]
    internal static partial void SetPushTokenCallback(IntPtr callback);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_start", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Start(string kind, string snapshot);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_update", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void Update(string kind, string snapshot);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_end", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void End(string kind, string? snapshot);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_active", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial IntPtr Active(string kind);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_widgets_reload", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial void ReloadWidgets(string? kind);

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_widgets_placements")]
    internal static partial IntPtr Placements();

    [LibraryImport(Lib, EntryPoint = "swiftdotnet_live_free")]
    internal static partial void Free(IntPtr pointer);

    /// <summary>Reads and frees a string the shim allocated with <c>strdup</c>.</summary>
    internal static string? TakeString(IntPtr pointer)
    {
        if (pointer == IntPtr.Zero) return null;
        try
        {
            return Marshal.PtrToStringUTF8(pointer);
        }
        finally
        {
            // The shim allocates with strdup, so the free must go back through it rather than through
            // Marshal.FreeHGlobal - different allocators, and mixing them corrupts the heap.
            Free(pointer);
        }
    }
}
