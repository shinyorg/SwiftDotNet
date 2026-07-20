namespace SwiftDotNet.Hosting;

/// <summary>
/// A service that needs to run initialization work once its provider is available.
///
/// The SwiftDotNet analog of MAUI's <c>IMauiInitializeService</c> / <c>IMauiInitializeScopedService</c>
/// pair — collapsed into <b>one</b> interface, with a <c>scoped</c> flag telling the implementation
/// which kind of provider it is being handed. That avoids MAUI's situation where a service that wants
/// both has to implement two near-identical interfaces.
/// </summary>
/// <remarks>
/// Registration order is invocation order, so an initializer that depends on another's work should be
/// registered after it.
/// <code>
/// builder.Services.AddSingleton&lt;ISwiftInitializer, MapsInitializer&gt;();
/// </code>
/// </remarks>
public interface ISwiftInitializer
{
    /// <summary>
    /// Run initialization against <paramref name="services"/>.
    /// </summary>
    /// <param name="services">
    /// The provider to initialize against — the app's root provider when <paramref name="scoped"/> is
    /// false, or a newly created scope's provider when it is true.
    /// </param>
    /// <param name="scoped">
    /// <c>false</c> for the one-time application-wide call made during
    /// <see cref="SwiftDotNetAppBuilder.Build"/>; <c>true</c> for each per-scope call (e.g. a navigation
    /// page's scope), which happens every time a scope is created.
    /// </param>
    void Initialize(IServiceProvider services, bool scoped);
}
