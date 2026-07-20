using Android.App;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;

namespace SampleApp;

[Activity(Label = "SwiftDotNet", MainLauncher = true)]
public sealed class MainActivity : SwiftDotNetActivity
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}
