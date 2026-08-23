using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SwiftDotNet;

/// <summary>
/// Lets a WinUI-hosted tree show a real <see cref="FrameworkElement"/> the DSL has no node type for — most
/// usefully a .NET MAUI control realised through <c>MauiEmbedding</c>.
///
/// <para>The Windows counterpart of <c>ApplePlatformViews</c> / <c>AndroidPlatformViews</c>, and by far the
/// simplest of the three: WinUI is a C#-bindable toolkit, so there is no shim to cross and no handle to
/// marshal. It is a thin wrapper over the existing <see cref="WinRenderers"/> registry, spelled the same
/// way as the other two so the app-side wiring reads identically on every platform.</para>
///
/// <code>
/// // App startup, before the first render:
/// MauiEmbedding.Initialize(this, window);
/// WindowsPlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as FrameworkElement);
/// </code>
///
/// <para><b>Status:</b> like the rest of the WinUI backend, this has <b>never been compiled</b> — the
/// backend needs Windows to build and no Windows CI job covers it. See
/// <c>docs/backends/windows.md</c>.</para>
/// </summary>
public static class WindowsPlatformViews
{
    /// <summary>Supply native elements by node key. A null result renders an empty placeholder.</summary>
    public static void Register(Func<string, FrameworkElement?> provider)
        => WinRenderers.Register(
            "MauiView",
            ctx => provider(ctx.String("key")) ?? new Grid());
}
