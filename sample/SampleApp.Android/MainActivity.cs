using Android.App;
using Android.OS;
using AndroidX.Activity;
using SwiftDotNet;
using SwiftDotNet.Sample;

namespace AndroidSample;

[Activity(Label = "SwiftDotNet", MainLauncher = true)]
public class MainActivity : ComponentActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // The SAME 5-tab ContentView the iOS app renders as SwiftUI — here as Jetpack Compose.
        SetContentView(SwiftDotNetHost.CreateRootView(this, new ContentView()));
    }
}
