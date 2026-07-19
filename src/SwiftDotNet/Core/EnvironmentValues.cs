namespace SwiftDotNet;

/// <summary>
/// The ambient style environment that cascades down the view tree, mirroring SwiftUI's
/// <c>EnvironmentValues</c>. Values set high in the tree (typically at the root) are inherited by
/// every descendant unless a view sets its own — the SwiftUI "global style" mechanism.
///
/// Unlike SwiftUI, the cascade is resolved <b>in C#</b> during the single render pass: each node that
/// doesn't carry an explicit <c>font</c>/<c>foregroundColor</c> modifier (or, for buttons, a control
/// style) inherits the ambient one, and the node ships to the backend fully resolved. So the cascade
/// works identically on every backend — SwiftUI, Compose, GTK, WinUI, Web, and Skia — with no
/// per-backend support, because it only ever emits modifier types the backends already understand.
///
/// Set values by wrapping a view: <c>root.Environment(e =&gt; e.Font(Font.Body).ForegroundColor(Color.Primary))</c>,
/// <c>root.Theme(myTheme)</c>, or <c>root.ButtonStyle(new FilledButtonStyle())</c>. Read the active
/// environment while a view is being built via <see cref="Current"/>.
/// </summary>
public sealed class EnvironmentValues
{
    /// <summary>The empty environment — no ambient values, the <see cref="Theme.Default"/> theme.</summary>
    public static readonly EnvironmentValues Empty = new(null, null, null, null);

    readonly SwiftFont? _font;
    readonly SwiftColor? _foregroundColor;
    readonly Theme? _theme;

    EnvironmentValues(SwiftFont? font, SwiftColor? foregroundColor, IButtonStyle? buttonStyle, Theme? theme)
    {
        _font = font;
        _foregroundColor = foregroundColor;
        ButtonStyle = buttonStyle;
        _theme = theme;
    }

    /// <summary>The ambient font inherited by text-bearing views (SwiftUI's <c>\.font</c>). Null = unset.</summary>
    public SwiftFont? Font => _font;

    /// <summary>The ambient foreground color (SwiftUI's <c>\.foregroundStyle</c>). Null = unset.</summary>
    public SwiftColor? ForegroundColor => _foregroundColor;

    /// <summary>The ambient control style applied to every <see cref="SwiftDotNet.Button"/> below. Null = unset.</summary>
    public IButtonStyle? ButtonStyle { get; }

    /// <summary>The ambient design-token bag; never null — falls back to <see cref="Theme.Default"/>.</summary>
    public Theme Theme => _theme ?? Theme.Default;

    /// <summary>Overlay <paramref name="overrides"/> onto this environment: each value the override sets wins,
    /// the rest is inherited. This is what makes nested scopes compose (an inner <c>.Font</c> keeps the outer
    /// <c>.ForegroundColor</c>).</summary>
    internal EnvironmentValues Overlay(EnvironmentValues overrides) => new(
        overrides._font ?? _font,
        overrides._foregroundColor ?? _foregroundColor,
        overrides.ButtonStyle ?? ButtonStyle,
        overrides._theme ?? _theme);

    internal static EnvironmentValues Create(SwiftFont? font, SwiftColor? fg, IButtonStyle? buttonStyle, Theme? theme)
        => new(font, fg, buttonStyle, theme);

    // ---- Ambient "current environment" for the active render pass ------------------------------
    // A render pass is synchronous and single-threaded, so a thread-static is a correct and cheap way
    // to expose the environment to code that has no RenderContext in hand — a composite view's Body or
    // an IViewStyle reading Theme tokens. EnvironmentScope pushes/pops it around building its subtree.

    [ThreadStatic] static EnvironmentValues? _current;

    /// <summary>The environment in effect for the view currently being built. Outside a render pass, or with
    /// no environment set, this is <see cref="Empty"/> (so reads never throw).</summary>
    public static EnvironmentValues Current => _current ?? Empty;

    internal static IDisposable Push(EnvironmentValues env)
    {
        var previous = _current;
        _current = env;
        return new Restore(previous);
    }

    sealed class Restore(EnvironmentValues? previous) : IDisposable
    {
        public void Dispose() => _current = previous;
    }

    // ---- Cascade resolution --------------------------------------------------------------------

