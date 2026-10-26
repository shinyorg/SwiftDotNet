using System.Globalization;
using System.Runtime.Versioning;

namespace SwiftDotNet;

/// <summary>What a device's fold is doing, as far as layout is concerned.</summary>
public enum FoldState
{
    /// <summary>No fold divides the window: the device has none, it is closed onto its outer display, or the
    /// host doesn't report one. This is the value on every backend without a posture reporter.</summary>
    None,
    /// <summary>The device has a fold and it is fully open. The window spans it seamlessly, so there is
    /// nothing to lay out around.</summary>
    Flat,
    /// <summary>The fold divides the window: the device is partly open (book or tabletop), or the hinge
    /// physically separates two displays. Only this state carries fold geometry.</summary>
    HalfOpen,
}

/// <summary>Which way the fold runs across the window.</summary>
public enum FoldOrientation
{
    /// <summary>No fold geometry (any state but <see cref="FoldState.HalfOpen"/>).</summary>
    None,
    /// <summary>A top-to-bottom fold: left and right halves, held like a book.</summary>
    Vertical,
    /// <summary>A side-to-side fold: top and bottom halves, stood on a table like a laptop.</summary>
    Horizontal,
}

/// <summary>
/// One posture report: the fold's state and geometry, the hinge angle where the platform has one, and the
/// size of the area the C# tree lays out in. All lengths are points/dp in the <b>root view's</b> coordinate
/// space — the window on Android (edge-to-edge), the safe area on iOS — so a view that fills the root can use
/// <see cref="FoldX"/>/<see cref="FoldY"/> directly.
/// </summary>
/// <param name="State">What the fold is doing. Only <see cref="FoldState.HalfOpen"/> carries geometry.</param>
/// <param name="Orientation">Which way the fold runs; <see cref="FoldOrientation.None"/> without geometry.</param>
/// <param name="FoldX">Left edge of the fold.</param>
/// <param name="FoldY">Top edge of the fold.</param>
/// <param name="FoldWidth">Width of the fold. 0 for a seamless (zero-width) fold, as on a Pixel Fold.</param>
/// <param name="FoldHeight">Height of the fold.</param>
/// <param name="HingeAngleDegrees">
/// The hinge angle (180 = flat), or <c>null</c> where the platform doesn't report one (Android, Web). <b>For
/// effects only</b> — a lid-angle animation, say. Apple is explicit that the angle is not a layout input: it
/// changes continuously while the device moves. Lay out from <see cref="State"/> and the fold geometry.
/// </param>
/// <param name="WindowWidth">Width of the root layout area; 0 when the host doesn't report it.</param>
/// <param name="WindowHeight">Height of the root layout area; 0 when the host doesn't report it.</param>
public readonly record struct PostureInfo(
    FoldState State,
    FoldOrientation Orientation,
    double FoldX,
    double FoldY,
    double FoldWidth,
    double FoldHeight,
    double? HingeAngleDegrees,
    double WindowWidth = 0,
    double WindowHeight = 0)
{
    /// <summary>No fold, no window size — what every host reports before (or instead of) a real report.</summary>
    public static PostureInfo None => default;

    /// <summary>True when a fold divides the window and its geometry is meaningful.</summary>
    public bool HasFold => State == FoldState.HalfOpen;

    /// <summary>A horizontal fold dividing the window: top and bottom halves (laptop / tabletop).</summary>
    public bool IsTabletop => HasFold && Orientation == FoldOrientation.Horizontal;

    /// <summary>A vertical fold dividing the window: left and right halves (book).</summary>
    public bool IsBook => HasFold && Orientation == FoldOrientation.Vertical;

    /// <summary>True when the host reported the root area's size.</summary>
    public bool HasWindowSize => WindowWidth > 0 && WindowHeight > 0;
}

/// <summary>
/// The live window posture — fold state and geometry plus the root area's size — reported by the platform
/// host. Built exactly like <see cref="SafeArea"/>: hosts push a report on the reserved <c>$posture</c> event
/// id, <see cref="Current"/> changes, and a render is scheduled, so a <c>Body</c> that reads it is recomputed
/// automatically. <see cref="TwoPaneView"/> is the main consumer.
///
/// <para><b>Who reports.</b> The SwiftUI host on iOS (window size always; the fold from iOS 27.1 on iPhone
/// Duo), the Compose host on Android (Jetpack WindowManager), the Blazor host on the web
/// (<c>navigator.devicePosture</c> + <c>window.viewport.segments</c>, Chromium only) and the Skia MAUI host on
/// Android/iOS. Everything else stays at <see cref="PostureInfo.None"/>. A host of your own (Unity, a test,
/// a preview) feeds <see cref="Report"/>.</para>
///
/// <para>v1 is one posture per process — per-window posture waits on multi-window support.</para>
/// </summary>
public static class WindowPosture
{
    /// <summary>
    /// The reserved event id hosts emit posture reports on. Node ids are structural paths rooted at
    /// <c>"0"</c>, so a <c>$</c>-prefixed id can never collide with one (same trick as <see cref="SafeArea"/>).
    /// </summary>
    internal const string EventId = "$posture";

