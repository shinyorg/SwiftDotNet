namespace SwiftDotNet;

/// <summary>
/// A modifier accumulator used to author reusable styles. It's a <see cref="View"/> only so the existing
/// fluent modifier extensions (<c>.Padding()</c>, <c>.Background()</c>, …) apply to it unchanged — a style is
/// just "the set of modifiers you'd otherwise chain onto a view." It never renders on its own.
/// </summary>
public sealed class ViewStyleBuilder : View
{
    internal ViewStyleBuilder() { }

    internal override Node BuildNode(RenderContext ctx, string path) =>
        throw new InvalidOperationException(
            "ViewStyleBuilder only accumulates modifiers for a style; it is never rendered.");
}

/// <summary>
/// A reusable bundle of modifiers — SwiftDotNet's analog of a SwiftUI <c>ViewModifier</c>/<c>View</c> extension.
/// Define a look once, then attach it to any view with <c>.Style(style)</c>. Configure it exactly as you'd
/// chain modifiers: <c>b =&gt; b.Padding().Background(Color.Secondary).CornerRadius(12)</c>. Unlike the ambient
/// environment, a <see cref="IViewStyle"/> is applied explicitly per view (it doesn't cascade).
/// </summary>
public interface IViewStyle
{
    /// <summary>Add this style's modifiers to <paramref name="builder"/> using the fluent modifier extensions.</summary>
    void Configure(ViewStyleBuilder builder);
}

/// <summary>
/// An ambient control style for <see cref="Button"/>, mirroring SwiftUI's <c>ButtonStyle</c>. Set once high in
/// the tree with <c>.ButtonStyle(new FilledButtonStyle())</c> and every button below adopts it, without
/// touching call sites. Like the rest of the cascade, the style contributes modifier <i>defaults</i>: a button
/// that sets a property itself (e.g. its own <c>.Background</c>) keeps its value.
/// </summary>
public interface IButtonStyle
{
    /// <summary>Add the style's modifiers to <paramref name="builder"/> using the fluent modifier extensions.</summary>
    void Configure(ViewStyleBuilder builder);
}

/// <summary>A solid, filled button — a tinted rounded rectangle with contrasting text (SwiftUI's
/// <c>.borderedProminent</c> shape). Defaults to the current <see cref="Theme"/>'s accent.</summary>
public sealed class FilledButtonStyle : IButtonStyle
{
    readonly SwiftColor? _background;
    readonly SwiftColor? _foreground;

    /// <param name="background">Fill color; defaults to the current theme's <see cref="Theme.Accent"/>.</param>
    /// <param name="foreground">Text color; defaults to the current theme's <see cref="Theme.OnAccent"/>.</param>
    public FilledButtonStyle(SwiftColor? background = null, SwiftColor? foreground = null)
    {
        _background = background;
        _foreground = foreground;
    }

    public void Configure(ViewStyleBuilder b)
    {
        var theme = EnvironmentValues.Current.Theme;
        b.Padding(Edge.Horizontal, 16)
         .Padding(Edge.Vertical, 10)
         .Background(_background ?? theme.Accent)
         .ForegroundColor(_foreground ?? theme.OnAccent)
         .CornerRadius(theme.CornerRadius);
    }
}

/// <summary>An outlined button — a stroked rounded rectangle in the tint color (SwiftUI's <c>.bordered</c>).</summary>
public sealed class BorderedButtonStyle : IButtonStyle
{
    readonly SwiftColor? _tint;

    /// <param name="tint">Border and text color; defaults to the current theme's <see cref="Theme.Accent"/>.</param>
    public BorderedButtonStyle(SwiftColor? tint = null) => _tint = tint;

    public void Configure(ViewStyleBuilder b)
    {
        var theme = EnvironmentValues.Current.Theme;
        var color = _tint ?? theme.Accent;
        b.Padding(Edge.Horizontal, 16)
         .Padding(Edge.Vertical, 10)
         .ForegroundColor(color)
         .Border(color, 1, cornerRadius: theme.CornerRadius);
    }
}

