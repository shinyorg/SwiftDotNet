using System.Reflection;
using System.Runtime.Loader;

namespace SwiftDotNet.Preview;

/// <summary>
/// Loads the app's view assembly so it can be thrown away and loaded again when the developer saves.
///
/// This is why the preview does not use .NET hot reload. Hot reload keeps the process's types and pushes
/// new method bodies into them, which is exactly right for a *running app* (state survives — see
/// <c>docs/hot-reload.md</c>) but leaves rude edits — a new type, a changed signature, a new base class —
/// out of reach. A preview has no state worth keeping, so it can do the stronger thing: rebuild the
/// assembly and load it fresh. Every edit applies, including the ones hot reload refuses.
///
/// Two details make the swap work rather than blow up:
/// <list type="bullet">
/// <item><b>SwiftDotNet itself is shared with the host.</b> If this context loaded its own copy, the
/// <c>View</c> the app subclasses would be a *different type* from the <c>View</c> the renderer expects,
/// and every cast would fail with a message about identical-looking types. Returning null from
/// <see cref="Load"/> defers to the default context, which is what shares them.</item>
/// <item><b>Assemblies load from bytes, not paths.</b> A path-loaded assembly is memory-mapped and
/// therefore locked, and the next build would fail to overwrite the file it is previewing — on Windows
/// loudly, elsewhere subtly.</item>
/// </list>
/// </summary>
sealed class PreviewLoadContext : AssemblyLoadContext
{
    readonly AssemblyDependencyResolver _resolver;
    readonly string _mainAssemblyPath;

    public PreviewLoadContext(string mainAssemblyPath)
        : base(name: "SwiftDotNet preview", isCollectible: true)
    {
        _mainAssemblyPath = mainAssemblyPath;
        _resolver = new AssemblyDependencyResolver(mainAssemblyPath);
    }

    /// <summary>Load the assembly under preview.</summary>
    public Assembly LoadMain() => LoadFromBytes(_mainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsSharedWithHost(assemblyName.Name))
        {
            try
            {
                return Default.LoadFromAssemblyName(assemblyName);
            }
            catch (FileNotFoundException)
            {
                // Sharing is best-effort, and this is the case that proves why: the name prefix says
                // "framework", but the host only references *part* of the framework. sample/SharedUI
                // pulls in SwiftDotNet.Maps, which this host has never heard of — so the app's own copy
                // is loaded below. Its View base class still resolves to the host's SwiftDotNet through
                // this same method, which is the identity that actually matters.
            }
        }

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromBytes(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }

    Assembly LoadFromBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // Load the PDB alongside when there is one: without it a stack trace out of the previewed view
        // has no line numbers, and the preview's whole job is telling you what your code just did.
        var pdb = Path.ChangeExtension(path, ".pdb");
        if (File.Exists(pdb))
        {
            try
            {
                using var dll = new MemoryStream(bytes);
                using var symbols = new MemoryStream(File.ReadAllBytes(pdb));
                return LoadFromStream(dll, symbols);
            }
            catch (BadImageFormatException)
            {
                // A stale PDB from an interrupted build. The assembly is still fine on its own.
            }
        }

        using var stream = new MemoryStream(bytes);
        return LoadFromStream(stream);
    }

    /// <summary>
    /// Assemblies that must be the same instance here and in the host. SwiftDotNet and its backends
    /// carry the types that cross the boundary (<c>View</c>, <c>IBridge</c>, <c>SwiftApp</c>); SkiaSharp
    /// carries a native library that must not be initialised twice in one process.
    /// </summary>
    static bool IsSharedWithHost(string? name) =>
        name is not null && (
            name.StartsWith("SwiftDotNet", StringComparison.Ordinal) ||
            name.StartsWith("SkiaSharp", StringComparison.Ordinal) ||
            name.StartsWith("HarfBuzzSharp", StringComparison.Ordinal));
}
