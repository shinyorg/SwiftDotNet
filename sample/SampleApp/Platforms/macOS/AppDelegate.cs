using AppKit;
using Foundation;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;

namespace SampleApp;

[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    protected override SwiftDotNetApp CreateSwiftApp() =>
        SwiftProgram.CreateSwiftApp(b => b.UseAppleMaps());
}

static class Program
{
    static void Main(string[] args)
    {
        NSApplication.Init();
        NSApplication.SharedApplication.Delegate = new AppDelegate();
        NSApplication.Main(args);
    }
}