/// <summary>A card look — padding, a raised surface fill, rounded corners, and a soft shadow — read from the
/// current <see cref="Theme"/>. Usable as a bundle (<c>.Style(new CardStyle())</c>) or via the <c>.CardStyle()</c>
/// convenience.</summary>
public sealed class CardStyle : IViewStyle
{
    public void Configure(ViewStyleBuilder b)
    {
        var theme = EnvironmentValues.Current.Theme;
        b.Padding()
         .Background(theme.Surface)
         .CornerRadius(theme.CornerRadius)
         .Shadow(6, y: 2);   // default (per-backend) shadow color
    }
}

/// <summary>
/// Entry points for the three global-styling mechanisms:
/// <list type="bullet">
/// <item><b>Reusable bundles</b> — <c>.Style(style)</c> / <c>.CardStyle()</c> (applied explicitly).</item>
/// <item><b>Environment cascade</b> — <c>.Environment(e =&gt; …)</c> / <c>.Theme(…)</c> (inherited by descendants).</item>
/// <item><b>Control styles</b> — <c>.ButtonStyle(…)</c> (inherited by every button below).</item>
/// </list>
/// </summary>
public static class StyleExtensions
{
    /// <summary>Apply a reusable <see cref="IViewStyle"/> bundle to this view (explicit, per-view). The bundle
    /// resolves during the render pass, so it may read the ambient <see cref="Theme"/> via
    /// <c>EnvironmentValues.Current</c>; its modifiers apply after any inline modifiers on the same view.</summary>
    public static T Style<T>(this T view, IViewStyle style) where T : View
    {
        ArgumentNullException.ThrowIfNull(style);
        view.AddStyle(style.Configure);
        return view;
    }

    /// <summary>Apply an inline bundle of modifiers to this view: <c>.Style(b =&gt; b.Padding().Background(...))</c>.
    /// Resolves at render time (see the <see cref="IViewStyle"/> overload).</summary>
    public static T Style<T>(this T view, Action<ViewStyleBuilder> configure) where T : View
    {
        ArgumentNullException.ThrowIfNull(configure);
        view.AddStyle(configure);
        return view;
    }

    /// <summary>Convenience for <c>.Style(new CardStyle())</c> — a themed card look.</summary>
    public static T CardStyle<T>(this T view) where T : View => view.Style(new CardStyle());

    /// <summary>Set ambient <see cref="EnvironmentValues"/> for this view's subtree (the SwiftUI "global style"
    /// mechanism): <c>root.Environment(e =&gt; e.Font(Font.Body).ForegroundColor(Color.Primary))</c>.</summary>
    public static EnvironmentScope Environment<T>(this T view, Action<EnvironmentBuilder> configure) where T : View
    {
        ArgumentNullException.ThrowIfNull(configure);
        var b = new EnvironmentBuilder();
        configure(b);
        return new EnvironmentScope(view, b.Build());
    }

    /// <summary>Inject a design-token <see cref="SwiftDotNet.Theme"/> for this view's subtree, read via
    /// <c>EnvironmentValues.Current.Theme</c>.</summary>
    public static EnvironmentScope Theme<T>(this T view, Theme theme) where T : View
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new EnvironmentScope(view, new EnvironmentBuilder().Theme(theme).Build());
    }

    /// <summary>Set the ambient <see cref="IButtonStyle"/> for every <see cref="Button"/> in this view's subtree
    /// (mirrors SwiftUI's <c>.buttonStyle()</c>).</summary>
    public static EnvironmentScope ButtonStyle<T>(this T view, IButtonStyle style) where T : View
    {
        ArgumentNullException.ThrowIfNull(style);
        return new EnvironmentScope(view, new EnvironmentBuilder().ButtonStyle(style).Build());
    }
}