    /// <summary>
    /// Fill in a freshly built node's inherited style: an ambient <c>font</c>/<c>foregroundColor</c> is added
    /// only if the node doesn't already carry one (an explicit local modifier always wins), and an ambient
    /// <see cref="SwiftDotNet.ButtonStyle"/> is applied to <c>Button</c> nodes. No-op for <see cref="Empty"/>.
    /// </summary>
    internal void InjectDefaults(RenderContext ctx, Node node)
    {
        if (ReferenceEquals(this, Empty)) return;

        if (_font is { } f && !HasModifier(node, "font"))
            node.Modifiers.Add(new Dictionary<string, object> { ["type"] = "font", ["value"] = f.Value });

        if (_foregroundColor is { } c && !HasModifier(node, "foregroundColor"))
            node.Modifiers.Add(new Dictionary<string, object> { ["type"] = "foregroundColor", ["value"] = c.Value });

        if (ButtonStyle is { } style && node.Type == "Button")
            ApplyControlStyle(style, ctx, node);
    }

    static bool HasModifier(Node node, string type)
    {
        foreach (var m in node.Modifiers)
            if (m.TryGetValue("type", out var t) && (t as string) == type)
                return true;
        return false;
    }

    /// <summary>Applies a control style's modifiers as defaults: any modifier whose <c>type</c> the node
    /// didn't already carry is added. Explicit local modifiers therefore suppress the style's version of the
    /// same property, matching the cascade rule for font/color.</summary>
    static void ApplyControlStyle(IButtonStyle style, RenderContext ctx, Node node)
    {
        var builder = new ViewStyleBuilder();
        style.Configure(builder);
        if (builder.Modifiers.Count == 0) return;

        // Freeze the node's original modifier types up front so a style that sets a property twice (e.g.
        // horizontal + vertical padding) contributes both, while an explicit local one suppresses all of them.
        var existing = new HashSet<string>();
        foreach (var m in node.Modifiers)
            if (m.TryGetValue("type", out var t) && t is string ts) existing.Add(ts);

        for (var i = 0; i < builder.Modifiers.Count; i++)
        {
            var dict = builder.Modifiers[i].Serialize(ctx, node.Id + "$style" + i);
            if (dict.TryGetValue("type", out var t) && t is string ts && !existing.Contains(ts))
                node.Modifiers.Add(dict);
        }
    }
}

/// <summary>
/// A transparent wrapper view that sets ambient <see cref="EnvironmentValues"/> for its subtree. It adds
/// no node of its own to the render tree — it builds its content at the same structural path — so it's
/// invisible to layout and to the diff engine. Created by the <c>.Environment(…)</c>, <c>.Theme(…)</c>,
/// and <c>.ButtonStyle(…)</c> extensions rather than constructed directly.
/// </summary>
public sealed class EnvironmentScope : View
{
    readonly View _content;
    readonly EnvironmentValues _overrides;

    internal EnvironmentScope(View content, EnvironmentValues overrides)
    {
        _content = content;
        _overrides = overrides;
    }

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        // Merge onto whatever environment is already in effect, so nested scopes compose, then build the
        // content under it. Passing the same path keeps this wrapper transparent in the tree/diff.
        var merged = EnvironmentValues.Current.Overlay(_overrides);
        using (EnvironmentValues.Push(merged))
            return _content.ToNode(ctx, path);
    }
}

/// <summary>Fluent builder for the <c>.Environment(e =&gt; …)</c> extension. Only the values you set become
/// overrides; everything else is inherited from the surrounding environment.</summary>
public sealed class EnvironmentBuilder
{
    SwiftFont? _font;
    SwiftColor? _foregroundColor;
    IButtonStyle? _buttonStyle;
    Theme? _theme;

    /// <summary>Set the ambient font inherited by descendant text (SwiftUI's <c>.font()</c> on a container).</summary>
    public EnvironmentBuilder Font(SwiftFont font) { _font = font; return this; }

    /// <summary>Set the ambient foreground color inherited by descendants (SwiftUI's <c>.foregroundStyle()</c>).</summary>
    public EnvironmentBuilder ForegroundColor(SwiftColor color) { _foregroundColor = color; return this; }

    /// <summary>Set the ambient control style applied to every descendant <see cref="SwiftDotNet.Button"/>.</summary>
    public EnvironmentBuilder ButtonStyle(IButtonStyle style) { _buttonStyle = style; return this; }

    /// <summary>Inject the design-token <see cref="SwiftDotNet.Theme"/> read via <c>EnvironmentValues.Current.Theme</c>.</summary>
    public EnvironmentBuilder Theme(Theme theme) { _theme = theme; return this; }

    internal EnvironmentValues Build() => EnvironmentValues.Create(_font, _foregroundColor, _buttonStyle, _theme);
}
