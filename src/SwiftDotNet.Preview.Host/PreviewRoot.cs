using System.Reflection;

namespace SwiftDotNet.Preview;

/// <summary>
/// Finds something to preview inside a freshly loaded assembly, and reports clearly when it can't.
///
/// Preference order, and the reason for it:
/// <list type="number">
/// <item><b><c>SwiftProgram.CreateSwiftApp()</c></b> — the framework's documented front door
/// (<c>docs/hosting-and-di.md</c>). Using it means the preview gets the app's real DI container, so a
/// view that resolves a service previews the same way it runs. Anything less is a different app.</item>
/// <item><b>A named <c>View</c> subclass</b> (<c>--view</c>) — for previewing one screen in isolation,
/// which is what a preview is usually for.</item>
/// <item><b>A single obvious <c>View</c></b> — if the assembly has exactly one candidate, use it rather
/// than making the developer name it.</item>
/// </list>
/// </summary>
static class PreviewRoot
{
    public sealed record Result(View Root, IServiceProvider? Services, string Description);

    public static Result Resolve(Assembly assembly, string? viewTypeName)
    {
        if (viewTypeName is not null)
            return FromViewType(assembly, viewTypeName);

        if (TryFromSwiftProgram(assembly, out var fromProgram))
            return fromProgram;

        var candidates = ViewCandidates(assembly).ToList();
        return candidates.Count switch
        {
            1 => FromViewType(assembly, candidates[0].FullName!),
            0 => throw new PreviewException(
                $"'{assembly.GetName().Name}' has no SwiftProgram.CreateSwiftApp() and no public View " +
                "subclass with a parameterless constructor. Pass --view <TypeName>."),
            _ => throw new PreviewException(
                $"'{assembly.GetName().Name}' has {candidates.Count} previewable views and no " +
                "SwiftProgram.CreateSwiftApp(). Pass --view <TypeName>, one of: " +
                string.Join(", ", candidates.Take(10).Select(c => c.FullName))),
        };
    }

    static bool TryFromSwiftProgram(Assembly assembly, out Result result)
    {
        result = null!;

        // Matched by shape, not by a marker interface, because that is how the convention is actually
        // documented — the sample's SwiftProgram is a plain static class.
        foreach (var type in SafeGetTypes(assembly))
        {
            if (type.Name != "SwiftProgram")
                continue;

            var method = type.GetMethod("CreateSwiftApp", BindingFlags.Public | BindingFlags.Static);
            if (method is null || !typeof(SwiftDotNet.Hosting.SwiftDotNetApp).IsAssignableFrom(method.ReturnType))
                continue;

            // The documented signature takes an optional platform-registration callback; a head that
            // does not need one may have dropped the parameter entirely.
            var args = method.GetParameters().Length == 0 ? [] : new object?[method.GetParameters().Length];
            var app = (SwiftDotNet.Hosting.SwiftDotNetApp)method.Invoke(null, args)!;

            result = new Result(app.CreateRoot(), app.Services, $"{type.FullName}.CreateSwiftApp()");
            return true;
        }

        return false;
    }

    static Result FromViewType(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName, throwOnError: false)
                   ?? SafeGetTypes(assembly).FirstOrDefault(t => t.Name == typeName)
                   ?? throw new PreviewException($"No type '{typeName}' in '{assembly.GetName().Name}'.");

        if (!typeof(View).IsAssignableFrom(type))
            throw new PreviewException($"'{type.FullName}' does not derive from View.");

        if (type.GetConstructor(Type.EmptyTypes) is null)
            throw new PreviewException(
                $"'{type.FullName}' has no parameterless constructor, so the preview cannot build one. " +
                "Preview it through SwiftProgram.CreateSwiftApp() instead, which supplies its dependencies.");

        return new Result((View)Activator.CreateInstance(type)!, null, type.FullName!);
    }

    static IEnumerable<Type> ViewCandidates(Assembly assembly) =>
        SafeGetTypes(assembly).Where(t =>
            t.IsPublic &&
            !t.IsAbstract &&
            typeof(View).IsAssignableFrom(t) &&
            t.GetConstructor(Type.EmptyTypes) is not null);

    /// <summary>
    /// A half-loaded dependency makes <c>GetTypes()</c> throw *after* filling in the types it did
    /// manage to load. Those are exactly the ones worth looking at, so take them.
    /// </summary>
    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}

/// <summary>An error worth showing the developer verbatim, rather than a stack trace.</summary>
sealed class PreviewException(string message) : Exception(message);
