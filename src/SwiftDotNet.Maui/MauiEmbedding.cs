using Microsoft.Maui.Controls.Embedding;
using Microsoft.Maui.Hosting;
using MauiControl = Microsoft.Maui.Controls.View;

namespace SwiftDotNet;

/// <summary>
/// Runs MAUI's handler infrastructure inside an app that is <b>not</b> a MAUI app, so a
/// <see cref="MauiView"/> can be realised on a backend where SwiftDotNet is the host and MAUI is the guest
/// — the iOS/SwiftUI, Android/Compose and WinUI backends.
///
/// <para>This is the inverse of <see cref="MauiPlatformViewLayer"/> and is much the harder direction. When
/// SwiftDotNet is hosted <em>inside</em> a MAUI app, MAUI is already running and an embedded control is
/// just another child in the same layout — nothing here is needed. When SwiftDotNet owns the app, MAUI has
/// no application object, no service provider and no <c>IMauiContext</c>, and a
/// <c>Microsoft.Maui.Controls.View</c> is inert until all three exist. That is what this creates.</para>
///
/// <code>
/// // in the iOS AppDelegate / Android Activity, before the first render:
/// MauiEmbedding.Initialize();
/// ApplePlatformViews.Register(key => MauiEmbedding.PlatformHandle(key));   // iOS
/// </code>
///
/// <para><b>Status:</b> compiles; <b>never run</b>. Everything below the API surface — whether a handler
/// created this way behaves outside a MAUI window, how it participates in the host's layout pass, what
/// happens to its lifecycle events — is unverified. The direction that <em>is</em> exercised is the other
/// one; see <see cref="MauiPlatformViewLayer"/>.</para>
/// </summary>
public static class MauiEmbedding
{
    static MauiApp? _app;
    static IMauiContext? _context;
    static readonly Dictionary<string, MauiControl> Realised = new(StringComparer.Ordinal);

    /// <summary>The embedded MAUI app, or null before <see cref="Initialize"/>.</summary>
    public static MauiApp? App => _app;

    /// <summary>The context handlers are created against, or null before <see cref="Initialize"/>.</summary>
    public static IMauiContext? Context => _context;

    /// <summary>
    /// Stand MAUI up inside a non-MAUI app. Call once, on the UI thread, before the first render.
    ///
    /// <para>The signature is platform-specific because embedding is: MAUI needs the real application
    /// object to install its handlers against, and the real window to scope the context to. There is no
    /// cross-platform spelling of either, and pretending otherwise would only move the <c>#if</c> into
    /// every caller.</para>
    /// </summary>
    /// <param name="configure">
    /// Register handlers, fonts and services exactly as a <c>MauiProgram</c> would. The embedding builder
    /// has already been applied when this runs.
    /// </param>
#if IOS || MACCATALYST
    public static void Initialize(
        UIKit.IUIApplicationDelegate platformApplication,
        UIKit.UIWindow platformWindow,
        Action<MauiAppBuilder>? configure = null)
#elif ANDROID
    public static void Initialize(
        Android.App.Application platformApplication,
        Android.App.Activity platformWindow,
        Action<MauiAppBuilder>? configure = null)
#elif WINDOWS
    public static void Initialize(
        Microsoft.UI.Xaml.Application platformApplication,
        Microsoft.UI.Xaml.Window platformWindow,
        Action<MauiAppBuilder>? configure = null)
#endif
    {
        if (_app is not null) return;

        var builder = MauiApp.CreateBuilder();
        // UseMauiEmbeddedApp is the public entry point for native embedding (it lives in
        // Microsoft.Maui.Controls.Xaml; the same-named Microsoft.Maui.Embedding.EmbeddingExtensions in
        // Microsoft.Maui.dll is `internal`, which is the trap here — code targeting it compiles nowhere).
        // It injects the embedded handler collection into the service collection, so handlers work with no
        // MAUI Application driving the window.
        builder.UseMauiEmbeddedApp<Microsoft.Maui.Controls.Application>();
        configure?.Invoke(builder);
        _app = builder.Build();
        _context = _app.CreateEmbeddedWindowContext(platformWindow);
    }

    /// <summary>
    /// Realise the control for an identity as its <b>platform</b> view — a <c>UIView</c>, an
    /// <c>android.view.View</c> or a <c>FrameworkElement</c> depending on the TFM — or null when the
    /// identity is unknown or MAUI has not been initialised.
    ///
    /// <para>Returned as <see cref="object"/> deliberately: this assembly must not reference any
    /// SwiftDotNet <em>backend</em>. If it did, an app using the Skia MAUI host would end up with two copies
    /// of Core in its graph — the neutral one the Skia chain resolves and a platform one this reference
    /// would drag in. The caller already knows the platform type it wants.</para>
    /// </summary>
    public static object? CreatePlatformView(string key)
    {
        if (_context is null) return null;
        if (!Realised.TryGetValue(key, out var control))
        {
            if (MauiViewRegistry.Create(key) is not { } created) return null;
            Realised[key] = control = created;
        }
        MauiViewRegistry.Update(key, control);
        return control.ToPlatformEmbedded(_context);
    }

    /// <summary>Push current values into an already-realised control, without rebuilding it.</summary>
    public static void Refresh(string key)
    {
        if (Realised.TryGetValue(key, out var control)) MauiViewRegistry.Update(key, control);
    }

    /// <summary>
    /// Forget an identity and let its handler go. The native-shim backends have no equivalent of the
    /// platform-view placement set, so unlike <see cref="MauiPlatformViewLayer"/> they cannot detect a
    /// removed node — the app calls this when it knows a view is gone.
    /// </summary>
    public static void Release(string key)
    {
        if (Realised.Remove(key, out var control)) control.Handler?.DisconnectHandler();
        MauiViewRegistry.Release(key);
    }
}
