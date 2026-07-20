namespace SwiftDotNet;

/// <summary>
/// An observer of view lifecycle events. Registered in the container; every registered implementation
/// is called for <b>every</b> retained view, with the view passed in.
///
/// <code>
/// builder.Services.AddSingleton&lt;IViewLifecycle, AnalyticsViewLifecycle&gt;();
/// </code>
///
/// This is the cross-cutting seam — analytics, logging, telemetry, and the framework's own
/// dependency injection all ride on it. A view that only cares about *itself* should instead override
/// the <c>OnCreated</c> / <c>OnAppearing</c> / <c>OnDisappearing</c> / <c>OnDestroyed</c> methods on
/// <see cref="View"/>; both are raised by the same dispatch (see <see cref="View.OnCreated"/>).
/// </summary>
/// <remarks>
/// <para><b>Ordering.</b> Setup events run observers first, then the view — so services (including the
/// framework's own <c>[Inject]</c> fill) are in place before the view's own code runs. Teardown events
/// run the view first, then observers, mirroring construction/disposal order.</para>
/// <para><b>Only retained views raise these.</b> Views built inline inside a <c>Body</c> are
/// reconstructed on every render pass, so "created"/"destroyed" would fire continuously and mean
/// nothing. Until view-instance reconciliation lands, only the root — and, once the paused navigation
/// plan lands, each pushed page — participates.</para>
/// <para>Callbacks run on the UI thread.</para>
/// </remarks>
public interface IViewLifecycle
{
    /// <summary>The view has been constructed. Fires once, before the first render.</summary>
    void OnCreated(View view);

    /// <summary>The view became visible, or visible again. Can fire more than once.</summary>
    void OnAppearing(View view);

    /// <summary>The view is no longer visible. Always paired with a prior <see cref="OnAppearing"/>.</summary>
    void OnDisappearing(View view);

    /// <summary>
    /// The view has been torn down and will not be shown again. Fires once, after <see cref="OnDisappearing"/>
    /// and — when the view owns a service scope — before that scope is disposed.
    /// </summary>
    void OnDestroyed(View view);
}
