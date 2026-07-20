using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SwiftDotNet.SourceGenerators;

/// <summary>
/// Emits the <c>IInjectable.Inject</c> implementation for every type declaring <c>[Inject]</c>
/// properties, as plain static assignments — no reflection, so property injection stays trim/AOT-clean.
///
/// For a view like:
/// <code>
/// public sealed partial class WeatherView : View
/// {
///     [Inject] public IWeatherService Weather { get; set; } = default!;
///     [Inject] public IImageCache? Cache { get; set; }
/// }
/// </code>
/// it emits:
/// <code>
/// partial class WeatherView : global::SwiftDotNet.IInjectable
/// {
///     void global::SwiftDotNet.IInjectable.Inject(global::System.IServiceProvider provider)
///     {
///         this.Weather = global::SwiftDotNet.SwiftHost.Require&lt;global::IWeatherService&gt;(provider);
///         this.Cache   = global::SwiftDotNet.SwiftHost.Optional&lt;global::IImageCache&gt;(provider);
///     }
/// }
/// </code>
/// A nullable property resolves optionally; a non-nullable one is required.
/// </summary>
[Generator]
public sealed class InjectGenerator : IIncrementalGenerator
{
    const string InjectAttribute = "SwiftDotNet.InjectAttribute";

    /// <summary>
    /// Fully-qualified <b>with</b> the nullable annotation, so an implementing partial declaration
    /// matches its declaring one's signature exactly (CS9256) and the backing field keeps its nullability.
    /// </summary>
    static readonly SymbolDisplayFormat DeclaredTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // Types declaring [Inject] members, with their members and validity already resolved.
        var targets = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                InjectAttribute,
                predicate: static (node, _) => node is PropertyDeclarationSyntax,
                transform: static (ctx, _) => (IPropertySymbol)ctx.TargetSymbol)
            .Collect()
            .Select(static (properties, _) => Group(properties));

        // Every type ever used as a generic type argument (UseSwiftApp<T>, PushAsync<T>, AddSingleton<T>,
        // GetRequiredService<T>, …) or constructed inside a registration lambda. Used only to decide
        // whether SDN1003 applies — see IsContainerCreated.
        var containerCreated = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is GenericNameSyntax or ObjectCreationExpressionSyntax,
                transform: static (ctx, ct) => ResolveCandidate(ctx, ct))
            .Where(static s => s is not null)
            .Collect()
            .Select(static (symbols, _) =>
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var s in symbols)
                    if (s is not null)
                        set.Add(s);
                return set;
            });

        context.RegisterSourceOutput(targets.Combine(containerCreated), static (spc, pair) =>
        {
            var (groups, created) = pair;
            foreach (var group in groups)
                Emit(spc, group, created);
        });
    }

    /// <summary>Collects types that plausibly come from the container, to suppress SDN1003.</summary>
    static string? ResolveCandidate(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        switch (ctx.Node)
        {
            // UseSwiftApp<ContentView>(), PushAsync<DetailView>(), AddSingleton<Foo>(), …
            case GenericNameSyntax generic:
                foreach (var arg in generic.TypeArgumentList.Arguments)
                    if (ctx.SemanticModel.GetSymbolInfo(arg, ct).Symbol is INamedTypeSymbol named)
                        return named.ToDisplayString();
                return null;

            // AddSingleton(sp => new ContentView(sp.GetRequiredService<IFoo>())) — explicit factories.
            case ObjectCreationExpressionSyntax creation
                when creation.FirstAncestorOrSelf<LambdaExpressionSyntax>() is not null:
                return ctx.SemanticModel.GetSymbolInfo(creation.Type, ct).Symbol is INamedTypeSymbol t
                    ? t.ToDisplayString()
                    : null;

            default:
                return null;
        }
    }

    static ImmutableArray<InjectTarget> Group(ImmutableArray<IPropertySymbol> properties)
    {
        if (properties.IsDefaultOrEmpty)
            return ImmutableArray<InjectTarget>.Empty;

        var byType = new Dictionary<INamedTypeSymbol, List<IPropertySymbol>>(SymbolEqualityComparer.Default);
        foreach (var property in properties)
        {
            if (property.ContainingType is not { } type)
                continue;
            if (!byType.TryGetValue(type, out var list))
                byType[type] = list = new List<IPropertySymbol>();
            list.Add(property);
        }

        var builder = ImmutableArray.CreateBuilder<InjectTarget>(byType.Count);
        foreach (var pair in byType)
            builder.Add(new InjectTarget(pair.Key, pair.Value.ToImmutableArray()));
        return builder.ToImmutable();
    }

    static void Emit(SourceProductionContext spc, InjectTarget target, HashSet<string> containerCreated)
    {
        var type = target.Type;

        // SDN1001 — the generated implementation is a partial declaration, so the type must permit one.
        var isPartial = type.DeclaringSyntaxReferences
            .Select(static r => r.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .Any(static d => d.Modifiers.Any(SyntaxKind.PartialKeyword));

        if (!isPartial)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                Diagnostics.NotPartial, Location(type), type.Name));
            return;
        }

        // Two supported forms:
        //   partial: `[Inject] public partial IFoo Foo { get; }` — we emit the implementing part, so the
        //            property is read-only to everyone else and needs no `= default!` initializer.
        //   settable: `[Inject] public IFoo Foo { get; set; } = default!;` — we assign it directly.
        // Anything else (get-only non-partial, init-only) cannot be assigned after construction.
        var injectable = ImmutableArray.CreateBuilder<IPropertySymbol>(target.Properties.Length);
        foreach (var property in target.Properties)
        {
            if (property.IsPartialDefinition)
            {
                injectable.Add(property);
                continue;
            }

            if (property.SetMethod is null || property.SetMethod.IsInitOnly)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Diagnostics.NotSettable, Location(property), type.Name, property.Name));
                continue;
            }
            injectable.Add(property);
        }

        var settable = injectable;
        if (settable.Count == 0)
            return;

        // SDN1003 — warn when nothing will ever call Inject on this type. Heuristic and deliberately
        // conservative: any generic-type-argument or in-lambda construction mention counts as
        // "container-created", so cross-assembly registration can still produce a false positive.
        if (!containerCreated.Contains(type.ToDisplayString()))
            spc.ReportDiagnostic(Diagnostic.Create(Diagnostics.NeverInjected, Location(type), type.Name));

        spc.AddSource($"{FileName(type)}.Inject.g.cs", Render(type, settable.ToImmutable()));
    }

    static string Render(INamedTypeSymbol type, ImmutableArray<IPropertySymbol> properties)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();

        var hasNamespace = !type.ContainingNamespace.IsGlobalNamespace;
        if (hasNamespace)
        {
            sb.Append("namespace ").Append(type.ContainingNamespace.ToDisplayString()).AppendLine(";");
            sb.AppendLine();
        }

        // Re-open any containing types so a nested view still compiles.
        var nesting = new Stack<INamedTypeSymbol>();
        for (var outer = type.ContainingType; outer is not null; outer = outer.ContainingType)
            nesting.Push(outer);

        var indent = string.Empty;
        foreach (var outer in nesting)
        {
            sb.Append(indent).Append("partial ").Append(Keyword(outer)).Append(' ').Append(outer.Name).AppendLine();
            sb.Append(indent).AppendLine("{");
            indent += "    ";
        }

        sb.Append(indent).Append("partial ").Append(Keyword(type)).Append(' ').Append(type.Name)
          .AppendLine(" : global::SwiftDotNet.IInjectable");
        sb.Append(indent).AppendLine("{");

        // Implementing declarations for the partial form, each over its own backing field.
        foreach (var property in properties)
        {
            if (!property.IsPartialDefinition)
                continue;

            var optional = property.NullableAnnotation == NullableAnnotation.Annotated;
            var declared = property.Type.ToDisplayString(DeclaredTypeFormat);
            var field = BackingField(property);

            // The field is always nullable for reference types: it is null between construction and
            // injection, whatever the property's own annotation says.
            var fieldType = property.Type.IsReferenceType && !optional ? declared + "?" : declared;
            sb.Append(indent).Append("    private ").Append(fieldType).Append(' ').Append(field)
              .AppendLine(";");

            sb.Append(indent).Append("    ").Append(Accessibility(property)).Append(" partial ")
              .Append(declared).Append(' ').Append(property.Name).Append(" => ").Append(field);

            // A required reference-typed service reads as non-null, so fail loudly rather than handing
            // back null when nothing ever injected this view (see SDN1003).
            if (!optional && property.Type.IsReferenceType)
                sb.Append(" ?? throw new global::System.InvalidOperationException(\"'")
                  .Append(type.Name).Append('.').Append(property.Name)
                  .Append("' was read before it was injected. Only container-created views are injected; ")
                  .Append("inline children should use Service<T>() instead.\")");

            sb.AppendLine(";");
            sb.AppendLine();
        }

        sb.Append(indent).AppendLine("    void global::SwiftDotNet.IInjectable.Inject(global::System.IServiceProvider provider)");
        sb.Append(indent).AppendLine("    {");

        foreach (var property in properties)
        {
            // A nullable service is optional; anything else is required and throws when unregistered.
            var optional = property.NullableAnnotation == NullableAnnotation.Annotated;
            var serviceType = property.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
                .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            var target_ = property.IsPartialDefinition ? BackingField(property) : "this." + property.Name;

            sb.Append(indent).Append("        ").Append(target_)
              .Append(" = global::SwiftDotNet.SwiftHost.")
              .Append(optional ? "Optional<" : "Require<")
              .Append(serviceType)
              .AppendLine(">(provider);");
        }

        sb.Append(indent).AppendLine("    }");
        sb.Append(indent).AppendLine("}");

        for (var i = nesting.Count; i > 0; i--)
        {
            indent = indent.Substring(4);
            sb.Append(indent).AppendLine("}");
        }

        return sb.ToString();
    }

    static string BackingField(IPropertySymbol property) => "__inject_" + property.Name;

    /// <summary>The implementing declaration must repeat the declaring one's accessibility.</summary>
    static string Accessibility(IPropertySymbol property) => property.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        _ => "private",
    };

    static string Keyword(INamedTypeSymbol type) => type.IsRecord
        ? type.IsValueType ? "record struct" : "record"
        : type.IsValueType ? "struct" : "class";

    static string FileName(INamedTypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", string.Empty)
            .Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');

    static Location Location(ISymbol symbol) =>
        symbol.Locations.FirstOrDefault() ?? Microsoft.CodeAnalysis.Location.None;

    // A plain struct, not a record: netstandard2.0 has no IsExternalInit for init-only setters.
    readonly struct InjectTarget(INamedTypeSymbol type, ImmutableArray<IPropertySymbol> properties)
    {
        public INamedTypeSymbol Type { get; } = type;
        public ImmutableArray<IPropertySymbol> Properties { get; } = properties;
    }
}
