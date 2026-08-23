using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

// Core declares View types (Grid, Button, Slider, DatePicker, Image, Rectangle, Label, Menu, List, …) and
// the enums HorizontalAlignment / VerticalAlignment in this same namespace (SwiftDotNet). A simple name
// binds to the enclosing namespace's member before any using-imported one, so the WPF types are reached
// through these distinctly-named aliases. Note a plain `using Grid = System.Windows.Controls.Grid;` does
// NOT work — that alias itself conflicts with the namespace member (CS0576) — which is why every one of
// them is renamed rather than plain.
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfDatePicker = System.Windows.Controls.DatePicker;
using WpfGrid = System.Windows.Controls.Grid;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfImage = System.Windows.Controls.Image;
using WpfRectangle = System.Windows.Shapes.Rectangle;
using WpfSlider = System.Windows.Controls.Slider;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace SwiftDotNet;

/// <summary>A node in the retained WPF element tree — mirrors the wire node and holds its live control.</summary>
sealed class WpfNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<WpfNode> Children { get; } = new();

    /// <summary>The base control (used for value sync / children).</summary>
    public FrameworkElement Inner { get; private set; } = null!;

    /// <summary>The outermost element added to the parent (Inner, or a modifier Border wrapping it).</summary>
    public FrameworkElement Element { get; private set; } = null!;

    WpfBridge _bridge = null!;
    WpfNavController? _nav;

    /// <summary>
    /// How this node re-lays its children when a <c>setChildren</c> patch arrives, set by whichever
    /// container built the element.
    /// <para>It is a per-container callback rather than the WinUI port's "clear the panel and re-add"
    /// because that is only correct for a plain stack. <c>setChildren</c> fires on any structural change
    /// (see <see cref="TreeDiffer"/>), and a non-keyed container rebuilds every child from scratch — so a
    /// naive re-add would drop each new child's <c>Grid.Column</c>/<c>Row</c> attached properties (every
    /// cell collapsing into 0,0), a Canvas's <c>Left</c>/<c>Top</c>, a List's row chrome and separators,
    /// and a TabControl's headers. Containers with no meaningful re-lay leave it null.</para>
    /// </summary>
    Action? _relayout;

    /// <summary>The two-way-bound control when <see cref="Inner"/> is a wrapper around it (TextField's
    /// placeholder grid, the stepper's button row) — what <see cref="UpdateProps"/> syncs.</summary>
    TextBox? _textBox;
    TextBlock? _placeholder;
    TextBlock? _stepperValue;

    public static WpfNode Build(JsonElement e, WpfBridge bridge)
    {
        var node = new WpfNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        node._bridge = bridge;

        if (node.Type == "NavigationStack")
        {
            node._nav = new WpfNavController();
            bridge.NavStack.Push(node._nav);
        }

        foreach (var child in e.GetProperty("children").EnumerateArray())
            node.Children.Add(Build(child, bridge));

        if (node.Type == "NavigationStack") bridge.NavStack.Pop();

        node.Inner = node.CreateElement();
        node.Element = node.Inner;
        node.ApplyModifiers();
        return node;
    }

    // ---- element construction ------------------------------------------------

    FrameworkElement CreateElement() => Type switch
    {
        "Text" => Text(Str("text")),
        "Button" => MakeButton(),
        "Spacer" => MakeSpacer(),
        "Divider" => new Border { Height = 1, Background = WpfStyle.Brush("secondary"), HorizontalAlignment = WpfHorizontalAlignment.Stretch },
        "VStack" => Stack(Orientation.Vertical),
        "HStack" => Stack(Orientation.Horizontal),
        "ZStack" => MakeZStack(),
        "ScrollView" => MakeScroll(),
        "Grid" => MakeGrid(),
        "AbsoluteLayout" => MakeAbsoluteLayout(),
        "List" => MakeList(),
        "Form" => MakeForm(),
        "Section" => MakeSection(),
        "Group" => Stack(Orientation.Vertical),
        "DisclosureGroup" => MakeDisclosure(),
        "TabView" => MakeTabView(),
        "Tab" => Children.Count > 0 ? Children[0].Element : Text(""),
        "Menu" => MakeMenu(),
        "TextField" => MakeTextField(),
        "SecureField" => MakeSecureField(),
        "TextEditor" => MakeTextEditor(),
        "Toggle" => MakeToggle(),
        "Slider" => MakeSlider(),
        "Stepper" => MakeStepper(),
        "Picker" => MakePicker(),
        "DatePicker" => MakeDatePicker(),
        "ColorPicker" => MakeColorPicker(),
        "NavigationStack" => _nav!.Build(Children.Count > 0 ? Children[0].Element : Text("")),
        "NavigationLink" => MakeNavLink(),
        "Sheet" => Children.Count > 0 ? Children[0].Element : Text(""),
        "Alert" or "ActionSheet" => Children.Count > 0 ? Children[0].Element : Text(""),
        "WebView" => MakeWebView(),
        "Image" => MakeImage(),
        "Label" => MakeLabel(),
        "ProgressView" => MakeProgress(),
        "Gauge" => MakeGauge(),
        "Link" => MakeLink(),
        "Rectangle" => new WpfRectangle(),
        "Circle" => new Ellipse(),
        "Capsule" => new WpfRectangle { RadiusX = 999, RadiusY = 999 },
        "RoundedRectangle" => new WpfRectangle { RadiusX = Num("cornerRadius") ?? 8, RadiusY = Num("cornerRadius") ?? 8 },
        _ => CustomOrPlaceholder(),
    };

    IWpfRenderer? _customRenderer;

    FrameworkElement CustomOrPlaceholder()
    {
        _customRenderer = WpfRenderers.Get(Type);
        return _customRenderer is { } r ? r.Create(RenderCtx()) : Text($"⚠️ {Type}");
    }

    WpfRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    /// <summary>
    /// Degradation shared with WinUI: a <c>StackPanel</c> gives every child its desired size along the
    /// stack axis, so a stretched Spacer between two stacked views contributes nothing. It works as
    /// intended inside a ZStack or Grid cell. The Skia backend implements the real SwiftUI semantics.
    /// </summary>
    static Border MakeSpacer() => new()
    {
        HorizontalAlignment = WpfHorizontalAlignment.Stretch,
        VerticalAlignment = WpfVerticalAlignment.Stretch,
    };

    FrameworkElement MakeWebView()
    {
        var web = new Microsoft.Web.WebView2.Wpf.WebView2 { MinHeight = 300, HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        var url = Str("url");
        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u))
            web.Source = u;
        else if (Props.GetValueOrDefault("html") is string html)
            _ = LoadHtmlAsync(web, html);
        return web;
    }

    static async Task LoadHtmlAsync(Microsoft.Web.WebView2.Wpf.WebView2 web, string html)
    {
        await web.EnsureCoreWebView2Async();
        web.NavigateToString(html);
    }

    FrameworkElement MakeButton()
    {
        var b = new WpfButton { Content = Str("title"), HorizontalAlignment = WpfHorizontalAlignment.Center, Padding = new Thickness(12, 6, 12, 6) };
        b.Click += (_, _) => _bridge.Emit(Id, null);
        return b;
    }

    StackPanel Stack(Orientation orientation)
    {
        var spacing = Num("spacing") ?? 0;
        var panel = new StackPanel { Orientation = orientation };
        var align = Props.GetValueOrDefault("alignment") as string;
        if (orientation == Orientation.Vertical)
            panel.HorizontalAlignment = align is null ? WpfHorizontalAlignment.Center : AlignH(align);
        else
            panel.VerticalAlignment = align is null ? WpfVerticalAlignment.Center : AlignV(align);
        _relayout = () => Fill(panel, orientation, spacing);
        _relayout();
        return panel;
    }

    /// <summary>Clears a stack and re-adds every current child, re-applying the spacing margins.</summary>
    void Fill(Panel panel, Orientation orientation, double spacing)
    {
        panel.Children.Clear();
        foreach (var c in Children) panel.Children.Add(c.Element);
        ApplyStackSpacing(panel, orientation, spacing);
    }

    /// <summary>
    /// WPF's <see cref="StackPanel"/> has no <c>Spacing</c> property (WinUI's does), so the gap is
    /// realised as a leading margin on every child after the first. It is assigned absolutely rather than
    /// added to the existing margin, so re-laying children on a <c>setChildren</c> patch cannot compound
    /// it. Nothing else in this backend writes <c>Margin</c> — padding goes on a wrapper Border — so the
    /// property is free for this.
    /// </summary>
    static void ApplyStackSpacing(Panel panel, Orientation orientation, double spacing)
    {
        for (var i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is not FrameworkElement fe) continue;
            var gap = i == 0 ? 0 : spacing;
            fe.Margin = orientation == Orientation.Vertical
                ? new Thickness(0, gap, 0, 0)
                : new Thickness(gap, 0, 0, 0);
        }
    }

    WpfGrid MakeZStack()
    {
        var grid = new WpfGrid();
        _relayout = () =>
        {
            grid.Children.Clear();
            foreach (var c in Children) grid.Children.Add(c.Element);
            ApplyZStackAlignment(grid);   // re-laid children lose it
        };
        _relayout();
        return grid;
    }

    /// <summary>ZStack's <c>alignment</c> prop (an <see cref="Alignment"/> token) has no Grid-level
    /// equivalent in WPF — a Grid positions each child independently — so it is pushed onto every child.
    /// The prop is only serialized when the DSL sets it explicitly, but note it does override a child's
    /// own alignment.</summary>
    void ApplyZStackAlignment(WpfGrid grid)
    {
        if (Props.GetValueOrDefault("alignment") is not string token) return;
        var h = AlignH(token);
        var v = AlignV(token);
        foreach (var child in grid.Children)
            if (child is FrameworkElement fe)
            {
                fe.HorizontalAlignment = h;
                fe.VerticalAlignment = v;
            }
    }

    ScrollViewer MakeScroll()
    {
        var horizontal = Str("axis") == "horizontal";
        var orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
        var inner = new StackPanel
        {
            Orientation = orientation,
            HorizontalAlignment = WpfHorizontalAlignment.Center,
        };
        _relayout = () => Fill(inner, orientation, 12);
        _relayout();
        return new ScrollViewer
        {
            Content = inner,
            // WPF's defaults are the wrong way round for this (vertical Visible, horizontal Disabled),
            // and a permanently-visible scrollbar steals layout width even with nothing to scroll.
            VerticalScrollBarVisibility = horizontal ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = horizontal ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>
    /// WPF's Grid is the closest native match there is: Column/RowDefinitions take Pixel/Star/Auto
    /// straight across, and Grid.SetColumnSpan/SetRowSpan cover both span axes. Two DSL concepts have no
    /// direct equivalent: <see cref="GridTrackKind.Flexible"/>'s upper bound lands on the definition's
    /// MinWidth/MaxWidth, and WPF has no ColumnSpacing/RowSpacing (WinUI does), so the gutters become a
    /// leading margin on every cell that is not in the first column/row.
    /// </summary>
    WpfGrid MakeGrid()
    {
        var grid = new WpfGrid();
        _relayout = () => LayoutGrid(grid);
        _relayout();
        return grid;
    }

    /// <summary>
    /// (Re)builds the track definitions and places every child. Idempotent, so it doubles as the
    /// <see cref="_relayout"/> callback — the definitions are cleared first because a structural change
    /// can change the row count.
    /// </summary>
    void LayoutGrid(WpfGrid grid)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();

        var colTracks = GridEngine.ParseTracks(StrOrNull("columnTracks"), (int)(Num("columns") ?? 2));
        var columnSpacing = Num("columnSpacing") ?? Num("spacing") ?? 8;
        var rowSpacing = Num("rowSpacing") ?? Num("spacing") ?? 8;

        foreach (var t in colTracks)
        {
            var def = new ColumnDefinition { Width = Length(t) };
            if (t.Kind == GridTrackKind.Flexible)
            {
                def.MinWidth = t.Value;
                if (t.Max is { } max) def.MaxWidth = max;
            }
            grid.ColumnDefinitions.Add(def);
        }

        var requested = new (int?, int?, int, int)[Children.Count];
        for (var i = 0; i < Children.Count; i++) requested[i] = Children[i].GridCellSpec();
        var spans = GridEngine.Place(colTracks.Length, requested, out var rowCount);

        var rowTracks = StrOrNull("rowTracks") is { } rowSpec ? GridEngine.ParseTracks(rowSpec, rowCount) : null;
        for (var r = 0; r < rowCount; r++)
        {
            var t = rowTracks is not null && r < rowTracks.Length ? rowTracks[r] : GridTrack.Auto;
            var def = new RowDefinition { Height = Length(t) };
            if (t.Kind == GridTrackKind.Flexible)
            {
                def.MinHeight = t.Value;
                if (t.Max is { } max) def.MaxHeight = max;
            }
            grid.RowDefinitions.Add(def);
        }

        var token = Props.GetValueOrDefault("alignment") as string;
        for (var i = 0; i < Children.Count; i++)
        {
            var el = Children[i].Element;
            var s = spans[i];
            WpfGrid.SetColumn(el, s.Column);
            WpfGrid.SetRow(el, s.Row);
            if (s.ColumnSpan > 1) WpfGrid.SetColumnSpan(el, s.ColumnSpan);
            if (s.RowSpan > 1) WpfGrid.SetRowSpan(el, s.RowSpan);
            // Gutters, not outer padding: only interior edges get a margin.
            el.Margin = new Thickness(s.Column > 0 ? columnSpacing : 0, s.Row > 0 ? rowSpacing : 0, 0, 0);
            if (token is not null)
            {
                el.HorizontalAlignment = AlignH(token);
                el.VerticalAlignment = AlignV(token);
            }
            grid.Children.Add(el);
        }
    }

    static GridLength Length(GridTrack t) => t.Kind switch
    {
        GridTrackKind.Fixed => new GridLength(t.Value, GridUnitType.Pixel),
        GridTrackKind.Star => new GridLength(t.Value, GridUnitType.Star),
        _ => GridLength.Auto,   // Flexible is Auto plus the Min/Max bounds set on the definition
    };

    /// <summary>
    /// A WPF <see cref="Canvas"/> positions children at explicit Left/Top, which covers point bounds.
    /// Canvas never resizes its children, so declared sizes are pushed onto the elements, and
    /// proportional bounds are recomputed on <c>SizeChanged</c> — the Canvas itself has no layout-time
    /// size to resolve fractions against until it has been measured once.
    /// </summary>
    Canvas MakeAbsoluteLayout()
    {
        var canvas = new Canvas { HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        _relayout = () =>
        {
            canvas.Children.Clear();
            foreach (var c in Children) canvas.Children.Add(c.Element);
            // Re-added children carry no Canvas.Left/Top, so the bounds must be resolved again.
            SyncAbsoluteBounds(canvas, canvas.ActualWidth, canvas.ActualHeight);
        };
        _relayout();
        canvas.SizeChanged += (_, e) => SyncAbsoluteBounds(canvas, e.NewSize.Width, e.NewSize.Height);
        return canvas;
    }

    void SyncAbsoluteBounds(Canvas canvas, double hostWidth, double hostHeight)
    {
        foreach (var c in Children)
        {
            var m = c.Modifiers.FirstOrDefault(x => x["type"] as string == "layoutBounds");
            if (m is null) continue;
            var flags = AbsoluteLayoutBounds.Parse(m.GetValueOrDefault("flags") as string);
            var el = c.Element;
            var (x, y, w, h) = AbsoluteLayoutBounds.Resolve(
                N(m, "x"), N(m, "y"), Num(m, "width"), Num(m, "height"), flags,
                hostWidth, hostHeight, el.ActualWidth, el.ActualHeight);

            if (Num(m, "width").HasValue) el.Width = w;
            if (Num(m, "height").HasValue) el.Height = h;
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
        }
    }

    /// <summary>This node's <c>gridCell</c> placement request (nulls mean "flow me").</summary>
    (int? Column, int? Row, int ColumnSpan, int RowSpan) GridCellSpec()
    {
        var m = Modifiers.FirstOrDefault(x => x["type"] as string == "gridCell");
        if (m is null) return (null, null, 1, 1);
        return (
            Num(m, "column") is { } c ? (int)c : null,
            Num(m, "row") is { } r ? (int)r : null,
            Num(m, "columnSpan") is { } cs ? Math.Max(1, (int)cs) : 1,
            Num(m, "rowSpan") is { } rs ? Math.Max(1, (int)rs) : 1);
    }


    Border MakeList()
    {
        // Grid / horizontal lists reuse the standard grid / horizontal-stack layout. All three shapes
        // install a _relayout, so all three survive a setChildren patch — the grid re-places its cells
        // (a plain re-add would collapse them into 0,0) and the vertical list rebuilds its row chrome.
        if (Str("layout") == "grid")
            // MakeGrid installs the grid's own _relayout, so a structural change re-places the cells.
            return new Border { CornerRadius = new CornerRadius(8), Child = MakeGrid() };
        if (Str("axis") == "horizontal")
        {
            var h = new StackPanel { Orientation = Orientation.Horizontal };
            _relayout = () => Fill(h, Orientation.Horizontal, 8);
            _relayout();
            return new Border
            {
                CornerRadius = new CornerRadius(8),
                Child = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = h,
                },
            };
        }

        var panel = new StackPanel();
        _relayout = () => LayoutListRows(panel);
        _relayout();
        return new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = WpfStyle.Brush("secondary"), Child = panel };
    }

    void LayoutListRows(Panel panel)
    {
        var selectable = Str("selectionMode").Length > 0;
        panel.Children.Clear();
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var row = new Border { Padding = new Thickness(16, 12, 16, 12), Child = child.Element };
            // Selection: a tap emits the row's key to C#; selected rows get a highlight. Rows are rebuilt
            // on every layout pass, so handlers attach cleanly without stacking.
            if (selectable && child.Props.GetValueOrDefault("key") is string key)
            {
                if (child.Props.GetValueOrDefault("selected") as bool? == true)
                    row.Background = new SolidColorBrush(Colors.DodgerBlue) { Opacity = 0.2 };
                else
                    // A Border with a null Background is invisible to hit-testing in WPF, so an unselected
                    // row would simply not be clickable.
                    row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (_, _) => _bridge.Emit(Id, key);
            }
            panel.Children.Add(row);
            if (i < Children.Count - 1)
                panel.Children.Add(new Border { Height = 1, Background = WpfStyle.Brush("secondary") });
        }
    }

    ScrollViewer MakeForm()
    {
        var panel = new StackPanel();
        _relayout = () => Fill(panel, Orientation.Vertical, 16);
        _relayout();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    StackPanel MakeSection()
    {
        var panel = new StackPanel();
        _relayout = () =>
        {
            panel.Children.Clear();
            // The header is part of the section's own chrome, so it is re-added ahead of the children
            // rather than surviving as a stray first element.
            if (Props.GetValueOrDefault("header") is string header)
                panel.Children.Add(new TextBlock { Text = header, FontWeight = FontWeights.SemiBold });
            foreach (var c in Children) panel.Children.Add(c.Element);
            ApplyStackSpacing(panel, Orientation.Vertical, 6);
        };
        _relayout();
        return panel;
    }

    Expander MakeDisclosure()
    {
        var content = new StackPanel();
        _relayout = () => Fill(content, Orientation.Vertical, 4);
        _relayout();
        var expander = new Expander { Header = Str("label"), Content = content, IsExpanded = Bool("expanded"), HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        expander.Expanded += (_, _) => _bridge.Emit(Id, "true");
        expander.Collapsed += (_, _) => _bridge.Emit(Id, "false");
        return expander;
    }

    TabControl MakeTabView()
    {
        var tabs = new TabControl();
        _relayout = () =>
        {
            // A TabItem owns its page as Content, so the pages must be unhooked before new items adopt them.
            foreach (var item in tabs.Items.OfType<TabItem>()) item.Content = null;
            tabs.Items.Clear();
            foreach (var tab in Children)
                tabs.Items.Add(new TabItem { Header = tab.Str("title"), Content = tab.Element });
        };
        _relayout();
        if (Props.ContainsKey("selectedIndex"))
        {
            tabs.SelectedIndex = (int)(Num("selectedIndex") ?? 0);
            tabs.SelectionChanged += (_, e) =>
            {
                // SelectionChanged is a bubbling routed event, so a ComboBox or ListBox *inside* a tab
                // raises it here too. Without this guard, changing a Picker would be reported as a tab
                // change and knock the app back to whatever tab index the Picker happened to select.
                if (!ReferenceEquals(e.OriginalSource, tabs)) return;
                _bridge.Emit(Id, tabs.SelectedIndex.ToString(CultureInfo.InvariantCulture));
            };
        }
        return tabs;
    }

    void SyncTabView()
    {
        if (Inner is TabControl tc && Props.ContainsKey("selectedIndex"))
        {
            var idx = (int)(Num("selectedIndex") ?? 0);
            if (tc.SelectedIndex != idx) tc.SelectedIndex = idx;
        }
    }

    /// <summary>WPF has no <c>MenuFlyout</c>; a <see cref="ContextMenu"/> opened from the button's own
    /// click is the equivalent idiom (and is what a WPF split/drop-down button is built from).</summary>
    WpfButton MakeMenu()
    {
        var menu = new ContextMenu();
        _relayout = () =>
        {
            menu.Items.Clear();
            foreach (var c in Children)
            {
                var item = new MenuItem { Header = c.Str("title") };
                var childId = c.Id;
                item.Click += (_, _) => _bridge.Emit(childId, null);
                menu.Items.Add(item);
            }
        };
        _relayout();
        var button = new WpfButton { Content = Str("label"), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) =>
        {
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    // ---- controls (two-way bound) -------------------------------------------

    /// <summary>
    /// WPF's <see cref="TextBox"/> has no placeholder/watermark of its own, so the field is a two-layer
    /// grid: the box, plus a hit-test-invisible <see cref="TextBlock"/> shown only while the text is
    /// empty. <see cref="Inner"/> is therefore the grid, and <c>_textBox</c> is what UpdateProps syncs.
    /// </summary>
    FrameworkElement MakeTextField()
    {
        var tb = new TextBox { Text = Str("text"), HorizontalAlignment = WpfHorizontalAlignment.Stretch, Padding = new Thickness(4, 3, 4, 3) };
        if (KeyboardScope() is { } scope) InputMethod.SetInputScope(tb, scope);
        if (Num("maxLength") is { } max) tb.MaxLength = (int)max;
        tb.TextChanged += (_, _) =>
        {
            SyncPlaceholder();
            _bridge.Emit(Id, tb.Text);
        };
        _textBox = tb;
        return WithPlaceholder(tb, Str("placeholder"));
    }

    FrameworkElement WithPlaceholder(TextBox box, string placeholder)
    {
        if (placeholder.Length == 0) return box;
        var grid = new WpfGrid();
        grid.Children.Add(box);
        _placeholder = new TextBlock
        {
            Text = placeholder,
            Foreground = WpfStyle.Brush("secondary"),
            Margin = new Thickness(6, 0, 6, 0),
            VerticalAlignment = WpfVerticalAlignment.Center,
            IsHitTestVisible = false,
        };
        grid.Children.Add(_placeholder);
        SyncPlaceholder();
        return grid;
    }

    void SyncPlaceholder()
    {
        if (_placeholder is null || _textBox is null) return;
        _placeholder.Visibility = _textBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Degradation: WPF's <see cref="PasswordBox"/> has no placeholder property and, unlike
    /// <see cref="TextBox"/>, exposes its value as a plain CLR property rather than a DependencyProperty,
    /// so the watermark trick above cannot be layered over it the same way. The placeholder is dropped
    /// rather than faked.
    /// </summary>
    PasswordBox MakeSecureField()
    {
        var pb = new PasswordBox { Password = Str("text"), HorizontalAlignment = WpfHorizontalAlignment.Stretch, Padding = new Thickness(4, 3, 4, 3) };
        if (Num("maxLength") is { } max) pb.MaxLength = (int)max;
        pb.PasswordChanged += (_, _) => _bridge.Emit(Id, pb.Password);
        return pb;
    }

    /// <summary>
    /// F9 <c>keyboard</c> prop → a WPF <see cref="InputScope"/>. Returns null for the default (unset)
    /// keyboard so the field keeps the platform default.
    /// <para>Degradation: on desktop WPF an InputScope only steers the tablet/touch input panel and
    /// handwriting recognition — it does not restrict what a physical keyboard can type, and Core's
    /// binding is what actually keeps a numeric field numeric.</para>
    /// <para>Degradation: the F9 <c>returnKey</c> prop (done/go/next/search/send) has NO WPF equivalent —
    /// the touch keyboard's Enter key label is not settable — so the value is deliberately ignored rather
    /// than faked.</para>
    /// <para>Degradation: <c>maxLength</c> is applied at build time only; Core also clamps in-binding, so
    /// a field whose max changes later stays correct in state even though the control's own cap does not
    /// move (modifiers/props of this kind are not re-applied by <see cref="UpdateProps"/>).</para>
    /// </summary>
    InputScope? KeyboardScope()
    {
        var value = Str("keyboard") switch
        {
            // "number" is SwiftUI's numberPad → digits only; "decimal" allows the decimal separator.
            "number" => InputScopeNameValue.Digits,
            "decimal" => InputScopeNameValue.Number,
            "email" => InputScopeNameValue.EmailSmtpAddress,
            "phone" => InputScopeNameValue.TelephoneNumber,
            "url" => InputScopeNameValue.Url,
            _ => (InputScopeNameValue?)null,
        };
        if (value is not { } v) return null;
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName { NameValue = v });
        return scope;
    }

    TextBox MakeTextEditor()
    {
        var tb = new TextBox
        {
            Text = Str("text"),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 100,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
        };
        tb.TextChanged += (_, _) => _bridge.Emit(Id, tb.Text);
        _textBox = tb;
        return tb;
    }

    /// <summary>
    /// WPF has no <c>ToggleSwitch</c> (WinUI's mapping), so Toggle lands on a <see cref="CheckBox"/> —
    /// the platform's own on/off control. Behaviour is identical; only the chrome differs.
    /// </summary>
    CheckBox MakeToggle()
    {
        var cb = new CheckBox { Content = Str("label"), IsChecked = Bool("value"), VerticalContentAlignment = WpfVerticalAlignment.Center };
        // Checked/Unchecked rather than Click, so a programmatic change is reported the same way a user
        // one is; the UpdateProps guard below is what keeps that from looping.
        cb.Checked += (_, _) => _bridge.Emit(Id, "true");
        cb.Unchecked += (_, _) => _bridge.Emit(Id, "false");
        return cb;
    }

    WpfSlider MakeSlider()
    {
        var slider = new WpfSlider
        {
            Minimum = Num("min") ?? 0,
            Maximum = Num("max") ?? 1,
            SmallChange = 0.01,
            LargeChange = 0.1,
            Value = Num("value") ?? 0,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
        };
        slider.ValueChanged += (_, e) => _bridge.Emit(Id, e.NewValue.ToString(CultureInfo.InvariantCulture));
        return slider;
    }

    /// <summary>
    /// WPF has no <c>NumberBox</c> (WinUI's mapping), so the stepper is built from the parts: a read-only
    /// value plus −/+ repeat buttons, which is the same interaction without the spin-box chrome.
    /// </summary>
    StackPanel MakeStepper()
    {
        var min = Num("min") ?? -1e9;
        var max = Num("max") ?? 1e9;
        var value = Num("value") ?? 0;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WpfVerticalAlignment.Center });

        _stepperValue = new TextBlock
        {
            Text = ((int)value).ToString(CultureInfo.InvariantCulture),
            VerticalAlignment = WpfVerticalAlignment.Center,
            MinWidth = 32,
            TextAlignment = TextAlignment.Center,
        };

        // RepeatButton, not Button: holding it steps continuously, which is what a spin box does.
        var minus = new RepeatButton { Content = "−", Width = 28, Padding = new Thickness(0) };
        var plus = new RepeatButton { Content = "+", Width = 28, Padding = new Thickness(0) };
        void Step(double delta)
        {
            // Read the current value back out of the label so a held button accumulates, rather than
            // repeatedly re-emitting the value this element was built with.
            var current = double.TryParse(_stepperValue!.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : value;
            var next = Math.Clamp(current + delta, min, max);
            if (Math.Abs(next - current) < 0.0001) return;
            _stepperValue.Text = ((int)next).ToString(CultureInfo.InvariantCulture);
            _bridge.Emit(Id, ((int)next).ToString(CultureInfo.InvariantCulture));
        }
        minus.Click += (_, _) => Step(-1);
        plus.Click += (_, _) => Step(1);

        panel.Children.Add(minus);
        panel.Children.Add(_stepperValue);
        panel.Children.Add(plus);
        ApplyStackSpacing(panel, Orientation.Horizontal, 8);
        return panel;
    }

    StackPanel MakePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WpfVerticalAlignment.Center });
        var combo = new ComboBox { ItemsSource = Children.Select(c => c.Str("text")).ToList(), SelectedIndex = (int)(Num("selection") ?? 0) };
        combo.SelectionChanged += (_, _) => _bridge.Emit(Id, combo.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        // The options are child nodes, so adding or removing one arrives as a setChildren patch.
        _relayout = () => combo.ItemsSource = Children.Select(c => c.Str("text")).ToList();
        panel.Children.Add(combo);
        ApplyStackSpacing(panel, Orientation.Horizontal, 8);
        return panel;
    }

    StackPanel MakeDatePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WpfVerticalAlignment.Center });
        var picker = new WpfDatePicker
        {
            // The wire value is Unix seconds; WPF's DatePicker is DateTime-based, so it round-trips
            // through UTC to keep the value stable across time zones.
            SelectedDate = DateTimeOffset.FromUnixTimeSeconds((long)(Num("value") ?? 0)).UtcDateTime,
        };
        picker.SelectedDateChanged += (_, _) =>
        {
            if (picker.SelectedDate is { } d)
                _bridge.Emit(Id, new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Utc))
                    .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        };
        panel.Children.Add(picker);
        ApplyStackSpacing(panel, Orientation.Horizontal, 8);
        return panel;
    }

    /// <summary>
    /// WPF ships no colour picker at all (WinUI has one in the box), so this is a swatch button opening a
    /// <see cref="Popup"/> of the palette the wire vocabulary can express. That is deliberately the whole
    /// range: <c>WpfStyle.Color</c> resolves named tokens and <c>#rrggbb</c>, and the DSL emits one of
    /// those back — so a full HSV wheel would offer colours the token grammar cannot round-trip.
    /// </summary>
    StackPanel MakeColorPicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WpfVerticalAlignment.Center });

        var swatch = new Border { Width = 32, Height = 24, CornerRadius = new CornerRadius(4), Background = WpfStyle.Brush(Str("value")) ?? Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = WpfStyle.Brush("secondary") };
        var button = new WpfButton { Content = swatch, Padding = new Thickness(2) };

        var palette = new WpfGrid { Margin = new Thickness(8) };
        for (var c = 0; c < 4; c++) palette.ColumnDefinitions.Add(new ColumnDefinition());
        var colors = new[] { "red", "green", "blue", "accentColor", "#FF9500", "#FFCC00", "#5856D6", "#8E8E93" };
        for (var r = 0; r < 2; r++) palette.RowDefinitions.Add(new RowDefinition());

        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        for (var i = 0; i < colors.Length; i++)
        {
            var token = colors[i];
            var cell = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(4),
                Background = WpfStyle.Brush(token) ?? Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            cell.MouseLeftButtonUp += (_, _) =>
            {
                // Emit the canonical #rrggbb so the value round-trips through Color() regardless of which
                // spelling the palette used.
                if (WpfStyle.Color(token) is { } picked)
                {
                    swatch.Background = new SolidColorBrush(picked);
                    _bridge.Emit(Id, $"#{picked.R:X2}{picked.G:X2}{picked.B:X2}");
                }
                popup.IsOpen = false;
            };
            WpfGrid.SetColumn(cell, i % 4);
            WpfGrid.SetRow(cell, i / 4);
            palette.Children.Add(cell);
        }
        popup.Child = new Border { Background = SystemColors.WindowBrush, BorderBrush = WpfStyle.Brush("secondary"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = palette };
        button.Click += (_, _) => popup.IsOpen = true;

        panel.Children.Add(button);
        ApplyStackSpacing(panel, Orientation.Horizontal, 8);
        return panel;
    }

    // ---- navigation ----------------------------------------------------------

    WpfButton MakeNavLink()
    {
        var nav = _bridge.NavStack.Count > 0 ? _bridge.NavStack.Peek() : null;
        var row = new WpfGrid { HorizontalAlignment = WpfHorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (Children.Count > 0)
        {
            var content = Children[0].Element;
            WpfGrid.SetColumn(content, 0);
            row.Children.Add(content);
        }
        var chevron = new TextBlock { Text = "›", VerticalAlignment = WpfVerticalAlignment.Center };
        WpfGrid.SetColumn(chevron, 1);
        row.Children.Add(chevron);

        var button = new WpfButton { Content = row, HorizontalAlignment = WpfHorizontalAlignment.Stretch, HorizontalContentAlignment = WpfHorizontalAlignment.Stretch, Padding = new Thickness(8, 6, 8, 6) };
        if (Children.Count > 1)
        {
            var dest = Children[1].Element;
            var title = Children[1].TitleOf();
            button.Click += (_, _) => nav?.Push(dest, title);
        }
        return button;
    }

    string TitleOf() => Modifiers.FirstOrDefault(m => m["type"] as string == "navigationTitle")?["value"] as string ?? "";

    // ---- display -------------------------------------------------------------

    StackPanel MakeLabel()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(Text(WpfStyle.Emoji(Str("systemImage"))));
        panel.Children.Add(Text(Str("title")));
        ApplyStackSpacing(panel, Orientation.Horizontal, 6);
        return panel;
    }

    /// <summary>WPF has no HyperlinkButton; a <see cref="Hyperlink"/> inline in a TextBlock is the
    /// platform idiom, and its navigation has to be launched explicitly.</summary>
    FrameworkElement MakeLink()
    {
        var url = Str("url");
        var link = new Hyperlink(new Run(Str("title")));
        if (Uri.TryCreate(url, UriKind.Absolute, out var u))
        {
            link.NavigateUri = u;
            link.RequestNavigate += (_, e) =>
            {
                // WPF does not open the browser for you (WinUI's HyperlinkButton does); UseShellExecute
                // is what hands the URI to the default handler.
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                }
                catch { /* no handler registered for the scheme — nothing useful to do */ }
                e.Handled = true;
            };
        }
        return new TextBlock(link) { TextWrapping = TextWrapping.Wrap };
    }

    // F3 raster: a real bitmap from url / file / bytes; an SF-Symbol name falls back to the emoji glyph.
    FrameworkElement MakeImage()
    {
        try
        {
            var stretch = Str("contentMode") == "fill" ? Stretch.UniformToFill : Stretch.Uniform;
            BitmapImage? source = null;
            if (Str("url") is { Length: > 0 } url)
                source = Bitmap(b => { b.UriSource = new Uri(url); });
            else if (Str("file") is { Length: > 0 } file)
                source = Bitmap(b => { b.UriSource = new Uri(file, UriKind.RelativeOrAbsolute); });
            else if (Str("bytes") is { Length: > 0 } b64)
            {
                var bytes = Convert.FromBase64String(b64);
                source = Bitmap(b => { b.StreamSource = new MemoryStream(bytes); b.CacheOption = BitmapCacheOption.OnLoad; });
            }
            if (source is not null)
                return new WpfImage { Source = source, Stretch = stretch };
        }
        catch { /* fall through to the glyph on any decode error */ }
        return Text(WpfStyle.Emoji(Str("system")));
    }

    /// <summary>A BitmapImage must be configured between BeginInit/EndInit; outside that pair the
    /// source properties throw.</summary>
    static BitmapImage Bitmap(Action<BitmapImage> configure)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        configure(bmp);
        bmp.EndInit();
        return bmp;
    }

    StackPanel MakeProgress()
    {
        var panel = new StackPanel { HorizontalAlignment = WpfHorizontalAlignment.Center };
        if (Props.GetValueOrDefault("label") is string text) panel.Children.Add(Text(text));
        if (Num("value") is { } value)
            panel.Children.Add(new ProgressBar { Value = value, Maximum = 1, Width = 200, Height = 6 });
        else
            // WPF has no ProgressRing; an indeterminate bar is the in-box spinner equivalent.
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Width = 200, Height = 6 });
        ApplyStackSpacing(panel, Orientation.Vertical, 4);
        return panel;
    }

    StackPanel MakeGauge()
    {
        var panel = new StackPanel();
        if (Props.GetValueOrDefault("label") is string text) panel.Children.Add(Text(text));
        panel.Children.Add(new ProgressBar { Minimum = Num("min") ?? 0, Maximum = Num("max") ?? 1, Value = Num("value") ?? 0, Height = 6, HorizontalAlignment = WpfHorizontalAlignment.Stretch });
        ApplyStackSpacing(panel, Orientation.Vertical, 4);
        return panel;
    }

    // ---- modifiers -----------------------------------------------------------

    /// <summary>
    /// Runs a <c>.Keyframes(…)</c> timeline as WPF <see cref="DoubleAnimationUsingKeyFrames"/> clocks —
    /// one per track, each stop a <c>SplineDoubleKeyFrame</c> (or <c>LinearDoubleKeyFrame</c>) so
    /// per-segment easing survives.
    /// <para>Unlike the WinUI port there is no <see cref="Storyboard"/>: WPF's
    /// <c>IAnimatable.BeginAnimation</c> applies a clock straight to a property, which avoids having to
    /// name the transform objects in a name scope just so a storyboard can find them. Width/Height need
    /// no special opt-in here either — WPF has no compositor/dependent-animation split.</para>
    /// </summary>
    void ApplyKeyframes(Dictionary<string, object?> mod, TransformGroup transforms, ref string? transformOrigin)
    {
        if (mod.GetValueOrDefault("tracks") is not string wire || wire.Length == 0) return;
        var tracks = KeyframeWire.Parse(wire);
        if (tracks.Count == 0) return;

        var duration = N(mod, "duration", 1);
        var delay = N(mod, "delay", 0);
        var fallback = WpfStyle.CurveFor(mod.GetValueOrDefault("curve") as string);
        var repeatCount = Num(mod, "repeatCount");
        var autoreverse = (mod.GetValueOrDefault("autoreverse") as string) == "true";

        // -1 = forever; a finite count repeats that many cycles. No repeat keys at all = play once.
        var repeat = repeatCount switch
        {
            null => new RepeatBehavior(1),
            < 0 => RepeatBehavior.Forever,
            var n => new RepeatBehavior(Math.Max(1, n.Value)),
        };

        // Transform tracks need their own transform objects to animate, so they are created on demand and
        // appended to the same group the static transform modifiers use.
        ScaleTransform? scale = null;
        RotateTransform? rotate = null;
        TranslateTransform? translate = null;
        var started = new List<(IAnimatable Target, DependencyProperty Property, DoubleAnimationUsingKeyFrames Animation)>();

        foreach (var (property, stops) in tracks)
        {
            IAnimatable target;
            DependencyProperty path;
            switch (property)
            {
                case "opacity": target = Inner; path = UIElement.OpacityProperty; break;
                case "width": target = Inner; path = FrameworkElement.WidthProperty; break;
                case "height": target = Inner; path = FrameworkElement.HeightProperty; break;
                case "scale":
                case "scaleX":
                case "scaleY":
                    scale ??= AddTransform(transforms, new ScaleTransform { ScaleX = 1, ScaleY = 1 });
                    target = scale;
                    path = property == "scaleY" ? ScaleTransform.ScaleYProperty : ScaleTransform.ScaleXProperty;
                    // A uniform `scale` track drives both axes, so it queues a second clock below.
                    break;
                case "rotation":
                    rotate ??= AddTransform(transforms, new RotateTransform());
                    target = rotate;
                    path = RotateTransform.AngleProperty;
                    break;
                case "offsetX":
                case "offsetY":
                    translate ??= AddTransform(transforms, new TranslateTransform());
                    target = translate;
                    path = property == "offsetY" ? TranslateTransform.YProperty : TranslateTransform.XProperty;
                    break;
                default: continue;
            }

            started.Add((target, path, KeyframeAnimation(stops, duration, delay, fallback, repeat, autoreverse)));
            if (property == "scale")
                started.Add((scale!, ScaleTransform.ScaleYProperty, KeyframeAnimation(stops, duration, delay, fallback, repeat, autoreverse)));
        }

        if (started.Count == 0) return;
        transformOrigin ??= "center";
        // Deferred to Loaded for the same reason every other backend defers: a timeline started before the
        // element is in the visual tree plays against a zero-size layout.
        Inner.Loaded += (_, _) =>
        {
            foreach (var (target, path, animation) in started) target.BeginAnimation(path, animation);
        };
    }

    static T AddTransform<T>(TransformGroup group, T transform) where T : Transform
    {
        group.Children.Add(transform);
        return transform;
    }

    /// <summary>One track as a keyframed double animation.</summary>
    static DoubleAnimationUsingKeyFrames KeyframeAnimation(
        List<Keyframe> stops, double duration, double delay,
        AnimationCurve fallback, RepeatBehavior repeat, bool autoreverse)
    {
        var anim = new DoubleAnimationUsingKeyFrames
        {
            BeginTime = TimeSpan.FromSeconds(delay),
            Duration = new Duration(TimeSpan.FromSeconds(duration)),
            RepeatBehavior = repeat,
            AutoReverse = autoreverse,
            // Hold the final stop rather than snapping back, matching every other backend.
            FillBehavior = FillBehavior.HoldEnd,
        };
        foreach (var stop in stops)
        {
            var at = KeyTime.FromTimeSpan(TimeSpan.FromSeconds(stop.Time * duration));
            // The curve records how a value *arrives*; a spline key frame is the closest equivalent, and
            // linear needs no spline at all.
            anim.KeyFrames.Add((stop.Curve ?? fallback) switch
            {
                AnimationCurve.Linear => new LinearDoubleKeyFrame(stop.Value, at),
                var c => new SplineDoubleKeyFrame(stop.Value, at, WpfStyle.SplineFor(c)),
            });
        }
        return anim;
    }

    void ApplyModifiers()
    {
        // Shape fill from foregroundColor/background modifier.
        if (Inner is Shape shape)
        {
            var fill = ModColor("foregroundColor") ?? ModColor("background");
            if (fill is { } f) shape.Fill = new SolidColorBrush(f);
            if (double.IsNaN(shape.Width) || shape.Width == 0) shape.Width = 40;
            if (double.IsNaN(shape.Height) || shape.Height == 0) shape.Height = 40;
        }

        FrameworkElement current = Inner;
        Thickness? padding = null;
        WpfColor? background = null;
        WpfBrush? backgroundBrush = null;         // F5: gradient fill takes precedence over the flat color
        WpfColor? borderColor = null;
        double borderWidth = 1, corner = 0;
        var transforms = new TransformGroup();    // F4: scale/offset/rotation compose here
        string? transformOrigin = null;
        (double radius, double dx, double dy, WpfColor color)? shadow = null;

        foreach (var m in Modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    padding = new Thickness(N(m, "leading"), N(m, "top"), N(m, "trailing"), N(m, "bottom"));
                    break;
                case "background":
                    if (m.GetValueOrDefault("gradient") is string grad) backgroundBrush = WpfStyle.Gradient(grad);
                    else background = WpfStyle.Color(m.GetValueOrDefault("value") as string);
                    break;
                case "material":
                    // F6: a translucent tint fallback (real acrylic needs DWM composition interop).
                    var mtint = (m.GetValueOrDefault("value") as string) switch
                    { "ultraThin" => 0.55, "thin" => 0.65, "thick" => 0.85, _ => 0.75 };
                    var mdark = (m.GetValueOrDefault("dark") as string) == "true";
                    var mbase = mdark ? WpfColor.FromArgb(255, 20, 20, 22) : WpfColor.FromArgb(255, 255, 255, 255);
                    mbase.A = (byte)(mtint * 255);
                    backgroundBrush = new SolidColorBrush(mbase);
                    break;
                case "cornerRadius":
                    corner = N(m, "radius");
                    break;
                case "border":
                    borderColor = WpfStyle.Color(m.GetValueOrDefault("color") as string);
                    borderWidth = N(m, "width", 1);
                    if (N(m, "cornerRadius") > 0) corner = N(m, "cornerRadius");
                    break;
                case "shadow":
                    // Wire shape mirrors Web/Skia: radius + x/y offset + an optional color token. The
                    // default matches Web's box-shadow fallback (black @ 35%).
                    shadow = (N(m, "radius", 4), N(m, "x"), N(m, "y"),
                        WpfStyle.Color(m.GetValueOrDefault("color") as string) ?? WpfColor.FromArgb(0x59, 0, 0, 0));
                    break;
                case "opacity":
                    Inner.Opacity = N(m, "amount", 1);
                    break;
                case "disabled":
                    // UIElement.IsEnabled disables the whole subtree and greys native controls, so unlike
                    // the WinUI port this needs no Control/non-Control split.
                    Inner.IsEnabled = (m.GetValueOrDefault("value") as string) != "true";
                    break;
                case "scaleEffect":
                    transforms.Children.Add(new ScaleTransform { ScaleX = N(m, "x", 1), ScaleY = N(m, "y", 1) });
                    transformOrigin ??= m.GetValueOrDefault("value") as string;
                    break;
                case "offset":
                    transforms.Children.Add(new TranslateTransform { X = N(m, "x"), Y = N(m, "y") });
                    break;
                case "rotation":
                    transforms.Children.Add(new RotateTransform { Angle = N(m, "degrees") });
                    transformOrigin ??= m.GetValueOrDefault("value") as string;
                    break;
                case "animation":
                    // Degradation: WPF has no implicit layout-transition system (WinUI's
                    // RepositionThemeTransition, Compose's animateContentSize). An element that moves or
                    // resizes because the tree changed jumps there. Explicit `.Keyframes(…)` timelines are
                    // unaffected — those are real animations and run below.
                    break;
                case "frame":
                    if (Num(m, "width") is { } w) current.Width = w;
                    if (Num(m, "height") is { } h) current.Height = h;
                    if (m.GetValueOrDefault("alignment") is string fa) { current.HorizontalAlignment = AlignH(fa); current.VerticalAlignment = AlignV(fa); }
                    break;
                case "align":
                    current.HorizontalAlignment = AlignH(m.GetValueOrDefault("value") as string);
                    break;
                case "onTapGesture":
                    if (m.GetValueOrDefault("event") is string ev)
                    {
                        EnsureHitTestable(current);
                        var wantDouble = N(m, "amount", 1) >= 2;
                        // WPF has no Tapped/DoubleTapped pair; ClickCount on the button-down event is how
                        // a double click is distinguished.
                        current.MouseLeftButtonUp += (_, e) =>
                        {
                            if (wantDouble == (e.ClickCount >= 2)) _bridge.Emit(ev, null);
                        };
                    }
                    break;
                case "onLongPress":
                    if (m.GetValueOrDefault("event") is string lev)
                    {
                        EnsureHitTestable(current);
                        AttachLongPress(current, lev);
                    }
                    break;
                case "onSwipe":
                    if (m.GetValueOrDefault("event") is string sev)
                    {
                        EnsureHitTestable(current);
                        AttachSwipe(current, sev, m.GetValueOrDefault("value") as string);
                    }
                    break;
                case "onDrag":
                    // F1 continuous drag → "<phase>;tx,ty;lx,ly;vx,vy".
                    if (m.GetValueOrDefault("event") is string dev)
                    {
                        EnsureHitTestable(current);
                        AttachDrag(current, dev);
                    }
                    break;
                case "onMagnify":
                    // F1 pinch → cumulative scale factor. A WPF desktop app gets no pinch from a mouse, so
                    // ctrl+wheel is the zoom gesture, matching the Skia desktop heads. A precision
                    // touchpad/touchscreen also delivers ctrl+wheel for its own pinch, so real pinch
                    // hardware works without a separate path.
                    if (m.GetValueOrDefault("event") is string mev)
                    {
                        EnsureHitTestable(current);
                        AttachMagnify(current, mev);
                    }
                    break;
                case "foregroundColor":
                    if (Inner is TextBlock tbf && WpfStyle.Brush(m.GetValueOrDefault("value") as string) is { } fg) tbf.Foreground = fg;
                    break;
                case "font":
                    if (Inner is TextBlock tbn && WpfStyle.Font(m.GetValueOrDefault("value") as string) is { } font)
                    { tbn.FontSize = font.size; tbn.FontWeight = font.weight; }
                    break;
            }
        }

        // A `.Keyframes(…)` timeline contributes its own transforms, so it runs before the group is sealed.
        var keyframes = Modifiers.FirstOrDefault(m => m["type"] as string == "keyframes");
        if (keyframes is not null) ApplyKeyframes(keyframes, transforms, ref transformOrigin);

        if (transforms.Children.Count > 0)
        {
            Inner.RenderTransform = transforms;
            Inner.RenderTransformOrigin = OriginPoint(transformOrigin);
        }

        if (padding is not null || background is not null || backgroundBrush is not null || borderColor is not null || corner > 0)
        {
            var wrapper = new Border { Child = Inner };
            if (padding is { } p) wrapper.Padding = p;
            if (backgroundBrush is { } gb) wrapper.Background = gb;
            else if (background is { } bg) wrapper.Background = new SolidColorBrush(bg);
            if (borderColor is { } bc) { wrapper.BorderBrush = new SolidColorBrush(bc); wrapper.BorderThickness = new Thickness(borderWidth); }
            if (corner > 0)
            {
                wrapper.CornerRadius = new CornerRadius(corner);
                // Unlike WinUI, a WPF Border does NOT clip its child to the corner radius; without this
                // a rounded background is drawn under square content and the corners look filled in.
                wrapper.ClipToBounds = true;
            }
            wrapper.HorizontalAlignment = Inner.HorizontalAlignment;
            Element = wrapper;
        }
        else
        {
            Element = Inner;
        }

        // The shadow applies to whatever the modifier chain produced (so it hugs the padded/bordered box).
        if (shadow is { } sh) ApplyShadow(Element, sh.radius, sh.dx, sh.dy, sh.color);
    }

    /// <summary>
    /// Casts a drop shadow behind <paramref name="element"/>.
    /// <para>WPF has a real <see cref="DropShadowEffect"/> that takes blur radius, offset and colour
    /// directly and derives the silhouette from the content's alpha — so unlike the WinUI backend (whose
    /// Composition sprite always casts a rectangle) a rounded or non-rectangular element casts the right
    /// shape, matching Web and Skia. No wrapper element is needed at all.</para>
    /// <para>The offset is polar in WPF: Direction is measured counter-clockwise from the positive x-axis
    /// with y pointing <em>up</em>, hence the negated dy.</para>
    /// </summary>
    static void ApplyShadow(FrameworkElement element, double radius, double dx, double dy, WpfColor color)
    {
        element.Effect = new DropShadowEffect
        {
            BlurRadius = radius,
            ShadowDepth = Math.Sqrt(dx * dx + dy * dy),
            Direction = Math.Atan2(-dy, dx) * 180.0 / Math.PI,
            // DropShadowEffect ignores the alpha channel of Color and takes it from Opacity instead.
            Color = WpfColor.FromRgb(color.R, color.G, color.B),
            Opacity = color.A / 255.0,
        };
    }

    /// <summary>
    /// A WPF <see cref="Panel"/>, <see cref="Border"/> or <see cref="TextBlock"/> with no Background is
    /// invisible to hit-testing — mouse events pass straight through it. Every gesture modifier therefore
    /// has to guarantee the element it attaches to can actually be hit.
    /// </summary>
    static void EnsureHitTestable(FrameworkElement element)
    {
        switch (element)
        {
            case Panel { Background: null } panel: panel.Background = Brushes.Transparent; break;
            case Border { Background: null } border: border.Background = Brushes.Transparent; break;
            case Control { Background: null } control: control.Background = Brushes.Transparent; break;
        }
    }

    /// <summary>
    /// Long press. WPF has no Holding event (WinUI does), so a hold is timed here: the timer starts on
    /// button-down and is cancelled by an early release or by moving outside the tap slop.
    /// A right-click is also accepted, the mouse/trackpad equivalent of a press-and-hold.
    /// </summary>
    void AttachLongPress(FrameworkElement element, string ev)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        var origin = new Point();
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _bridge.Emit(ev, null);
        };
        element.MouseLeftButtonDown += (_, e) => { origin = e.GetPosition(element); timer.Start(); };
        element.MouseLeftButtonUp += (_, _) => timer.Stop();
        element.MouseLeave += (_, _) => timer.Stop();
        element.MouseMove += (_, e) =>
        {
            if (!timer.IsEnabled) return;
            var p = e.GetPosition(element);
            if (Math.Abs(p.X - origin.X) > 8 || Math.Abs(p.Y - origin.Y) > 8) timer.Stop();
        };
        element.MouseRightButtonUp += (_, _) => _bridge.Emit(ev, null);
    }

    /// <summary>Directional swipe, resolved from a captured mouse drag (WPF raises manipulation events
    /// for touch only, and only after IsManipulationEnabled — a mouse never produces them).</summary>
    void AttachSwipe(FrameworkElement element, string ev, string? direction)
    {
        var start = new Point();
        var tracking = false;
        element.MouseLeftButtonDown += (_, e) => { start = e.GetPosition(element); tracking = true; element.CaptureMouse(); };
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (!tracking) return;
            tracking = false;
            element.ReleaseMouseCapture();
            var p = e.GetPosition(element);
            var dx = p.X - start.X;
            var dy = p.Y - start.Y;
            var matched = Math.Abs(dx) > Math.Abs(dy)
                ? (dx < 0 ? direction == "left" : direction == "right")
                : (dy < 0 ? direction == "up" : direction == "down");
            if (matched && (Math.Abs(dx) > 40 || Math.Abs(dy) > 40)) _bridge.Emit(ev, null);
        };
    }

    /// <summary>
    /// F1 continuous drag as "&lt;phase&gt;;tx,ty;lx,ly;vx,vy". The velocity WPF does not report is
    /// estimated from the last movement and the wall-clock gap between the final two samples, which is
    /// what the fling behaviour on the other backends is driven by.
    /// </summary>
    void AttachDrag(FrameworkElement element, string ev)
    {
        var start = new Point();
        var last = new Point();
        var lastTicks = 0L;
        var velocity = new Vector();
        var dragging = false;

        element.MouseLeftButtonDown += (_, e) =>
        {
            start = last = e.GetPosition(element);
            lastTicks = DateTime.UtcNow.Ticks;
            velocity = default;
            dragging = true;
            element.CaptureMouse();
            _bridge.Emit(ev, FormattableString.Invariant($"b;0,0;{start.X},{start.Y};0,0"));
        };
        element.MouseMove += (_, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(element);
            var now = DateTime.UtcNow.Ticks;
            var dt = (now - lastTicks) / (double)TimeSpan.TicksPerSecond;
            if (dt > 0.0001) velocity = new Vector((p.X - last.X) / dt, (p.Y - last.Y) / dt);
            last = p;
            lastTicks = now;
            _bridge.Emit(ev, FormattableString.Invariant($"c;{p.X - start.X},{p.Y - start.Y};{p.X},{p.Y};0,0"));
        };
        element.MouseLeftButtonUp += (_, e) =>
        {
            if (!dragging) return;
            dragging = false;
            element.ReleaseMouseCapture();
            var p = e.GetPosition(element);
            _bridge.Emit(ev, FormattableString.Invariant($"e;{p.X - start.X},{p.Y - start.Y};{p.X},{p.Y};{velocity.X},{velocity.Y}"));
        };
    }

    /// <summary>F1 pinch → the cumulative scale factor, from ctrl+wheel (see the call site).</summary>
    void AttachMagnify(FrameworkElement element, string ev)
    {
        var scale = 1.0;
        element.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
            scale = Math.Clamp(scale * (1 + e.Delta / 120.0 * 0.05), 0.2, 10);
            _bridge.Emit(ev, scale.ToString(CultureInfo.InvariantCulture));
            e.Handled = true;
        };
    }

    WpfColor? ModColor(string type)
        => WpfStyle.Color(Modifiers.FirstOrDefault(m => m["type"] as string == type)?.GetValueOrDefault("value") as string);

    // ---- patch application ---------------------------------------------------

    public void UpdateProps(JsonElement props, JsonElement modifiers)
    {
        Props = ReadDict(props);
        Modifiers = ReadDictArray(modifiers);

        switch (Type)
        {
            case "Text": ((TextBlock)Inner).Text = Str("text"); break;
            case "ZStack": ApplyZStackAlignment((WpfGrid)Inner); break;
            case "Button": ((WpfButton)Inner).Content = Str("title"); break;
            case "TextField" or "TextEditor": SyncText(Str("text")); break;
            case "Toggle": SyncToggle(); break;
            case "Slider": SyncSlider(); break;
            case "Stepper": SyncStepper(); break;
            case "DisclosureGroup": ((Expander)Inner).IsExpanded = Bool("expanded"); break;
            case "TabView": SyncTabView(); break;
            case "Sheet": SyncSheetDialog(); break;
            case "Alert" or "ActionSheet": SyncAlertDialog(); break;
            default: _customRenderer?.Update(Inner, RenderCtx()); break;
        }
    }

    // Each of these guards on "is it already this value?" — the control's own change event emits back to
    // Core, so writing unconditionally would bounce a value the user is mid-edit.
    void SyncText(string value)
    {
        if (_textBox is null || _textBox.Text == value) return;
        _textBox.Text = value;
        SyncPlaceholder();
    }

    void SyncToggle()
    {
        var cb = (CheckBox)Inner;
        if (cb.IsChecked != Bool("value")) cb.IsChecked = Bool("value");
    }

    void SyncSlider()
    {
        var s = (WpfSlider)Inner;
        if (Math.Abs(s.Value - (Num("value") ?? 0)) > 0.0001) s.Value = Num("value") ?? 0;
    }

    void SyncStepper()
    {
        if (_stepperValue is null) return;
        var text = ((int)(Num("value") ?? 0)).ToString(CultureInfo.InvariantCulture);
        if (_stepperValue.Text != text) _stepperValue.Text = text;
    }

    // ---- dialogs -------------------------------------------------------------
    // WPF has no ContentDialog. A real modal Window.ShowDialog() is not an option either: it blocks its
    // caller until dismissed, and the caller here is Render(), part-way through applying a patch — the
    // render loop would stall and re-enter. So a dialog is an overlay layer stacked into the bridge's
    // host Grid: a dimmed scrim plus a card, above the content and covering it.

    FrameworkElement? _sheetLayer;
    FrameworkElement? _alertLayer;

    void SyncSheetDialog()
    {
        if (Bool("presented"))
        {
            if (_sheetLayer is not null) return;
            var content = Children.Count > 1 ? Children[1].Element : Text("");
            var body = new StackPanel();
            body.Children.Add(content);
            var close = new WpfButton { Content = "Close", HorizontalAlignment = WpfHorizontalAlignment.Right, Padding = new Thickness(12, 6, 12, 6) };
            close.Click += (_, _) => _bridge.Emit(Id, "false");
            body.Children.Add(close);
            ApplyStackSpacing(body, Orientation.Vertical, 12);

            _sheetLayer = Scrim(Card(body), () => _bridge.Emit(Id, "false"));
            _bridge.ShowOverlay(_sheetLayer);
        }
        else if (_sheetLayer is not null)
        {
            _bridge.HideOverlay(_sheetLayer);
            _sheetLayer = null;
        }
    }

    /// <summary>
    /// Alert and ActionSheet both land on the same overlay card — Windows has no action-sheet idiom.
    /// Every button is laid out in the card (there is no three-slot limit to work around, unlike WinUI's
    /// ContentDialog), with the cancel button last and also bound to the scrim and Esc, which are the
    /// dialog's other exits.
    /// </summary>
    void SyncAlertDialog()
    {
        if (!Bool("presented"))
        {
            if (_alertLayer is not null) { _bridge.HideOverlay(_alertLayer); _alertLayer = null; }
            return;
        }
        if (_alertLayer is not null) return;

        var buttons = DialogButtons.Parse(Str("buttons"));
        var cancel = DialogButtons.CancelIndex(buttons);

        var body = new StackPanel { MinWidth = 260 };
        if (Str("title").Length > 0)
            body.Children.Add(new TextBlock { Text = Str("title"), FontWeight = FontWeights.SemiBold, FontSize = 16, TextWrapping = TextWrapping.Wrap });
        if (Str("message").Length > 0)
            body.Children.Add(new TextBlock { Text = Str("message"), TextWrapping = TextWrapping.Wrap });

        void Choose(int index)
        {
            if (_alertLayer is not null) { _bridge.HideOverlay(_alertLayer); _alertLayer = null; }
            _bridge.Emit(Id, index.ToString(CultureInfo.InvariantCulture));
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            if (i == cancel) continue;   // the cancel button is appended last
            var index = i;
            body.Children.Add(DialogButton(buttons[i], () => Choose(index)));
        }
        if (cancel >= 0)
        {
            var index = cancel;
            body.Children.Add(DialogButton(buttons[cancel], () => Choose(index)));
        }
        ApplyStackSpacing(body, Orientation.Vertical, 8);

        // Dismissed without choosing (scrim click / Esc) reports "false", the wire's own token for it.
        _alertLayer = Scrim(Card(body), () =>
        {
            if (_alertLayer is not null) { _bridge.HideOverlay(_alertLayer); _alertLayer = null; }
            _bridge.Emit(Id, "false");
        });
        _bridge.ShowOverlay(_alertLayer);
    }

    WpfButton DialogButton((string Label, DialogRole Role) button, Action onClick)
    {
        var b = new WpfButton
        {
            Content = button.Label,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Padding = new Thickness(12, 6, 12, 6),
        };
        if (button.Role == DialogRole.Destructive) b.Foreground = WpfStyle.Brush("red");
        if (button.Role == DialogRole.Default) b.FontWeight = FontWeights.SemiBold;
        b.Click += (_, _) => onClick();
        return b;
    }

    static Border Card(FrameworkElement content) => new()
    {
        Background = SystemColors.WindowBrush,
        BorderBrush = WpfStyle.Brush("secondary"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(20),
        MaxWidth = 420,
        HorizontalAlignment = WpfHorizontalAlignment.Center,
        VerticalAlignment = WpfVerticalAlignment.Center,
        Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
    };

    /// <summary>The dimming layer. It must have a Background to be hit-testable, which is also what stops
    /// clicks reaching the content underneath — the point of a modal.</summary>
    static WpfGrid Scrim(FrameworkElement card, Action onDismiss)
    {
        var layer = new WpfGrid { Background = new SolidColorBrush(WpfColor.FromArgb(0x66, 0, 0, 0)) };
        layer.MouseLeftButtonDown += (s, e) => { if (ReferenceEquals(e.OriginalSource, s)) onDismiss(); };
        layer.Children.Add(card);
        // Focus the layer so Esc reaches it; without Focusable it is skipped by the keyboard entirely.
        layer.Focusable = true;
        layer.KeyDown += (_, e) => { if (e.Key == Key.Escape) onDismiss(); };
        layer.Loaded += (_, _) => layer.Focus();
        return layer;
    }

    public void SetChildren(JsonElement children)
    {
        // Reconcile the child WpfNode list, reusing elements by key. TreeDiffer emits setChildren both
        // for a keyed key-sequence change (insert/remove/move) and for any structural change to a child
        // list — so a keyed container recycles surviving rows (reusing a matched row's already-built
        // element preserves its control state and IS the recycling) while a non-keyed one rebuilds.
        ReconcileChildren(children);

        // Each container installed the re-lay that is correct for it (see _relayout).
        _relayout?.Invoke();
    }

    void ReconcileChildren(JsonElement children)
    {
        var keyed = Props.GetValueOrDefault("keyed") as bool? == true;
        var byKey = new Dictionary<string, WpfNode>();
        if (keyed)
            foreach (var c in Children)
                if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<WpfNode>();
        foreach (var el in children.EnumerateArray())
        {
            var key = keyed && el.TryGetProperty("props", out var p) && p.TryGetProperty("key", out var kp)
                ? kp.GetString() : null;
            next.Add(key is not null && byKey.TryGetValue(key, out var reuse) && reuse.Type == el.GetProperty("type").GetString()
                ? reuse
                : Build(el, _bridge));
        }

        // A reused element is still parented to the old panel; WPF throws if it is added to a second one.
        foreach (var child in next) Detach(child.Element);

        Children.Clear();
        Children.AddRange(next);
    }

    /// <summary>Unparents an element so it can be re-added elsewhere (WPF forbids two logical parents).</summary>
    static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentControl cc when ReferenceEquals(cc.Content, element): cc.Content = null; break;
        }
    }

    // ---- helpers -------------------------------------------------------------

    static WpfHorizontalAlignment AlignH(string? t) => t switch
    {
        "leading" or "topLeading" or "bottomLeading" => WpfHorizontalAlignment.Left,
        "trailing" or "topTrailing" or "bottomTrailing" => WpfHorizontalAlignment.Right,
        _ => WpfHorizontalAlignment.Center,
    };

    static WpfVerticalAlignment AlignV(string? t) => t switch
    {
        "top" or "topLeading" or "topTrailing" => WpfVerticalAlignment.Top,
        "bottom" or "bottomLeading" or "bottomTrailing" => WpfVerticalAlignment.Bottom,
        _ => WpfVerticalAlignment.Center,
    };

    string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    string? StrOrNull(string key) => Props.TryGetValue(key, out var v) ? v as string : null;
    double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
    static double? Num(Dictionary<string, object?> m, string key) => m.TryGetValue(key, out var v) && v is double d ? d : null;
    static double N(Dictionary<string, object?> m, string key, double fallback = 0) => m.TryGetValue(key, out var v) && v is double d ? d : fallback;

    static Point OriginPoint(string? t)
    {
        double fx = t is "leading" or "topLeading" or "bottomLeading" ? 0
                  : t is "trailing" or "topTrailing" or "bottomTrailing" ? 1 : 0.5;
        double fy = t is "top" or "topLeading" or "topTrailing" ? 0
                  : t is "bottom" or "bottomLeading" or "bottomTrailing" ? 1 : 0.5;
        return new Point(fx, fy);
    }

    static Dictionary<string, object?> ReadDict(JsonElement e)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in e.EnumerateObject())
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        return d;
    }

    static List<Dictionary<string, object?>> ReadDictArray(JsonElement e)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var item in e.EnumerateArray()) list.Add(ReadDict(item));
        return list;
    }
}
