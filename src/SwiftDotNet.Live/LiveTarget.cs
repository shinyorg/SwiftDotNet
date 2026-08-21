namespace SwiftDotNet;

/// <summary>Which host will render a live tree. Several rules differ by platform, not just by surface.</summary>
public enum LivePlatform
{
    Apple,
    Android,
}

/// <summary>
/// Where a live tree is going. The validator needs all of this because the constraints are not a
/// property of the tree alone — the same tree is legal as an Android notification, illegal as an Apple
/// accessory widget, and merely wasteful as a Live Activity.
/// </summary>
public sealed record LiveTarget
{
    /// <summary>The system surface.</summary>
    public LiveSurface Surface { get; init; } = LiveSurface.Activity;

    /// <summary>The rendering host.</summary>
    public LivePlatform Platform { get; init; } = LivePlatform.Apple;

    /// <summary>For <see cref="LiveSurface.Widget"/>, which family this tree fills.</summary>
    public WidgetFamily? Family { get; init; }

    /// <summary>
    /// The app's Android <c>minSdk</c>. Runtime sizing (<c>setViewLayoutWidth</c> and friends) landed in
    /// API 31, so a lower floor turns <c>.Frame(…)</c> into a silent no-op on old devices — the validator
    /// says so rather than letting it be discovered on a device.
    /// </summary>
    public int AndroidMinSdk { get; init; } = 31;

    /// <summary>Convenience for the common Apple Live Activity target.</summary>
    public static LiveTarget AppleActivity { get; } = new() { Surface = LiveSurface.Activity, Platform = LivePlatform.Apple };

    /// <summary>Convenience for an Android custom-content notification.</summary>
    public static LiveTarget AndroidNotification { get; } = new() { Surface = LiveSurface.Notification, Platform = LivePlatform.Android };
}

/// <summary>
/// Widget shapes. Apple's are a fixed enum; Android's are a continuous size grid that
/// <c>SwiftDotNet.Live.Android</c> buckets onto the nearest of these, so one <c>Body</c> switch reads
/// sensibly on both.
/// </summary>
public enum WidgetFamily
{
    /// <summary>Apple <c>systemSmall</c>; Android ≈ 2×2 cells.</summary>
    Small,
    /// <summary>Apple <c>systemMedium</c>; Android ≈ 4×2 cells.</summary>
    Medium,
    /// <summary>Apple <c>systemLarge</c>; Android ≈ 4×4 cells.</summary>
    Large,
    /// <summary>Apple <c>systemExtraLarge</c> (iPad only); Android ≈ 8×4 cells.</summary>
    ExtraLarge,
    /// <summary>Apple lock-screen circular accessory. Apple only — Android has no lock-screen widget host.</summary>
    AccessoryCircular,
    /// <summary>Apple lock-screen rectangular accessory. Apple only.</summary>
    AccessoryRectangular,
    /// <summary>Apple lock-screen inline accessory — a single line of text beside the clock. Apple only.</summary>
    AccessoryInline,
}

/// <summary>How a tree is turned into pixels on Android.</summary>
public enum LiveRenderMode
{
    /// <summary>
    /// Build a real <c>RemoteViews</c> tree. Default. Real views, so TalkBack reads them, the system
    /// themes them, and per-view taps work — but only what the <c>@RemotableViewMethod</c> whitelist can
    /// express, and runtime sizing needs API 31.
    /// </summary>
    Native,

    /// <summary>
    /// Render the whole tree to a bitmap with the Skia engine and ship it as one <c>ImageView</c>.
    /// Anything the Skia backend can draw becomes legal, and it works back to API 24 — at the cost of
    /// accessibility (one opaque image), per-view hit testing, and automatic theme/font-scale response.
    /// An explicit, documented trade rather than a silent fallback.
    /// </summary>
    Bitmap,
}