    static PostureInfo _current;

    /// <summary>
    /// True on the platforms whose host can report a fold (iOS, Android, the browser). Informational:
    /// <see cref="Current"/> is safe to read everywhere and is simply <see cref="PostureInfo.None"/> where
    /// nothing reports.
    /// </summary>
    /// Mac Catalyst is excluded for the same reason as <see cref="SafeArea.IsSupported"/>: nothing in the
    /// Catalyst chain multi-targets into the SwiftUI shim.
    [SupportedOSPlatformGuard("ios")]
    [SupportedOSPlatformGuard("android")]
    [SupportedOSPlatformGuard("browser")]
    [UnsupportedOSPlatformGuard("maccatalyst")]
    public static bool IsSupported =>
#if NETSTANDARD2_1
        // Unity: no OperatingSystem probes before .NET 5, and Unity's host would feed Report() itself.
        false;
#else
        (OperatingSystem.IsIOS() && !OperatingSystem.IsMacCatalyst()) || OperatingSystem.IsAndroid()
        || OperatingSystem.IsBrowser();
#endif

    /// <summary>
    /// The posture most recently reported. <see cref="PostureInfo.None"/> until the first report lands — hosts
    /// report after their first layout pass, so a view's very first render sees no fold and no window size,
    /// and is re-rendered once the real values arrive.
    /// </summary>
    public static PostureInfo Current => _current;

    /// <summary>
    /// Push a posture from a host that isn't one of the built-in reporters — a Skia host reading the
    /// platform API, Unity, a preview, a test. A value equal to <see cref="Current"/> is dropped without
    /// scheduling a render. Call it on the UI thread.
    /// </summary>
    public static void Report(PostureInfo value)
    {
        if (value == _current)
            return;

        _current = value;
        SwiftApp.RequestRender();
    }

    /// <summary>
    /// Applies a host report: <c>"state;orientation;x;y;w;h;angle;windowW;windowH"</c>, invariant culture.
    /// <c>state</c> is <c>none|flat|halfOpen</c>, <c>orientation</c> is <c>none|vertical|horizontal</c>, and an
    /// empty <c>angle</c> means "no hinge angle". A malformed report is dropped and the last good value kept;
    /// an unchanged one doesn't render (hosts report on every layout pass).
    /// </summary>
    internal static void Update(string? payload)
    {
        if (TryParse(payload, out var next))
            Report(next);
    }

    internal static bool TryParse(string? payload, out PostureInfo value)
    {
        value = default;
        if (string.IsNullOrEmpty(payload))
            return false;

        var parts = payload!.Split(';');
        if (parts.Length < 9)
            return false;

        FoldState state;
        switch (parts[0])
        {
            case "none": state = FoldState.None; break;
            case "flat": state = FoldState.Flat; break;
            case "halfOpen": state = FoldState.HalfOpen; break;
            default: return false;
        }

        FoldOrientation orientation;
        switch (parts[1])
        {
            case "none": orientation = FoldOrientation.None; break;
            case "vertical": orientation = FoldOrientation.Vertical; break;
            case "horizontal": orientation = FoldOrientation.Horizontal; break;
            default: return false;
        }

        Span<double> geometry = stackalloc double[4];
        for (var i = 0; i < 4; i++)
            if (!TryNumber(parts[2 + i], out geometry[i]))
                return false;

        double? angle = null;
        if (parts[6].Length > 0)
        {
            if (!TryNumber(parts[6], out var a))
                return false;
            angle = a;
        }

        if (!TryNumber(parts[7], out var windowWidth) || !TryNumber(parts[8], out var windowHeight))
            return false;

        // Geometry only means something while the fold divides the window; normalise it away otherwise so
        // two "no fold" reports that differ only in stale coordinates compare equal and don't re-render.
        if (state != FoldState.HalfOpen)
        {
            orientation = FoldOrientation.None;
            geometry.Clear();
        }

        value = new PostureInfo(state, orientation, geometry[0], geometry[1], geometry[2], geometry[3], angle,
            windowWidth, windowHeight);
        return true;
    }

    static bool TryNumber(string s, out double d)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && !double.IsNaN(d)
           && !double.IsInfinity(d);
}
