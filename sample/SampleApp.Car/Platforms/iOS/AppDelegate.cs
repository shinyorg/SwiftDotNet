using Foundation;
using SwiftDotNet;
using SwiftDotNet.CarPlay;
using SwiftDotNet.Hosting;
using UIKit;

namespace SampleApp.Car;

/// <summary>
/// A phone head plus <c>UseCarPlay()</c>. The scene manifest merged by SwiftDotNetCar.targets is what makes this a
/// CarPlay app: the phone window comes from SwiftDotNetSceneDelegate and the car from
/// SwiftDotNetCarPlaySceneDelegate, both over the app built here.
/// </summary>
[Register("AppDelegate")]
public sealed class AppDelegate : SwiftDotNetAppDelegate
{
    // UseCarPlay() is what keeps the CarPlay assembly (and CarPlay.framework) in the app — see its remarks.
    protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp(b => b.UseCarPlay());
}

public static class Program
{
    static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
