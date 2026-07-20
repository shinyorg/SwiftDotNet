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
///     protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
/// }
/// </code>
/// </summary>
public abstract class SwiftDotNetActivity : ComponentActivity
{
    /// <summary>
    /// Build the app — services, logging and the root view. Called once during <see cref="OnCreate"/>.
    /// The MAUI analog of <c>CreateMauiApp()</c>; put the body in a shared <c>SwiftProgram</c>.
    /// </summary>
    protected abstract Hosting.SwiftDotNetApp CreateSwiftApp();

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var app = CreateSwiftApp();
        SetContentView(SwiftDotNetHost.CreateRootView(this, app.CreateRoot(), app.Services));
    }
}
