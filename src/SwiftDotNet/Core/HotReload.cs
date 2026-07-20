using System.Reflection.Metadata;

[assembly: MetadataUpdateHandler(typeof(SwiftDotNet.HotReload))]

namespace SwiftDotNet;

/// <summary>
/// Hot reload support: when <c>dotnet watch</c> pushes an edited method body into the running process,
/// the runtime calls this handler and we re-render the whole tree.
///
/// It works because of two properties the runtime already has:
/// <list type="bullet">
/// <item>the root <see cref="View"/> instance is <b>retained</b> for the life of the app
/// (<see cref="Hosting.SwiftDotNetApp.CreateRoot"/> caches it), while <see cref="View.Body"/> is
/// re-evaluated on <i>every</i> render — so an edited <c>Body</c> takes effect on the next pass with no
/// extra machinery;</item>
/// <item><see cref="State{T}"/> cells are fields on that retained instance, so <b>state survives a
/// reload</b> — you keep your scroll position, your typed text, and which page you pushed. That is the
/// SwiftUI-preview behaviour, and it is the reason hot reload is actually useful here rather than a
/// glorified restart.</item>
/// </list>
///
/// Nothing backend-specific is needed: every backend already applies patches, and a reload simply
/// produces one big <c>replace</c> patch instead of a small diff.
///
/// <b>Debug only.</b> Everything here is gated on <see cref="MetadataUpdater.IsSupported"/>, which the
/// runtime sets false for Release / trimmed / AOT builds — so the trimmer removes this whole path from a
/// shipping app rather than us guarding it with <c>#if DEBUG</c> (which would bake the decision into the
/// NuGet package instead of the consumer's build).
/// </summary>
public static class HotReload
{
    static readonly List<Action<Type[]?>> Flushes = new();

    /// <summary>
    /// True when the process was launched in a way that can receive metadata updates — i.e. under
    /// <c>dotnet watch</c> or a debugger with hot reload enabled. False in Release/trimmed/AOT builds.
    /// </summary>
    public static bool IsSupported => MetadataUpdater.IsSupported;

    /// <summary>
    /// Raised after an edit has been applied and the re-render has been scheduled. The argument is the
    /// set of updated types, or <c>null</c> when the runtime could not determine it. Useful for
    /// dev-only affordances (a reload toast, re-seeding a fake service).
    /// </summary>
    public static event Action<Type[]?>? Reloaded;

    /// <summary>
    /// Register a cache to be flushed before each reload. Backends and apps use this for anything keyed
    /// by something an edit can invalidate.
    /// </summary>
    /// <remarks>
    /// Deliberately <i>not</i> used for the renderer registries
    /// (<c>SkiaRenderers</c>/<c>GtkRenderers</c>/<c>WebRenderers</c>/<c>WinRenderers</c>): those hold
    /// registration state the app set up at startup and would never re-run, and an edited renderer keeps
    /// its identity while picking up the new method bodies anyway. Clearing them would break the seam,
    /// not refresh it.
    /// </remarks>
    public static void RegisterCacheFlush(Action<Type[]?> flush)
    {
        ArgumentNullException.ThrowIfNull(flush);
        if (!IsSupported)
            return;
        Flushes.Add(flush);
    }

    // ---- Called by the runtime, by reflection, in this order. ------------------------------------

    /// <summary>Runtime entry point — do not call directly.</summary>
    internal static void ClearCache(Type[]? updatedTypes)
    {
        for (var i = 0; i < Flushes.Count; i++)
            Flushes[i](updatedTypes);
    }

    /// <summary>Runtime entry point — do not call directly.</summary>
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        SwiftApp.Invalidate();
        Reloaded?.Invoke(updatedTypes);
    }
}
