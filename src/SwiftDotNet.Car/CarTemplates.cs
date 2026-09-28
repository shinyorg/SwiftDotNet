using SwiftDotNet.Graphics;
using Point = SwiftDotNet.Graphics.Point;
using Size = SwiftDotNet.Graphics.Size;
using Rect = SwiftDotNet.Graphics.Rect;


namespace SwiftDotNet;

/// <summary>
/// Base for every car template. Templates are immutable descriptions; a <see cref="CarScreen"/> rebuilds
/// its template whenever it is invalidated and the renderer diffs/updates the native template.
/// </summary>
public abstract record CarTemplate
{
    public string? Title { get; init; }

    /// <summary>
    /// Buttons in the navigation bar (CarPlay trailing bar buttons) / action strip (Android Auto).
    /// Not rendered for tab contents.
    /// </summary>
    public IReadOnlyList<CarAction> HeaderActions { get; init; } = [];
}

#region List

public enum CarAccessory
{
    None,
    /// <summary>Chevron indicating the row drills into another screen (Android: browsable row).</summary>
    Disclosure,
    Cloud
}

public abstract record CarListItemBase
{
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public CarImage? Image { get; init; }
    public bool IsEnabled { get; init; } = true;
    /// <summary>Optional stable id (used by renderers to diff and by search results).</summary>
    public string? Id { get; init; }
}

public sealed record CarListItem : CarListItemBase
{
    public CarAccessory Accessory { get; init; }
    public Action? OnSelect { get; init; }
}

/// <summary>
/// A row with an on/off state. Android Auto renders a native toggle; CarPlay (which has no switch)
/// renders a checkmark accessory and flips the value on tap.
/// </summary>
public sealed record CarToggleItem : CarListItemBase
{
    public bool IsOn { get; init; }
    public Action<bool>? OnToggled { get; init; }
}

public sealed record CarListSection
{
    public string? Header { get; init; }
    public IReadOnlyList<CarListItemBase> Items { get; init; } = [];
}

public sealed record CarListTemplate : CarTemplate
{
    public IReadOnlyList<CarListSection> Sections { get; init; } = [];
    public string? EmptyMessage { get; init; }
    public bool IsLoading { get; init; }

    public int ItemCount => Sections.Sum(s => s.Items.Count);

    /// <summary>Convenience for a single, header-less section.</summary>
    public static CarListTemplate Create(string? title, IEnumerable<CarListItemBase> items) =>
        new() { Title = title, Sections = [new CarListSection { Items = items.ToList() }] };
}

#endregion

#region Grid

public sealed record CarGridItem
{
    public required string Title { get; init; }
    /// <summary>Both CarPlay (CPGridButton) and Android Auto (GridItem) require an image.</summary>
    public required CarImage Image { get; init; }
    public Action? OnSelect { get; init; }
    public bool IsEnabled { get; init; } = true;
}

public sealed record CarGridTemplate : CarTemplate
{
    public IReadOnlyList<CarGridItem> Items { get; init; } = [];
}

#endregion

#region Tabs

public sealed record CarTab
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    /// <summary>Required by Android Auto; used as the CarPlay tab image.</summary>
    public required CarImage Image { get; init; }
    /// <summary>Portable tab content: <see cref="CarListTemplate"/>, <see cref="CarGridTemplate"/> or <see cref="CarInfoTemplate"/>.</summary>
    public required CarTemplate Content { get; init; }
}

public sealed record CarTabsTemplate : CarTemplate
{
    public IReadOnlyList<CarTab> Tabs { get; init; } = [];
    /// <summary>Initially selected tab. Defaults to the first tab.</summary>
    public string? SelectedTabId { get; init; }
    public Action<string>? OnTabSelected { get; init; }
}

#endregion

#region Info / pane

public enum CarInfoLayout
{
    Leading,
    TwoColumn
}

public sealed record CarInfoItem(string Title, string? Detail = null);

/// <summary>Detail pane. CarPlay: CPInformationTemplate. Android Auto: PaneTemplate.</summary>
public sealed record CarInfoTemplate : CarTemplate
{
    public IReadOnlyList<CarInfoItem> Items { get; init; } = [];
    public IReadOnlyList<CarAction> Actions { get; init; } = [];
    public CarInfoLayout Layout { get; init; } = CarInfoLayout.Leading;
    public bool IsLoading { get; init; }
}

#endregion

#region Modals

/// <summary>Marker for templates that can only be presented modally via <see cref="ICarNavigator.Present"/>.</summary>
public abstract record CarModalTemplate : CarTemplate
{
    public IReadOnlyList<CarAction> Actions { get; init; } = [];
}

