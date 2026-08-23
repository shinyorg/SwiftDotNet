using Com.Swiftdotnet.Bridge;
using AndroidView = Android.Views.View;
using NativeBridge = Com.Swiftdotnet.Bridge.SwiftDotNetBridge;

namespace SwiftDotNet;

/// <summary>
/// Lets a Compose-hosted tree show a real <see cref="AndroidView"/> the DSL has no node type for — most
/// usefully a .NET MAUI control realised through <c>MauiEmbedding</c>.
///
/// <para>The Android counterpart of <c>ApplePlatformViews</c>, and the same shape: the Kotlin bridge asks
/// for a view by node key and wraps whatever comes back in a Compose <c>AndroidView</c>. Nothing in the
/// bridge knows what MAUI is.</para>
///
/// <code>
/// // Activity.OnCreate, before the first render:
/// MauiEmbedding.Initialize(Application, this);
/// AndroidPlatformViews.Register(key => MauiEmbedding.CreatePlatformView(key) as AndroidView);
/// </code>
///
/// <para>Until <see cref="Register"/> is called, a <c>MauiView</c> node renders an empty placeholder view
/// rather than crashing — the same graceful fallback an unregistered custom renderer gets.</para>
/// </summary>
public static class AndroidPlatformViews
{
    static ProviderProxy? _proxy;

    /// <summary>Supply native views by node key. A null result renders an empty view.</summary>
    public static void Register(Func<string, AndroidView?> provider)
    {
        // Held in a static so the Java peer isn't collected while Kotlin still holds the interface.
        _proxy = new ProviderProxy(provider);
        NativeBridge.SetPlatformViewProvider(_proxy);
    }

    /// <summary>Stop serving platform views; nodes fall back to the placeholder.</summary>
    public static void Unregister()
    {
        NativeBridge.SetPlatformViewProvider(null);
        _proxy?.Dispose();
        _proxy = null;
    }

    /// <summary>Bridges the Kotlin <c>PlatformViewProvider</c> interface to a managed delegate.</summary>
    sealed class ProviderProxy : Java.Lang.Object, IPlatformViewProvider
    {
        readonly Func<string, AndroidView?> _provider;
        public ProviderProxy(Func<string, AndroidView?> provider) => _provider = provider;

        public AndroidView? ViewFor(string key)
        {
            // This runs inside a Compose composition on the UI thread; an exception crossing back into
            // Kotlin would take the process down rather than surface anything useful.
            try { return _provider(key); }
            catch { return null; }
        }
    }
}
