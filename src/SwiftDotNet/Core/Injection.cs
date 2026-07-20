namespace SwiftDotNet;

/// <summary>
/// Marks a property on a <see cref="View"/> as a service to be supplied from the container.
///
/// The fill is emitted by the SwiftDotNet source generator as plain static assignments — no reflection —
/// which is what keeps property injection trim/AOT-clean under iOS AOT. The containing type must be
/// <c>partial</c> so the generated <see cref="IInjectable"/> implementation can be attached to it.
///
/// A nullable property (<c>IFoo?</c>) resolves optionally; a non-nullable one is required and throws
/// when unregistered.
///
/// <para><b>Constructor parameters carry parent data; <c>[Inject]</c> properties carry services.</b>
/// Constructor injection of services still works for views the container builds (the root, and pages
/// pushed via <c>INavigator.PushAsync&lt;T&gt;()</c>), but <c>[Inject]</c> is the documented default.</para>
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class InjectAttribute : Attribute;

/// <summary>
/// Implemented by the source generator for every view that declares <see cref="InjectAttribute"/>
/// members. Hosts call it once per <b>retained</b> view instance — the root and pushed pages — right
/// after construction.
/// </summary>
/// <remarks>
/// Views built inline inside a <c>Body</c> are rebuilt every render and are not retained, so nothing
/// injects them; the generator reports <c>SDN1003</c> for that case rather than letting the members
/// silently stay null. Those views should use <see cref="View.Service{TService}"/> instead, until
/// view-instance reconciliation lands.
/// </remarks>
public interface IInjectable
{
    /// <summary>Fill this instance's <c>[Inject]</c> members from <paramref name="provider"/>.</summary>
    void Inject(IServiceProvider provider);
}

/// <summary>Convenience for the one-line "inject if it wants it" call hosts make.</summary>
public static class InjectableExtensions
{
    /// <summary>
    /// Fills <paramref name="view"/>'s <c>[Inject]</c> members when it implements <see cref="IInjectable"/>;
    /// a no-op otherwise. Returns the view so it can be used inline.
    /// </summary>
    public static TView InjectServices<TView>(this TView view, IServiceProvider provider) where TView : View
    {
        if (view is IInjectable injectable)
            injectable.Inject(provider);
        return view;
    }
}
