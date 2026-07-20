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
        // Draw behind the system bars so `WindowInsets.safeDrawing` reports real values — and because
        // Android 15+ enforces edge-to-edge for apps targeting SDK 35 anyway. Must run before
        // base.OnCreate so the decor-view flags are in place when Compose first measures.
        // Content stays clear of the bars via `.SafeAreaPadding(...)` — see docs/backends/android.md.
        EdgeToEdge.Enable(this);
        base.OnCreate(savedInstanceState);
        var app = CreateSwiftApp();
        SetContentView(SwiftDotNetHost.CreateRootView(this, app.CreateRoot(), app.Services));
    }
}
