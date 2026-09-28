using SwiftDotNet.Hosting;

namespace SwiftDotNet.Sample.Watch;

/// <summary>
/// The watch app's composition root. The watch SDK's generated bootstrap calls <see cref="CreateSwiftApp"/>
/// (override with the <c>SwiftDotNetWatchAppFactory</c> property) — the same shape every other head uses.
/// </summary>
public static class SwiftProgram
{
    public static SwiftDotNetApp CreateSwiftApp()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        // Explicit factory rather than UseSwiftApp<T>(): no constructor discovery, so it is NativeAOT-clean.
        builder.UseSwiftApp(_ => new WatchRootView());
        return builder.Build();
    }
}
