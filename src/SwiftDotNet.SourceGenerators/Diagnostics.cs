using Microsoft.CodeAnalysis;

namespace SwiftDotNet.SourceGenerators;

/// <summary>Diagnostics reported by the <c>[Inject]</c> generator.</summary>
static class Diagnostics
{
    const string Category = "SwiftDotNet.Injection";

    /// <summary>The generated <c>IInjectable</c> implementation is a partial — the type must allow it.</summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        "SDN1001",
        "View with [Inject] members must be partial",
        "'{0}' declares [Inject] members but is not declared 'partial', so the generated injection code cannot be attached to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>Nothing can be assigned to a get-only or init-only property from outside the constructor.</summary>
    public static readonly DiagnosticDescriptor NotSettable = new(
        "SDN1002",
        "[Inject] property must be settable",
        "'{0}.{1}' is marked [Inject] but has no accessible setter; injection assigns it after construction, so it needs 'set'",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// The important one: a view only ever built inline in a Body never passes through the container,
    /// so its [Inject] members would silently stay null until view-instance reconciliation lands.
    /// </summary>
    public static readonly DiagnosticDescriptor NeverInjected = new(
        "SDN1003",
        "[Inject] members will never be filled on this view",
        "'{0}' declares [Inject] members but is never created by the container. Inline children are rebuilt every render and are not injected, so use Service<T>() instead, or register the view and resolve it via UseSwiftApp<T>() or INavigator.PushAsync<T>().",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
