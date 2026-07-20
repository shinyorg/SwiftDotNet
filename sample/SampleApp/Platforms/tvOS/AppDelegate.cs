using Foundation;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;
using UIKit;

namespace SampleApp;

[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}

public static class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
