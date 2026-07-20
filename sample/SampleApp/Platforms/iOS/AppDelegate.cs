using Foundation;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;
using UIKit;

namespace SampleApp;

[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    // The head does nothing but build the shared app, adding the renderers only iOS can offer.
    protected override SwiftDotNetApp CreateSwiftApp() =>
        SwiftProgram.CreateSwiftApp(b => b.UseAppleMaps());
}

public static class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
