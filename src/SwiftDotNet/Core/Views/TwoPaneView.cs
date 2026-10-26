namespace SwiftDotNet;

/// <summary>Which of a <see cref="TwoPaneView"/>'s panes shows when it collapses to one.</summary>
public enum Pane
{
    /// <summary>The first pane (typically the list).</summary>
    Primary,
    /// <summary>The second pane (typically the detail).</summary>
    Secondary,
}

/// <summary>What a <see cref="TwoPaneView"/> does in a narrow-but-tall window.</summary>
public enum TallMode
{
    /// <summary>Collapse to one pane (the default — a portrait phone shows one pane).</summary>
    SinglePane,
    /// <summary>Stack the panes: primary on top.</summary>
    TopBottom,
    /// <summary>Stack the panes: secondary on top.</summary>
    BottomTop,
}

/// <summary>The arrangement a <see cref="TwoPaneView"/> resolved to for the current posture and window.</summary>
public enum TwoPaneLayout
{
    /// <summary>One pane, chosen by <see cref="TwoPaneView.PanePriority"/>.</summary>
    SinglePane,
    /// <summary>Side by side, split by ratio (no fold, or no fold to honour).</summary>
    Wide,
    /// <summary>Stacked, split by ratio (<see cref="TallMode"/> opted in).</summary>
    Tall,
    /// <summary>Side by side, split exactly on a vertical fold — nothing sits under the hinge.</summary>
    Book,
    /// <summary>Stacked, split exactly on a horizontal fold (tabletop).</summary>
    Tabletop,
}

/// <summary>
/// Two panes that follow the device: split on the fold of a half-open foldable (book or tabletop), side by
/// side in a wide window, one pane in a narrow one. The closest SwiftUI analog is iOS 27.1's
/// <c>ArrangementView</c>; on Android, Material 3's <c>ListDetailPaneScaffold</c>; on Windows, WinUI's
/// <c>TwoPaneView</c>, whose knobs this mirrors.
/// <code>
/// new TwoPaneView(
///     primary:   new List(...),
///     secondary: new DetailView(selected))
///   .MinWideWidth(600)
///   .PanePriority(Pane.Secondary)
///   .TallModeConfiguration(TallMode.TopBottom)
/// </code>
///
/// <para>It reads <see cref="WindowPosture.Current"/>, so it re-lays itself out when the device folds,
/// unfolds or rotates. In order, it picks:</para>
/// <list type="number">
/// <item><b>Book fold</b> crossing the window → primary left of the fold, secondary right of it.</item>
/// <item><b>Tabletop fold</b> → primary above the fold, secondary below.</item>
/// <item><b>Width ≥ <see cref="MinWideWidth"/></b> (or no window size reported) → side by side at
/// <see cref="SplitRatio"/>.</item>
/// <item><b>Height ≥ <see cref="MinTallHeight"/></b> with a <see cref="TallMode"/> other than
/// <see cref="SwiftDotNet.TallMode.SinglePane"/> → stacked.</item>
/// <item>Otherwise → the <see cref="PanePriority"/> pane alone.</item>
/// </list>
///
/// <para><b>v1 assumes the view fills the root area</b> — fold coordinates are reported in root
/// coordinates and the view has no way yet to learn its own frame. Use it as (or directly under) the root,
/// or inside a <c>TabView</c>/<c>NavigationStack</c> page that fills the screen.</para>
///
/// <para>Where the bridge supports it (<see cref="IBridgeCapabilities"/>) a side-by-side split is drawn by
/// the platform's own container — SwiftUI <c>ArrangementView</c> (<c>.split</c>) on iOS 27.1+, Compose
/// <c>ListDetailPaneScaffold</c> on Android. Everything else, and every other layout, is a plain
/// <see cref="Grid"/> composite that works on all backends.</para>
/// </summary>
public sealed class TwoPaneView : View
{
    /// <summary>The node type the native containers register under.</summary>
    internal const string NativeType = "TwoPaneView";

    readonly View _primary;
    readonly View _secondary;
    double _minWideWidth = 600;
    double _minTallHeight = 600;
    Pane _priority = Pane.Primary;
    TallMode _tallMode = TallMode.SinglePane;
    double _ratio = 0.5;
    double _spacing;
    bool _useNative = true;

    public TwoPaneView(View primary, View secondary)
    {
        Throw.IfNull(primary);
        Throw.IfNull(secondary);
        _primary = primary;
        _secondary = secondary;
    }

    /// <summary>Narrowest window (pts/dp) that shows both panes side by side when no fold is in play. Default 600.</summary>
    public TwoPaneView MinWideWidth(double width) { _minWideWidth = Math.Max(0, width); return this; }

    /// <summary>Shortest window that stacks the panes when <see cref="TallModeConfiguration"/> allows it. Default 600.</summary>
    public TwoPaneView MinTallHeight(double height) { _minTallHeight = Math.Max(0, height); return this; }

    /// <summary>Which pane shows when collapsed to one. Default <see cref="Pane.Primary"/>.</summary>
    public TwoPaneView PanePriority(Pane pane) { _priority = pane; return this; }

    /// <summary>What to do in a narrow, tall window. Default <see cref="SwiftDotNet.TallMode.SinglePane"/>.</summary>
    public TwoPaneView TallModeConfiguration(TallMode mode) { _tallMode = mode; return this; }

    /// <summary>The primary pane's share of a ratio split (wide or tall), clamped to 0.1–0.9. Default 0.5.
    /// Ignored when splitting on a fold — the fold decides.</summary>
    public TwoPaneView SplitRatio(double ratio) { _ratio = Math.Clamp(ratio, 0.1, 0.9); return this; }

