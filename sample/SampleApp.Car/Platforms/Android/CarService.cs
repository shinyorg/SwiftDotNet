using Android.App;
using Android.Content;
using SwiftDotNet.AndroidAuto;
using SwiftDotNet.Hosting;

namespace SampleApp.Car;

/// <summary>
/// The Android Auto entry point. The category is app-specific, so it is declared here; the session, screens
/// and host validation come from <see cref="SwiftDotNetCarAppService"/>. Android can start this service with
/// no activity running, which is why it builds the app too — <c>SwiftDotNetApp.GetOrCreate</c> makes sure the
/// activity and the car share the one container.
/// </summary>
[Service(Exported = true, Name = "com.swiftdotnet.sample.car.CarService")]
[IntentFilter(["androidx.car.app.CarAppService"], Categories = ["androidx.car.app.category.CHARGING"])]
public class CarService : SwiftDotNetCarAppService
{
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp();
}
