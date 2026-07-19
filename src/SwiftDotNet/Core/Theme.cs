namespace SwiftDotNet;

/// <summary>
/// A bag of design tokens (colors, fonts, spacing, corner radius) — the SwiftDotNet analog of a
/// design system read through SwiftUI's environment. Inject one at the root with
/// <c>root.Theme(new Theme { Accent = Color.Green })</c>; read it anywhere a view is being built via
/// <see cref="EnvironmentValues.Current"/> (<c>EnvironmentValues.Current.Theme</c>) — for example inside a
/// composite view's <c>Body</c> or a reusable <see cref="IViewStyle"/>.
///
/// Themes are immutable; derive a variant with a <c>with</c>-style initializer:
/// <c>Theme.Default with { Accent = Color.Red }</c>.
/// </summary>
public sealed record Theme
{
    /// <summary>The built-in light theme used when no theme is injected.</summary>
    public static readonly Theme Default = new();

    /// <summary>Primary brand/accent color — the default tint for interactive controls.</summary>
    public SwiftColor Accent { get; init; } = Color.Blue;

    /// <summary>The screen background color.</summary>
    public SwiftColor Background { get; init; } = Color.Hex("#FFFFFF");

    /// <summary>A slightly raised surface color (cards, grouped rows).</summary>
    public SwiftColor Surface { get; init; } = Color.Hex("#F2F2F7");

    /// <summary>The default foreground/content color drawn on <see cref="Background"/>.</summary>
    public SwiftColor OnBackground { get; init; } = Color.Primary;

    /// <summary>The foreground color drawn on top of <see cref="Accent"/> fills.</summary>
    public SwiftColor OnAccent { get; init; } = Color.Hex("#FFFFFF");

    /// <summary>The default body font.</summary>
    public SwiftFont BodyFont { get; init; } = Font.Body;

    /// <summary>The font for titles/headings.</summary>
    public SwiftFont TitleFont { get; init; } = Font.Title;

    /// <summary>The default corner radius for cards and filled controls, in points.</summary>
    public double CornerRadius { get; init; } = 12;

    /// <summary>The default spacing between stacked elements, in points.</summary>
    public double Spacing { get; init; } = 16;
}
