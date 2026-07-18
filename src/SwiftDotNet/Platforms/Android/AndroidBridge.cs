using Android.Content;
using Com.Swiftdotnet.Bridge;
using AndroidView = Android.Views.View;
using NativeBridge = Com.Swiftdotnet.Bridge.SwiftDotNetBridge;

namespace SwiftDotNet;

/// <summary>
/// The Android implementation of <see cref="IBridge"/>: calls the Kotlin/Compose bridge over JNI
/// and routes Compose events back into managed callbacks. Internal — apps use <see cref="SwiftDotNetHost"/>.
/// </summary>
internal sealed class AndroidBridge : IBridge
{
    /// <summary>Builds the Compose-backed host <see cref="AndroidView"/>.</summary>
    public AndroidView CreateHostView(Context context) => NativeBridge.CreateHostView(context);

    public void Render(string json) => NativeBridge.Render(json);

    public void SetEventHandler(Action<string, string?> handler)
        => NativeBridge.SetEventCallback(new EventProxy(handler));

    /// <summary>Bridges the Kotlin <c>EventCallback</c> interface to a managed delegate.</summary>
    sealed class EventProxy : Java.Lang.Object, IEventCallback
    {
        readonly Action<string, string?> _handler;
        public EventProxy(Action<string, string?> handler) => _handler = handler;
        public void OnEvent(string id, string? value) => _handler(id, value);
    }
}
