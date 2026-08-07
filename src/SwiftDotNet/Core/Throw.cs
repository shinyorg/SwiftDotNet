using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace SwiftDotNet;

/// <summary>
/// Argument guards, as one helper rather than scattered <c>ArgumentNullException.ThrowIfNull</c> calls.
/// </summary>
/// <remarks>
/// Those BCL statics are .NET 6+, and Core also targets netstandard2.1 for Unity. Routing every guard
/// through here keeps the call sites identical on every target instead of sprinkling <c>#if</c> through
/// the hosting and styles code. Behaviour and the resulting exception are unchanged.
/// </remarks>
static class Throw
{
    /// <summary>Throws <see cref="ArgumentNullException"/> when <paramref name="argument"/> is null.</summary>
    public static void IfNull(
        [NotNull] object? argument,
        [CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        if (argument is null) throw new ArgumentNullException(paramName);
    }

    /// <summary>Throws <see cref="ObjectDisposedException"/> when <paramref name="condition"/> holds.</summary>
    public static void IfDisposed(bool condition, object instance)
    {
        if (condition) throw new ObjectDisposedException(instance.GetType().FullName);
    }
}
