using Android.App;
using SwiftDotNet;

namespace SampleApp;

[Activity(Label = "SwiftDotNet", MainLauncher = true)]
public sealed class MainActivity : SwiftDotNetActivity
{
    protected override View CreateRoot() => AppRoot.Create();
}
