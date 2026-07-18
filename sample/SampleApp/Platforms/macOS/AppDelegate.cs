using AppKit;
using Foundation;
using SwiftDotNet;

namespace SampleApp;

[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    protected override View CreateRoot() => AppRoot.Create();
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
