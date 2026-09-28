namespace SwiftDotNet;

/// <summary>Where a template is being placed; some templates are only valid in some positions.</summary>
public enum CarTemplatePlacement
{
    /// <summary>Returned from <see cref="CarScreen.Build"/> (root or pushed).</summary>
    Screen,
    /// <summary>Content of a <see cref="CarTab"/>.</summary>
    TabContent,
    /// <summary>Overlay content of a <see cref="CarMapTemplate"/>.</summary>
    MapContent,
    /// <summary>Presented via <see cref="ICarNavigator.Present"/>.</summary>
    Modal
}

/// <summary>
/// Applies <see cref="CarLimits"/> and portability rules. Runs once per build, before any renderer sees the
/// template, so both platforms receive the same (possibly truncated) model.
/// </summary>
public sealed class CarTemplateValidator(
    CarLimits limits,
    CarConstraintMode mode,
    Action<string>? log = null,
    CarPlatform? platform = null,
    CarAppCategory? category = null)
{
    public CarLimits Limits { get; } = limits;
    public CarConstraintMode Mode { get; } = mode;
    public CarPlatform? Platform { get; } = platform;
    public CarAppCategory? Category { get; } = category;

    /// <summary>
    /// Whether the configured category may use a template type on the connected platform.
    /// Always true when no category (or no platform) is known.
    /// </summary>
    public bool Supports(Type templateType) => UnsupportedReason(templateType) is null;

    string? UnsupportedReason(Type templateType)
    {
        if (Category is not { } cat || Platform is not { } plat)
            return null;

        var mediaOrMessaging = cat is CarAppCategory.Audio or CarAppCategory.Communication;
        if (templateType == typeof(CarPointOfInterestTemplate) && (cat == CarAppCategory.Navigation || mediaOrMessaging))
            return $"{cat} apps cannot use CarPointOfInterestTemplate ({(plat == CarPlatform.CarPlay ? "CPPointOfInterestTemplate" : "PlaceListMapTemplate")} is for point-of-interest categories).";

        if (templateType == typeof(CarSearchTemplate) && plat == CarPlatform.CarPlay && cat != CarAppCategory.Navigation)
            return $"CarPlay only allows CPSearchTemplate for navigation apps; {cat} apps should offer a browsable list instead.";

        if (templateType == typeof(CarMapTemplate))
        {
            if (plat == CarPlatform.CarPlay && cat != CarAppCategory.Navigation)
                return $"CarPlay only lets navigation apps draw maps (CPMapTemplate); {cat} apps should use CarPointOfInterestTemplate.";
            if (plat == CarPlatform.AndroidAuto && (mediaOrMessaging || cat == CarAppCategory.DrivingTask))
                return $"Android Auto {cat} apps cannot use MapWithContentTemplate.";
        }
        return null;
    }

    public CarTemplate Normalize(CarTemplate template, CarTemplatePlacement placement = CarTemplatePlacement.Screen)
    {
        CheckPlacement(template, placement);
        if (UnsupportedReason(template.GetType()) is { } reason)
            throw new CarTemplateException(reason);

        var headerActions = placement == CarTemplatePlacement.TabContent && template.HeaderActions.Count > 0
            ? Cap(template.HeaderActions, 0, "header actions in tab content")
            : Cap(template.HeaderActions, Limits.MaxHeaderActions, "header actions");

        CarTemplate result = template switch
        {
            CarListTemplate list => NormalizeList(list),
            CarGridTemplate grid => grid with { Items = Cap(grid.Items, Limits.MaxGridItems, "grid items") },
            CarTabsTemplate tabs => NormalizeTabs(tabs),
            CarInfoTemplate info => info with
            {
                Items = Cap(info.Items, Limits.MaxInfoItems, "info items"),
                Actions = Cap(info.Actions, Limits.MaxInfoActions, "info actions")
            },
            CarModalTemplate modal => NormalizeModal(modal),
            CarPointOfInterestTemplate poi => NormalizePoi(poi),
            CarMapTemplate map => NormalizeMap(map),
            CarSearchTemplate search => search,
            _ => throw new CarTemplateException($"Unsupported template type {template.GetType().Name}.")
        };

        return ReferenceEquals(headerActions, template.HeaderActions) ? result : result with { HeaderActions = headerActions };
    }

    /// <summary>Caps a search result list (search results are produced after <see cref="Normalize"/>).</summary>
    public IReadOnlyList<CarListItem> NormalizeSearchResults(IReadOnlyList<CarListItem> results) =>
        Cap(results, Limits.MaxListItems, "search results");

    static void CheckPlacement(CarTemplate template, CarTemplatePlacement placement)
    {
        switch (placement)
        {
            case CarTemplatePlacement.Modal when template is not CarModalTemplate:
                throw new CarTemplateException($"Only alert/action sheet templates can be presented, not {template.GetType().Name}.");
            case not CarTemplatePlacement.Modal when template is CarModalTemplate:
                throw new CarTemplateException($"{template.GetType().Name} must be presented with ICarNavigator.Present, not built by a screen.");
            case CarTemplatePlacement.TabContent when template is not (CarListTemplate or CarGridTemplate or CarInfoTemplate):
                throw new CarTemplateException($"{template.GetType().Name} cannot be tab content. Portable tab content is list, grid or info.");
            case CarTemplatePlacement.MapContent when template is not (CarListTemplate or CarGridTemplate or CarInfoTemplate):
                throw new CarTemplateException($"{template.GetType().Name} cannot be map overlay content. Use list, grid or info.");
        }
    }

    CarListTemplate NormalizeList(CarListTemplate list)
    {
        var sections = Cap(list.Sections, Limits.MaxListSections, "list sections");
        var total = sections.Sum(s => s.Items.Count);
        if (total > Limits.MaxListItems)
        {
            Violation($"list items ({total} > {Limits.MaxListItems})");
            var remaining = Limits.MaxListItems;
            var trimmed = new List<CarListSection>();
            foreach (var section in sections)
            {
                if (remaining <= 0)
                    break;
                var take = Math.Min(remaining, section.Items.Count);
                trimmed.Add(section with { Items = section.Items.Take(take).ToList() });
                remaining -= take;
            }
            sections = trimmed;
        }
        return ReferenceEquals(sections, list.Sections) ? list : list with { Sections = sections };
    }

    CarTabsTemplate NormalizeTabs(CarTabsTemplate tabs)
    {
        if (tabs.Tabs.Count < 2)
            throw new CarTemplateException("A tabs template needs at least 2 tabs (Android Auto TabTemplate requirement).");
        if (tabs.Tabs.Select(t => t.Id).Distinct().Count() != tabs.Tabs.Count)
            throw new CarTemplateException("Tab ids must be unique.");

        var capped = Cap(tabs.Tabs, Limits.MaxTabs, "tabs");
        var normalized = capped.Select(t => t with { Content = Normalize(t.Content, CarTemplatePlacement.TabContent) }).ToList();
        var selected = normalized.Any(t => t.Id == tabs.SelectedTabId) ? tabs.SelectedTabId : normalized[0].Id;
        return tabs with { Tabs = normalized, SelectedTabId = selected };
    }

    CarModalTemplate NormalizeModal(CarModalTemplate modal)
    {
        if (string.IsNullOrWhiteSpace(modal.Title))
            throw new CarTemplateException($"{modal.GetType().Name} requires a Title.");
        if (modal.Actions.Count == 0)
            throw new CarTemplateException($"{modal.GetType().Name} requires at least one action (CarPlay alerts can only be dismissed by an action).");
        return modal with { Actions = Cap(modal.Actions, Limits.MaxModalActions, "modal actions") };
    }

    CarPointOfInterestTemplate NormalizePoi(CarPointOfInterestTemplate poi)
    {
        var points = Cap(poi.Points, Limits.MaxPointsOfInterest, "points of interest");
        var selected = poi.SelectedIndex < points.Count ? poi.SelectedIndex : -1;
        return poi with { Points = points, SelectedIndex = selected };
    }

    CarMapTemplate NormalizeMap(CarMapTemplate map)
    {
        if (map.MapButtons.Any(b => b.Image is null))
            throw new CarTemplateException("Map buttons require an Image.");
        return map with
        {
            MapButtons = Cap(map.MapButtons, Limits.MaxMapButtons, "map buttons"),
            Content = map.Content is null ? null : Normalize(map.Content, CarTemplatePlacement.MapContent)
        };
    }

    IReadOnlyList<T> Cap<T>(IReadOnlyList<T> items, int max, string what)
    {
        if (items.Count <= max)
            return items;
        Violation($"{what} ({items.Count} > {max})");
        return items.Take(max).ToList();
    }

    void Violation(string what)
    {
        var message = $"SwiftDotNet.Car constraint exceeded: {what}.";
        if (Mode == CarConstraintMode.Throw)
            throw new CarConstraintException(message);
        log?.Invoke(message + " Truncated.");
    }
}
