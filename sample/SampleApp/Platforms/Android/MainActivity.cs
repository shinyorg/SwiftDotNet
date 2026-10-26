using Android.App;
using Android.Content.PM;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;

namespace SampleApp;

// ConfigurationChanges: folding/unfolding a foldable (and rotating) changes screen size and layout. Without
// these Android destroys and recreates the Activity — and with it the whole render tree and every State<T> —
// instead of letting Compose and WindowPosture re-lay the same tree out. [Activity] attributes are not
// inherited, so this can't live on SwiftDotNetActivity; see docs/getting-started.md.
[Activity(Label = "SwiftDotNet", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout | ConfigChanges.Orientation)]
public sealed class MainActivity : SwiftDotNetActivity
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}
