using Android.App;
using SwiftDotNet;
using SwiftDotNet.Hosting;

namespace SampleApp.Car;

[Activity(Label = "VoltFinder", MainLauncher = true)]
public sealed class MainActivity : SwiftDotNetActivity
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}
