using Android.OS;
using AndroidX.Activity;

namespace SwiftDotNet;

/// <summary>
/// Reusable Android activity that hosts a SwiftDotNet root view as real Jetpack Compose. Subclass it,
/// mark it the launcher, and return your root — the Compose host-view wiring is done for you:
/// <code>
/// [Activity(Label = "SwiftDotNet", MainLauncher = true)]
/// public sealed class MainActivity : SwiftDotNetActivity
/// {
///     protected override View CreateRoot() => new ContentView();
/// }
/// </code>
/// </summary>
public abstract class SwiftDotNetActivity : ComponentActivity
{
    /// <summary>Return the root view for the app. Called once during <see cref="OnCreate"/>.</summary>
    protected abstract View CreateRoot();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(SwiftDotNetHost.CreateRootView(this, CreateRoot()));
    }
}
