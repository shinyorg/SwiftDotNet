namespace SwiftDotNet;

/// <summary>Content limits enforced before a template reaches the platform.</summary>
public sealed record CarLimits
{
    public int MaxListItems { get; init; }
    public int MaxListSections { get; init; }
    public int MaxGridItems { get; init; }
    public int MaxTabs { get; init; }
    public int MaxInfoItems { get; init; }
    public int MaxInfoActions { get; init; }
    public int MaxModalActions { get; init; }
    public int MaxPointsOfInterest { get; init; }
    public int MaxHeaderActions { get; init; }
    public int MaxMapButtons { get; init; }
    /// <summary>Maximum screens on the navigation stack, including the root (modals excluded).</summary>
    public int MaxDepth { get; init; }

    /// <summary>
    /// Lowest common denominator of CarPlay and Android Auto (driving-mode) limits, so a template renders
    /// identically on both platforms.
    /// </summary>
    public static CarLimits Portable { get; } = new()
    {
        MaxListItems = 6,        // Android Auto ConstraintManager list limit while driving (minimum guaranteed)
        MaxListSections = 4,
        MaxGridItems = 6,        // Android Auto grid limit while driving; CarPlay allows 8
        MaxTabs = 4,             // Android Auto TabTemplate max; CarPlay 4-5 depending on category
        MaxInfoItems = 4,        // Android Auto Pane rows; CarPlay allows 10
        MaxInfoActions = 2,      // Android Auto Pane actions; CarPlay allows 3
        MaxModalActions = 2,     // Android Auto MessageTemplate; CarPlay alert typically 2
        MaxPointsOfInterest = 6, // Android Auto place list; CarPlay allows 12
        MaxHeaderActions = 2,    // CarPlay trailing bar buttons; Android Auto header end actions
        MaxMapButtons = 4,       // CarPlay CPMapTemplate / Android Auto map action strip
        MaxDepth = 5             // CarPlay template hierarchy depth / Android Auto task step limit
    };

    /// <summary>Takes the minimum of each limit.</summary>
    public CarLimits Min(CarLimits other) => new()
    {
        MaxListItems = Math.Min(MaxListItems, other.MaxListItems),
        MaxListSections = Math.Min(MaxListSections, other.MaxListSections),
        MaxGridItems = Math.Min(MaxGridItems, other.MaxGridItems),
        MaxTabs = Math.Min(MaxTabs, other.MaxTabs),
        MaxInfoItems = Math.Min(MaxInfoItems, other.MaxInfoItems),
        MaxInfoActions = Math.Min(MaxInfoActions, other.MaxInfoActions),
        MaxModalActions = Math.Min(MaxModalActions, other.MaxModalActions),
        MaxPointsOfInterest = Math.Min(MaxPointsOfInterest, other.MaxPointsOfInterest),
        MaxHeaderActions = Math.Min(MaxHeaderActions, other.MaxHeaderActions),
        MaxMapButtons = Math.Min(MaxMapButtons, other.MaxMapButtons),
        MaxDepth = Math.Min(MaxDepth, other.MaxDepth)
    };
}

public enum CarConstraintMode
{
    /// <summary>Drop content beyond the limits and report it through <see cref="CarOptions.Log"/>.</summary>
    Truncate,
    /// <summary>Throw <see cref="CarConstraintException"/> when a limit is exceeded.</summary>
    Throw
}

public enum CarLimitsSource
{
    /// <summary>Always use <see cref="CarLimits.Portable"/> - identical output on both platforms.</summary>
    Portable,
    /// <summary>Use the limits reported by the connected head unit (CPListTemplate.maximumItemCount, ConstraintManager…).</summary>
    Platform
}

/// <summary>
/// The app's car category. CarPlay grants one entitlement per category and each category only allows certain
/// templates; Android Auto declares it in the CarAppService intent filter. Setting it enables category checks.
/// </summary>
public enum CarAppCategory
{
    /// <summary>EV charging (CarPlay: com.apple.developer.carplay-charging, Android: androidx.car.app.category.CHARGING).</summary>
    Charging,
    Parking,
    Fueling,
    QuickFood,
    /// <summary>Generic points of interest (Android: androidx.car.app.category.POI).</summary>
    PointOfInterest,
    /// <summary>Turn-by-turn navigation (CarPlay: com.apple.developer.carplay-maps).</summary>
    Navigation,
    DrivingTask,
    Communication,
    Audio
}

public sealed class CarOptions
{
    /// <summary>When set, templates the category cannot use on the connected platform are rejected up front.</summary>
    public CarAppCategory? Category { get; set; }

    public CarConstraintMode ConstraintMode { get; set; } = CarConstraintMode.Truncate;
    public CarLimitsSource LimitsSource { get; set; } = CarLimitsSource.Portable;
    /// <summary>Overrides the limits entirely when set.</summary>
    public CarLimits? Limits { get; set; }
    /// <summary>Diagnostics sink (constraint truncation, renderer fallbacks).</summary>
    public Action<string>? Log { get; set; }
    /// <summary>
    /// Android Auto: accept any host (HostValidator.ALLOW_ALL_HOSTS_VALIDATOR). Defaults to only when the
    /// app is debuggable; otherwise the car-app library's sample allow-list is used.
    /// </summary>
    public bool? AndroidAllowAllHosts { get; set; }

    public CarLimits ResolveLimits(CarLimits? platformLimits)
    {
        if (Limits is not null)
            return Limits;
        return LimitsSource == CarLimitsSource.Platform && platformLimits is not null
            ? platformLimits
            : CarLimits.Portable;
    }
}

/// <summary>A template exceeded a <see cref="CarLimits"/> value in <see cref="CarConstraintMode.Throw"/> mode.</summary>
public sealed class CarConstraintException(string message) : Exception(message);

/// <summary>A template is structurally invalid on at least one platform (always thrown, regardless of mode).</summary>
public sealed class CarTemplateException(string message) : Exception(message);
