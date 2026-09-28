using Android.OS;
using NativeWear = Com.Swiftdotnet.Wear.SwiftDotNetWear;

namespace SwiftDotNet;

/// <summary>
/// Turns on the Compose-for-Wear skin: lists become <c>ScalingLazyColumn</c>s, buttons and toggles become
/// chips, navigation pops on the system back swipe, and the root gets a Wear <c>Scaffold</c> with
/// <c>TimeText</c>. Everything else renders through the ordinary Compose interpreter.
///
/// <para><see cref="SwiftDotNetWearActivity"/> calls this for you. Call it yourself only when hosting with
/// <see cref="SwiftDotNetHost.CreateRootView"/> directly — and before that call, since the skin is read
/// when the root composes.</para>
/// </summary>
public static class SwiftDotNetWear
{
    /// <summary>Install the Wear overrides. Idempotent.</summary>
    public static void Install() => NativeWear.Install();
}

/// <summary>
/// The Wear OS counterpart of <see cref="SwiftDotNetActivity"/>: the same hosting, with the Wear skin
/// installed first.
/// <code>
/// [Activity(Label = "My Watch App", MainLauncher = true, Theme = "@android:style/Theme.DeviceDefault.NoActionBar")]
/// public sealed class MainActivity : SwiftDotNetWearActivity
/// {
///     protected override SwiftDotNetApp CreateSwiftApp() => WatchProgram.CreateSwiftApp();
/// }
/// </code>
/// </summary>
public abstract class SwiftDotNetWearActivity : SwiftDotNetActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SwiftDotNetWear.Install();
        base.OnCreate(savedInstanceState);
    }
}
