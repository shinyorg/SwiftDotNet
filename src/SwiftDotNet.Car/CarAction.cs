using System.Diagnostics.CodeAnalysis;

namespace SwiftDotNet;

/// <summary>How a <see cref="CarAction"/> is emphasised.</summary>
public enum CarActionStyle
{
    Default,
    /// <summary>The recommended action. CarPlay: confirm style. Android Auto: <c>FLAG_PRIMARY</c>.</summary>
    Primary,
    /// <summary>A destructive action. CarPlay: destructive/cancel style. Android Auto: red background.</summary>
    Destructive,
    /// <summary>Cancels a modal. CarPlay: cancel style.</summary>
    Cancel,
}

/// <summary>A button: a header/navigation-bar button, a pane action, an alert action or a map button.</summary>
public sealed record CarAction
{
    public CarAction() { }

    [SetsRequiredMembers]
    public CarAction(string title, Action? onSelect = null, CarActionStyle style = CarActionStyle.Default)
    {
        Title = title;
        OnSelect = onSelect;
        Style = style;
    }

    public required string Title { get; init; }
    public Action? OnSelect { get; init; }
    public CarActionStyle Style { get; init; }

    /// <summary>Icon. Required for map buttons; optional elsewhere (an icon-only header button uses it instead of the title).</summary>
    public CarImage? Image { get; init; }

    public bool IsEnabled { get; init; } = true;
}