/// <summary>Short alert. CarPlay: CPAlertTemplate. Android Auto: MessageTemplate.</summary>
public sealed record CarAlertTemplate : CarModalTemplate;

/// <summary>Titled message with actions. CarPlay: CPActionSheetTemplate. Android Auto: MessageTemplate.</summary>
public sealed record CarActionSheetTemplate : CarModalTemplate
{
    public string? Message { get; init; }
}

#endregion

#region Search

public delegate Task<IReadOnlyList<CarListItem>> CarSearchHandler(string text, CancellationToken cancellationToken);

/// <summary>
/// Keyboard/voice search. The renderer owns the query text; <see cref="Search"/> is invoked as the text changes
/// and the returned rows are shown (capped to the list limit). Selecting a row invokes its OnSelect.
/// </summary>
public sealed record CarSearchTemplate : CarTemplate
{
    public string? Hint { get; init; }
    public required CarSearchHandler Search { get; init; }
    public Action<string>? OnSubmitted { get; init; }
}

#endregion

#region Map

/// <summary>
/// App-drawn map surface. <see cref="Draw"/> paints through the same <see cref="ICanvas"/> seam the
/// self-drawing engine uses: into CarPlay's <c>CPWindow</c> via a CoreGraphics canvas, and into Android
/// Auto's <c>Surface</c> via an <c>android.graphics</c> canvas. CarPlay requires the navigation entitlement
/// (<c>com.apple.developer.carplay-maps</c>); Android Auto requires host API 7+ and the
/// <c>androidx.car.app.MAP_TEMPLATES</c> and <c>androidx.car.app.ACCESS_SURFACE</c> permissions.
/// </summary>
public sealed record CarMapTemplate : CarTemplate
{
    /// <summary>Paints the map. Called on the UI thread whenever the surface needs a frame.</summary>
    public required Action<CarMapDrawContext> Draw { get; init; }
    /// <summary>Icon-only buttons floating on the map (zoom, recenter…).</summary>
    public IReadOnlyList<CarAction> MapButtons { get; init; } = [];
    /// <summary>
    /// Optional overlay content (list, grid or info). Android Auto renders it over the map
    /// (MapWithContentTemplate); CarPlay adds a navigation bar button that pushes it.
    /// </summary>
    public CarTemplate? Content { get; init; }
    /// <summary>Pan by a delta in device-independent units (positive = content moves right/down).</summary>
    public Action<float, float>? OnPan { get; init; }
    /// <summary>Zoom by a multiplicative factor around a focus point.</summary>
    public Action<float, Point>? OnZoom { get; init; }
    /// <summary>Tap on the surface (Android Auto only - CarPlay does not deliver taps to the base view).</summary>
    public Action<Point>? OnTap { get; init; }
}

/// <summary>What a <see cref="CarMapTemplate.Draw"/> callback paints with: a canvas, fonts for text, and the surface size in device-independent units.</summary>
public sealed class CarMapDrawContext(ICanvas canvas, IFontProvider fonts, Size size)
{
    public ICanvas Canvas { get; } = canvas;
    public IFontProvider Fonts { get; } = fonts;
    public Size Size { get; } = size;
    public Rect Bounds => Rect.Create(0, 0, Size.Width, Size.Height);
}

public sealed record CarPointOfInterest
{
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public string? Summary { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    /// <summary>
    /// Distance from the vehicle. Android Auto requires a distance on place-list rows (rendered as a DistanceSpan);
    /// without it the row is made browsable instead. CarPlay ignores it (put it in <see cref="Subtitle"/> if needed).
    /// </summary>
    public double? DistanceKm { get; init; }
    public SwiftColor? PinColor { get; init; }
    public CarAction? PrimaryAction { get; init; }
    public CarAction? SecondaryAction { get; init; }
    /// <summary>
    /// Selection callback. CarPlay shows the detail card (with primary/secondary actions) itself; Android Auto
    /// invokes this when the row is tapped, or presents an action sheet with the primary/secondary actions when null.
    /// </summary>
    public Action? OnSelect { get; init; }
}

/// <summary>
/// Host-drawn map with pins. CarPlay: CPPointOfInterestTemplate. Android Auto: PlaceListMapTemplate.
/// </summary>
public sealed record CarPointOfInterestTemplate : CarTemplate
{
    public IReadOnlyList<CarPointOfInterest> Points { get; init; } = [];
    public int SelectedIndex { get; init; } = -1;
}

#endregion
