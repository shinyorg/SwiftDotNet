using Foundation;
using SwiftDotNet;
using UIKit;

namespace SampleApp;

[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    protected override View CreateRoot()
    {
        AppleMaps.Register();   // activate the native MapKit renderer before the first render
        return AppRoot.Create();
    }
}

public static class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
