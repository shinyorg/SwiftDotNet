using Android.App;
using SwiftDotNet;
using SwiftDotNet.Hosting;

namespace SampleApp.Wear;

[Activity(Label = "SwiftDotNet Wear", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar")]
public sealed class MainActivity : SwiftDotNetWearActivity
{
    protected override SwiftDotNetApp CreateSwiftApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.UseSwiftApp(_ => new WatchRootView());
        return builder.Build();
    }
}