    /// <summary>Gap between the panes in a ratio split. Default 0. A fold split uses the fold's own width.</summary>
    public TwoPaneView PaneSpacing(double spacing) { _spacing = Math.Max(0, spacing); return this; }

    /// <summary>Allow the platform's native container where the bridge has one (default true). Set false to
    /// force the composite everywhere — handy for comparing the two.</summary>
    public TwoPaneView UseNativeContainer(bool useNative = true) { _useNative = useNative; return this; }

    /// <summary>The arrangement this view picks for <paramref name="posture"/>. Pure — exposed so apps (and
    /// tests) can mirror the decision, e.g. to hide a "back to list" button when both panes show.</summary>
    public TwoPaneLayout ResolveLayout(PostureInfo posture)
    {
        if (posture.IsBook && FoldInside(posture.FoldX, posture.FoldWidth, posture.WindowWidth))
            return TwoPaneLayout.Book;
        if (posture.IsTabletop && FoldInside(posture.FoldY, posture.FoldHeight, posture.WindowHeight))
            return TwoPaneLayout.Tabletop;

        // No size reported (a desktop or self-drawn backend without a posture host): assume a window big
        // enough for two panes — the common case there, and the one that shows the app's full content.
        if (!posture.HasWindowSize || posture.WindowWidth >= _minWideWidth)
            return TwoPaneLayout.Wide;

        if (_tallMode != TallMode.SinglePane && posture.WindowHeight >= _minTallHeight)
            return TwoPaneLayout.Tall;

        return TwoPaneLayout.SinglePane;
    }

    /// <summary>A fold only splits the view if it actually sits inside it (with something on both sides).</summary>
    static bool FoldInside(double start, double extent, double windowExtent)
        => start > 0 && (windowExtent <= 0 || start + extent < windowExtent);

    internal override Node BuildNode(RenderContext ctx, string path)
    {
        var posture = WindowPosture.Current;
        var layout = ResolveLayout(posture);

        // Native containers only take the side-by-side cases: ListDetailPaneScaffold has no stacked mode,
        // and keeping the collapse decision here means MinWideWidth/PanePriority behave the same everywhere.
        if (_useNative && layout is TwoPaneLayout.Wide or TwoPaneLayout.Book
            && SwiftApp.BridgeSupportsNativeView(NativeType))
        {
            var node = ctx.NewNode(NativeType, path);
            node.Props["ratio"] = layout == TwoPaneLayout.Book && posture.WindowWidth > 0
                ? Math.Clamp(posture.FoldX / posture.WindowWidth, 0.1, 0.9)
                : _ratio;
            node.Props["fold"] = layout == TwoPaneLayout.Book;
            if (layout == TwoPaneLayout.Wide && _spacing > 0) node.Props["spacing"] = _spacing;
            node.Children.Add(_primary.ToNode(ctx, path + ".0"));
            node.Children.Add(_secondary.ToNode(ctx, path + ".1"));
            return node;
        }

        return Compose(layout, posture).ToNode(ctx, path);
    }

    View Compose(TwoPaneLayout layout, PostureInfo p) => layout switch
    {
        TwoPaneLayout.Book => FoldSplit(p.FoldX, p.FoldWidth, horizontal: true),
        TwoPaneLayout.Tabletop => FoldSplit(p.FoldY, p.FoldHeight, horizontal: false),
        TwoPaneLayout.Wide => RatioSplit(horizontal: true, _primary, _secondary),
        TwoPaneLayout.Tall when _tallMode == TallMode.BottomTop => RatioSplit(horizontal: false, _secondary, _primary),
        TwoPaneLayout.Tall => RatioSplit(horizontal: false, _primary, _secondary),
        _ => _priority == Pane.Secondary ? _secondary : _primary,
    };

    /// <summary>
    /// A fold split is a Grid whose first track ends exactly at the fold and whose (optional) second track
    /// is the fold itself — empty — so neither pane draws under the hinge. A Grid rather than stacks with
    /// frames because star tracks fill the proposed size on every backend without knowing it in C#.
    /// </summary>
    Grid FoldSplit(double foldStart, double foldExtent, bool horizontal)
    {
        var tracks = foldExtent > 0
            ? new[] { GridTrack.Fixed(foldStart), GridTrack.Fixed(foldExtent), GridTrack.Star() }
            : new[] { GridTrack.Fixed(foldStart), GridTrack.Star() };
        var children = foldExtent > 0
            ? new View[] { _primary, new Spacer(), _secondary }
            : new View[] { _primary, _secondary };
        return Split(horizontal, tracks, children, spacing: 0);
    }

    Grid RatioSplit(bool horizontal, View first, View second)
    {
        var share = ReferenceEquals(first, _primary) ? _ratio : 1 - _ratio;
        return Split(horizontal, new[] { GridTrack.Star(share), GridTrack.Star(1 - share) },
            new[] { first, second }, _spacing);
    }

    static Grid Split(bool horizontal, GridTrack[] tracks, View[] children, double spacing)
    {
        // Top-aligned: a pane whose content is shorter than the window (a short list) reads as a pane that
        // starts at the top, not one floating in the middle of its half. Greedy content fills the cell anyway.
        if (horizontal)
            return new Grid(children).Columns(tracks).Rows(GridTrack.Star()).ColumnSpacing(spacing).RowSpacing(0)
                .Alignment(Alignment.Top);

        // Stacked: one column, so children flow one per row.
        return new Grid(children).Columns(GridTrack.Star()).Rows(tracks).RowSpacing(spacing).ColumnSpacing(0)
            .Alignment(Alignment.Top);
    }
}
