using System.Globalization;
using System.Runtime.Versioning;

namespace SwiftDotNet;

/// <summary>
/// The window's safe-area insets in points/dp — the margins that keep content clear of the status bar,
/// the display cutout/notch, the home indicator, and (via <see cref="Keyboard"/>) the soft keyboard.
/// Mirrors SwiftUI's <c>EnvironmentValues.safeAreaInsets</c> / Compose's <c>WindowInsets.safeDrawing</c>.
/// </summary>
/// <param name="Top">Inset from the top edge (status bar / notch).</param>
/// <param name="Leading">Inset from the leading edge (landscape cutout).</param>
/// <param name="Bottom">Inset from the bottom edge (home indicator / navigation bar).</param>
/// <param name="Trailing">Inset from the trailing edge (landscape cutout).</param>
/// <param name="Keyboard">Height the soft keyboard currently covers; 0 when dismissed.</param>
public readonly record struct SafeAreaInsets(
    double Top,
    double Leading,
    double Bottom,
    double Trailing,
    double Keyboard);

/// <summary>Which insets a safe-area modifier applies to, mirroring SwiftUI's <c>SafeAreaRegions</c>.</summary>
public enum SafeAreaRegions
{
    /// <summary>Only the container chrome — status bar, cutout, home indicator. Excludes the keyboard.</summary>
    Container,
    /// <summary>Only the soft keyboard.</summary>
    Keyboard,
    /// <summary>Both the container chrome and the keyboard.</summary>
    All,
}

/// <summary>
/// The live safe-area insets reported by the platform host, plus the availability guard for the
/// safe-area modifiers. <b>Mobile only</b> — safe area is a device-window concept, so this exists only
/// on iOS (SwiftUI) and Android (Compose).
///
/// <see cref="Current"/> participates in the render loop exactly like <see cref="State{T}"/>: the host
/// pushes new insets when they change (rotation, keyboard, cutout), which schedules a re-render, so a
/// <c>Body</c> that reads it is recomputed automatically.
///
/// <code>
/// if (SafeArea.IsSupported)                       // guards CA1416 from a neutral TFM
///     content = content.SafeAreaPadding(Edge.Top);
/// </code>
/// </summary>
public static class SafeArea
{
    /// <summary>
    /// The reserved event id the native hosts emit insets on. Node ids are structural paths rooted at
    /// <c>"0"</c> (see <c>RenderContext</c>), so a <c>$</c>-prefixed id can never collide with one.
    /// </summary>
    internal const string EventId = "$safeArea";

    static SafeAreaInsets _current;

    /// <summary>
    /// True on the platforms that report a safe area (iOS, Android). Branch on this before calling
    /// <c>.SafeAreaPadding</c>/<c>.IgnoresSafeArea</c>/<see cref="Current"/> from shared UI code —
    /// it's the platform guard the CA1416 analyzer recognizes.
    /// </summary>
    /// Mac Catalyst is excluded deliberately: <see cref="OperatingSystem.IsIOS"/> reports true there, but
    /// nothing in the Catalyst chain multi-targets into the SwiftUI shim, so there'd be no host reporting.
    ///
    /// The <c>SupportedOSPlatformGuard</c> attributes are what make this usable: they teach the platform-
    /// compatibility analyzer that code inside <c>if (SafeArea.IsSupported)</c> is iOS/Android-only, so the
    /// guarded call site stops warning CA1416.
    /// The <c>Unsupported</c> guard matters too: the analyzer treats Mac Catalyst as a subset of iOS, so
    /// without it a guarded call still warns "reachable on: 'maccatalyst'".
    [SupportedOSPlatformGuard("ios")]
    [SupportedOSPlatformGuard("android")]
    [UnsupportedOSPlatformGuard("maccatalyst")]
    public static bool IsSupported =>
        (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst()) || OperatingSystem.IsAndroid();

    /// <summary>
    /// The insets most recently reported by the host. All zero until the first report arrives — the
    /// host emits after its first layout pass, so a view's very first render sees zeros and is
    /// re-rendered once the real values land.
    /// </summary>
    [SupportedOSPlatform("ios")]
    [SupportedOSPlatform("android")]
    [UnsupportedOSPlatform("maccatalyst")]
    public static SafeAreaInsets Current => _current;

    /// <summary>
    /// Applies a host inset report (<c>"top;leading;bottom;trailing;keyboard"</c>). A payload equal to the
    /// current value is dropped without scheduling a render — both hosts report on every layout pass, so
    /// this de-duplication is what stops an inset report from spinning the render loop.
    /// </summary>
    internal static void Update(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
            return;

        var parts = payload.Split(';');
        if (parts.Length < 5)
            return;

        Span<double> v = stackalloc double[5];
        for (var i = 0; i < 5; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                return; // malformed report: keep the last known-good insets rather than half-applying

        var next = new SafeAreaInsets(v[0], v[1], v[2], v[3], v[4]);
        if (next == _current)
            return;

        _current = next;
        SwiftApp.RequestRender();
    }
}

static class SafeAreaTokens
{
    /// <summary>Comma-joined edge tokens (<c>"top,bottom"</c>) — the wire form both shims parse back into
    /// a SwiftUI <c>Edge.Set</c> / Compose <c>WindowInsetsSides</c>.</summary>
    public static string Token(this Edge edges)
    {
        if (edges == Edge.All)
            return "all";

        var parts = new List<string>(4);
        if (edges.HasFlag(Edge.Top)) parts.Add("top");
        if (edges.HasFlag(Edge.Leading)) parts.Add("leading");
        if (edges.HasFlag(Edge.Bottom)) parts.Add("bottom");
        if (edges.HasFlag(Edge.Trailing)) parts.Add("trailing");
        return string.Join(",", parts);
    }

    public static string Token(this SafeAreaRegions regions) => regions switch
    {
        SafeAreaRegions.Keyboard => "keyboard",
        SafeAreaRegions.All => "all",
        _ => "container",
    };
}
