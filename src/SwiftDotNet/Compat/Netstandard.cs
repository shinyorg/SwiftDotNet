// Compiled only for netstandard2.1 (the Unity target). These types exist in .NET 5+ but not in
// netstandard2.1, and the C# compiler requires them to be *present* — not to come from any particular
// assembly — to emit `init` accessors, `required` members, and the trimming annotations Core already
// carries. Declaring them here is the standard polyfill and changes no behaviour on any other target.
//
// See docs/backends/unity.md for why Core multi-targets netstandard2.1 at all.

#if NETSTANDARD2_1

// ReSharper disable once CheckNamespace
namespace System.Runtime.CompilerServices
{
    /// <summary>Marker the compiler needs to emit <c>init</c>-only setters.</summary>
    internal static class IsExternalInit;

    /// <summary>
    /// Lets a defaulted parameter capture the source text of another argument — what gives
    /// <c>Throw.IfNull</c> its parameter name without the caller passing one.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute
    {
        public string ParameterName { get; } = parameterName;
    }

    /// <summary>Marks a member the compiler enforces as <c>required</c>.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute;

    /// <summary>
    /// Tells a compiler which language features an API depends on. Emitted alongside
    /// <see cref="RequiredMemberAttribute"/>; a compiler that does not understand the feature refuses the
    /// reference rather than mis-compiling against it.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute(string featureName) : Attribute
    {
        public const string RefStructs = nameof(RefStructs);
        public const string RequiredMembers = nameof(RequiredMembers);

        public string FeatureName { get; } = featureName;
        public bool IsOptional { get; init; }
    }
}

// ReSharper disable once CheckNamespace
namespace System.Runtime.Versioning
{
    /// <summary>Declares the platforms an API supports. Purely informational at runtime.</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformAttribute(string platformName) : Attribute
    {
        public string PlatformName { get; } = platformName;
    }

    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class UnsupportedOSPlatformAttribute(string platformName) : Attribute
    {
        public string PlatformName { get; } = platformName;
    }

    /// <summary>Marks a member whose truth implies the annotated platform is available.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property,
        AllowMultiple = true, Inherited = false)]
    internal sealed class SupportedOSPlatformGuardAttribute(string platformName) : Attribute
    {
        public string PlatformName { get; } = platformName;
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property,
        AllowMultiple = true, Inherited = false)]
    internal sealed class UnsupportedOSPlatformGuardAttribute(string platformName) : Attribute
    {
        public string PlatformName { get; } = platformName;
    }
}

// ReSharper disable once CheckNamespace
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Declares that a constructor sets all required members, so callers need not.</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute;

    /// <summary>Which members the trimmer must preserve on an annotated type.</summary>
    [Flags]
    internal enum DynamicallyAccessedMemberTypes
    {
        None = 0,
        PublicParameterlessConstructor = 0x0001,
        PublicConstructors = 0x0002 | PublicParameterlessConstructor,
        NonPublicConstructors = 0x0004,
        PublicMethods = 0x0008,
        NonPublicMethods = 0x0010,
        PublicFields = 0x0020,
        NonPublicFields = 0x0040,
        PublicNestedTypes = 0x0080,
        NonPublicNestedTypes = 0x0100,
        PublicProperties = 0x0200,
        NonPublicProperties = 0x0400,
        PublicEvents = 0x0800,
        NonPublicEvents = 0x1000,
        Interfaces = 0x2000,
        All = ~None,
    }

    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter |
        AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Method |
        AttributeTargets.Class | AttributeTargets.Interface | AttributeTargets.Struct,
        Inherited = false)]
    internal sealed class DynamicallyAccessedMembersAttribute(DynamicallyAccessedMemberTypes memberTypes) : Attribute
    {
        public DynamicallyAccessedMemberTypes MemberTypes { get; } = memberTypes;
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute
    {
        public string Message { get; } = message;
        public string? Url { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresDynamicCodeAttribute(string message) : Attribute
    {
        public string Message { get; } = message;
        public string? Url { get; set; }
    }
}

#endif
