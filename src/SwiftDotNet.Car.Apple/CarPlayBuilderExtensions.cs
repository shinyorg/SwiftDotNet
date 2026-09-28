using CarPlay;
using ObjCRuntime;
using SwiftDotNet.Hosting;

namespace SwiftDotNet.CarPlay;

public static class CarPlayBuilderExtensions
{
    /// <summary>
    /// Opts the iOS head into CarPlay. Call it from the app delegate's <c>CreateSwiftApp</c>:
    /// <code>protected override SwiftDotNetApp CreateSwiftApp() => SwiftProgram.CreateSwiftApp(b => b.UseCarPlay());</code>
    /// </summary>
    /// <remarks>
    /// <para>This call is load-bearing even though it registers nothing. The only thing that names the CarPlay
    /// scene delegate is Info.plist, and a string in a plist is not a reference: without a C# call into this
    /// assembly the compiler drops the reference, the build leaves <c>SwiftDotNet.Car.Apple.dll</c> out of the
    /// app, <c>CarPlay.framework</c> is never linked, and connecting to a head unit fails inside FrontBoard with
    /// <c>unrealized specification class 'CPUITemplateApplicationSceneSpecification'</c> — the app's icon shows
    /// on the CarPlay screen but opens to nothing.</para>
    /// <para>Resolving the scene class here also loads the framework in the running process, which matters when
    /// the phone UI launched the app before a car connected.</para>
    /// </remarks>
    public static SwiftDotNetAppBuilder UseCarPlay(this SwiftDotNetAppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _ = Class.GetHandle(typeof(CPTemplateApplicationScene));
        _ = typeof(SwiftDotNetCarPlaySceneDelegate);
        return builder;
    }
}
