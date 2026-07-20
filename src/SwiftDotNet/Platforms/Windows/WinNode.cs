using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

// Core declares View types (Grid, Button, Slider, ColorPicker, TabView, Rectangle) and the enums
// HorizontalAlignment / VerticalAlignment in this same namespace (SwiftDotNet). A simple name binds to the
// enclosing namespace's member before any using-imported one, so the WinUI types are reached through these
// distinctly-named aliases (a same-name alias would itself collide with the namespace member).
using WinButton = Microsoft.UI.Xaml.Controls.Button;
using WinColorPicker = Microsoft.UI.Xaml.Controls.ColorPicker;
using WinGrid = Microsoft.UI.Xaml.Controls.Grid;
using WinHorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using WinRectangle = Microsoft.UI.Xaml.Shapes.Rectangle;
using WinSlider = Microsoft.UI.Xaml.Controls.Slider;
using WinTabView = Microsoft.UI.Xaml.Controls.TabView;
using WinVerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace SwiftDotNet;

/// <summary>A node in the retained WinUI element tree — mirrors the wire node and holds its live control.</summary>
sealed class WinNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<WinNode> Children { get; } = new();

    /// <summary>The base control (used for value sync / children).</summary>
    public FrameworkElement Inner { get; private set; } = null!;

    /// <summary>The outermost element added to the parent (Inner, or a modifier Border wrapping it).</summary>
    public FrameworkElement Element { get; private set; } = null!;

    WinBridge _bridge = null!;
    WinNavController? _nav;

    public static WinNode Build(JsonElement e, WinBridge bridge)
    {
        var node = new WinNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        node._bridge = bridge;

        if (node.Type == "NavigationStack")
        {
            node._nav = new WinNavController();
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
        "Spacer" => new Border { HorizontalAlignment = WinHorizontalAlignment.Stretch, VerticalAlignment = WinVerticalAlignment.Stretch },
        "Divider" => new Border { Height = 1, Background = WinStyle.Brush("secondary"), HorizontalAlignment = WinHorizontalAlignment.Stretch },
        "VStack" => Stack(Orientation.Vertical),
        "HStack" => Stack(Orientation.Horizontal),
        "ZStack" => MakeZStack(),
        "ScrollView" => MakeScroll(),
        "Grid" => MakeGrid(),
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
        "Alert" => Children.Count > 0 ? Children[0].Element : Text(""),
        "WebView" => MakeWebView(),
        "Image" => MakeImage(),
        "Label" => MakeLabel(),
        "ProgressView" => MakeProgress(),
        "Gauge" => MakeGauge(),
        "Link" => new HyperlinkButton { Content = Str("title"), NavigateUri = Uri.TryCreate(Str("url"), UriKind.Absolute, out var u) ? u : null },
        "Rectangle" => new WinRectangle(),
        "Circle" => new Ellipse(),
        "Capsule" => new WinRectangle { RadiusX = 999, RadiusY = 999 },
        "RoundedRectangle" => new WinRectangle { RadiusX = Num("cornerRadius") ?? 8, RadiusY = Num("cornerRadius") ?? 8 },
        _ => CustomOrPlaceholder(),
    };

    IWinRenderer? _customRenderer;

    FrameworkElement CustomOrPlaceholder()
    {
        _customRenderer = WinRenderers.Get(Type);
        return _customRenderer is { } r ? r.Create(RenderCtx()) : Text($"⚠️ {Type}");
    }

    WinRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap };

    FrameworkElement MakeWebView()
    {
        var web = new WebView2 { MinHeight = 300, HorizontalAlignment = WinHorizontalAlignment.Stretch };
        var url = Str("url");
        if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var u))
            web.Source = u;
        else if (Props.GetValueOrDefault("html") is string html)
            _ = LoadHtmlAsync(web, html);
        return web;
    }

    static async Task LoadHtmlAsync(WebView2 web, string html)
    {
        await web.EnsureCoreWebView2Async();
        web.NavigateToString(html);
    }

    FrameworkElement MakeButton()
    {
        var b = new WinButton { Content = Str("title"), HorizontalAlignment = WinHorizontalAlignment.Center };
        b.Click += (_, _) => _bridge.Emit(Id, null);
        return b;
    }

    StackPanel Stack(Orientation orientation)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = Num("spacing") ?? 0 };
        var align = Props.GetValueOrDefault("alignment") as string;
        if (orientation == Orientation.Vertical)
            panel.HorizontalAlignment = align is null ? WinHorizontalAlignment.Center : AlignH(align);
        else
            panel.VerticalAlignment = align is null ? WinVerticalAlignment.Center : AlignV(align);
        foreach (var c in Children) panel.Children.Add(c.Element);
        return panel;
    }

    WinGrid MakeZStack()
    {
        var grid = new WinGrid();
        foreach (var c in Children) grid.Children.Add(c.Element);
        ApplyZStackAlignment(grid);
        return grid;
    }

    /// <summary>ZStack's <c>alignment</c> prop (an <see cref="Alignment"/> token) has no Grid-level equivalent
    /// in WinUI — a Grid positions each child independently — so it is pushed onto every child. The prop is
    /// only serialized when the DSL sets it explicitly, but note it does override a child's own alignment.</summary>
    void ApplyZStackAlignment(WinGrid grid)
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
        var inner = new StackPanel
        {
            Orientation = Str("axis") == "horizontal" ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = WinHorizontalAlignment.Center,
        };
        foreach (var c in Children) inner.Children.Add(c.Element);
        return new ScrollViewer { Content = inner };
    }

    WinGrid MakeGrid()
    {
        var cols = (int)(Num("columns") ?? 2);
        var sp = Num("spacing") ?? 8;
        var grid = new WinGrid { ColumnSpacing = sp, RowSpacing = sp };
        for (var i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < Children.Count; i++)
        {
            var row = i / cols;
            if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var el = Children[i].Element;
            WinGrid.SetColumn(el, i % cols);
            WinGrid.SetRow(el, row);
            grid.Children.Add(el);
        }
        return grid;
    }

    /// <summary>The panel that directly hosts a container's child rows, when the container wraps them in
    /// chrome (List). SetChildren re-lays children into this instead of the wrapper.</summary>
    Panel? _childHost;

    Border MakeList()
    {
        // Grid / horizontal lists reuse the standard grid / horizontal-stack layout (keyed live-reorder is
        // supported for the default vertical list via _childHost; grid/horizontal rebuild on change).
        if (Str("layout") == "grid")
            return new Border { CornerRadius = new CornerRadius(8), Child = MakeGrid() };
        if (Str("axis") == "horizontal")
        {
            var h = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            foreach (var c in Children) h.Children.Add(c.Element);
            return new Border { CornerRadius = new CornerRadius(8), Child = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollMode = ScrollMode.Enabled, Content = h } };
        }

        var panel = new StackPanel();
        _childHost = panel;
        LayoutListRows(panel);
        return new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = WinStyle.Brush("secondary"), Child = panel };
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
                    row.Background = new SolidColorBrush(Microsoft.UI.Colors.DodgerBlue) { Opacity = 0.2 };
                row.Tapped += (_, _) => _bridge.Emit(Id, key);
            }
            panel.Children.Add(row);
            if (i < Children.Count - 1)
                panel.Children.Add(new Border { Height = 1, Background = WinStyle.Brush("secondary") });
        }
    }

    ScrollViewer MakeForm()
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (var c in Children) panel.Children.Add(c.Element);
        return new ScrollViewer { Content = panel };
    }

    StackPanel MakeSection()
    {
        var panel = new StackPanel { Spacing = 6 };
        if (Props.GetValueOrDefault("header") is string header)
            panel.Children.Add(new TextBlock { Text = header, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var c in Children) panel.Children.Add(c.Element);
        return panel;
    }

    Expander MakeDisclosure()
    {
        var content = new StackPanel { Spacing = 4 };
        foreach (var c in Children) content.Children.Add(c.Element);
        var expander = new Expander { Header = Str("label"), Content = content, IsExpanded = Bool("expanded"), HorizontalAlignment = WinHorizontalAlignment.Stretch };
        expander.Expanding += (_, _) => _bridge.Emit(Id, "true");
        expander.Collapsed += (_, _) => _bridge.Emit(Id, "false");
        return expander;
    }

    WinTabView MakeTabView()
    {
        var tabs = new WinTabView { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
        foreach (var tab in Children)
            tabs.TabItems.Add(new TabViewItem { Header = tab.Str("title"), Content = tab.Element, IsClosable = false });
        if (Props.ContainsKey("selectedIndex"))
        {
            tabs.SelectedIndex = (int)(Num("selectedIndex") ?? 0);
            tabs.SelectionChanged += (_, _) => _bridge.Emit(Id, tabs.SelectedIndex.ToString());
        }
        return tabs;
    }

    void SyncTabView()
    {
        if (Inner is WinTabView tv && Props.ContainsKey("selectedIndex"))
        {
            var idx = (int)(Num("selectedIndex") ?? 0);
            if (tv.SelectedIndex != idx) tv.SelectedIndex = idx;
        }
    }

    WinButton MakeMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var c in Children)
        {
            var item = new MenuFlyoutItem { Text = c.Str("title") };
            var childId = c.Id;
            item.Click += (_, _) => _bridge.Emit(childId, null);
            flyout.Items.Add(item);
        }
        return new WinButton { Content = Str("label"), Flyout = flyout };
    }

    // ---- controls (two-way bound) -------------------------------------------

    TextBox MakeTextField()
    {
        var tb = new TextBox { PlaceholderText = Str("placeholder"), Text = Str("text"), HorizontalAlignment = WinHorizontalAlignment.Stretch };
        if (KeyboardScope() is { } scope) tb.InputScope = scope;
        if (Num("maxLength") is { } max) tb.MaxLength = (int)max;
        tb.TextChanged += (_, _) => _bridge.Emit(Id, tb.Text);
        return tb;
    }

    PasswordBox MakeSecureField()
    {
        var pb = new PasswordBox { PlaceholderText = Str("placeholder"), Password = Str("text"), HorizontalAlignment = WinHorizontalAlignment.Stretch };
        if (KeyboardScope() is { } scope) pb.InputScope = scope;
        if (Num("maxLength") is { } max) pb.MaxLength = (int)max;
        pb.PasswordChanged += (_, _) => _bridge.Emit(Id, pb.Password);
        return pb;
    }

    /// <summary>F9 <c>keyboard</c> prop → a WinUI <c>InputScope</c>, which is what drives the touch
    /// keyboard layout on Windows. Returns null for the default (unset) keyboard so the field keeps the
    /// platform default.
    /// <para>Degradation: the F9 <c>returnKey</c> prop (done/go/next/search/send) has NO WinUI equivalent —
    /// unlike UIKit's <c>submitLabel</c> or Android's <c>ImeAction</c>, the Windows touch keyboard's Enter
    /// key label is not settable from XAML, so the value is deliberately ignored rather than faked.</para>
    /// <para>Degradation: <c>maxLength</c> is applied here, but Core also clamps in-binding, so a field whose
    /// max changes after build stays correct in state even though the control's own cap is build-time only
    /// (modifiers/props of this kind are not re-applied by <see cref="UpdateProps"/>).</para></summary>
    Microsoft.UI.Xaml.Input.InputScope? KeyboardScope()
    {
        var value = Str("keyboard") switch
        {
            // "number" is SwiftUI's numberPad → digits only; "decimal" allows the decimal separator.
            "number" => Microsoft.UI.Xaml.Input.InputScopeNameValue.Digits,
            "decimal" => Microsoft.UI.Xaml.Input.InputScopeNameValue.Number,
            "email" => Microsoft.UI.Xaml.Input.InputScopeNameValue.EmailSmtpAddress,
            "phone" => Microsoft.UI.Xaml.Input.InputScopeNameValue.TelephoneNumber,
            "url" => Microsoft.UI.Xaml.Input.InputScopeNameValue.Url,
            _ => (Microsoft.UI.Xaml.Input.InputScopeNameValue?)null,
        };
        if (value is not { } v) return null;
        var scope = new Microsoft.UI.Xaml.Input.InputScope();
        scope.Names.Add(new Microsoft.UI.Xaml.Input.InputScopeName(v));
        return scope;
    }

    TextBox MakeTextEditor()
    {
        var tb = new TextBox { Text = Str("text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, HorizontalAlignment = WinHorizontalAlignment.Stretch };
        tb.TextChanged += (_, _) => _bridge.Emit(Id, tb.Text);
        return tb;
    }

    ToggleSwitch MakeToggle()
    {
        var ts = new ToggleSwitch { Header = Str("label"), IsOn = Bool("value") };
        ts.Toggled += (_, _) => _bridge.Emit(Id, ts.IsOn ? "true" : "false");
        return ts;
    }

    WinSlider MakeSlider()
    {
        var slider = new WinSlider { Minimum = Num("min") ?? 0, Maximum = Num("max") ?? 1, StepFrequency = 0.01, Value = Num("value") ?? 0, HorizontalAlignment = WinHorizontalAlignment.Stretch };
        slider.ValueChanged += (_, e) => _bridge.Emit(Id, e.NewValue.ToString(CultureInfo.InvariantCulture));
        return slider;
    }

    StackPanel MakeStepper()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WinVerticalAlignment.Center });
        var box = new NumberBox { Minimum = Num("min") ?? -1e9, Maximum = Num("max") ?? 1e9, SmallChange = 1, Value = Num("value") ?? 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        box.ValueChanged += (_, _) => _bridge.Emit(Id, ((int)box.Value).ToString(CultureInfo.InvariantCulture));
        panel.Children.Add(box);
        return panel;
    }

    StackPanel MakePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WinVerticalAlignment.Center });
        var combo = new ComboBox { ItemsSource = Children.Select(c => c.Str("text")).ToList(), SelectedIndex = (int)(Num("selection") ?? 0) };
        combo.SelectionChanged += (_, _) => _bridge.Emit(Id, combo.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        panel.Children.Add(combo);
        return panel;
    }

    StackPanel MakeDatePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WinVerticalAlignment.Center });
        var picker = new CalendarDatePicker { Date = DateTimeOffset.FromUnixTimeSeconds((long)(Num("value") ?? 0)) };
        picker.DateChanged += (_, e) =>
        {
            if (e.NewDate is { } d) _bridge.Emit(Id, d.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        };
        panel.Children.Add(picker);
        return panel;
    }

    StackPanel MakeColorPicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = WinVerticalAlignment.Center });

        var colorPicker = new WinColorPicker { IsMoreButtonVisible = false, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false };
        if (WinStyle.Color(Str("value")) is { } c) colorPicker.Color = c;
        colorPicker.ColorChanged += (_, e) =>
            _bridge.Emit(Id, $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}");

        var swatch = new Border { Width = 32, Height = 24, CornerRadius = new CornerRadius(4), Background = WinStyle.Brush(Str("value")) };
        var flyout = new Flyout { Content = colorPicker };
        var button = new WinButton { Content = swatch, Flyout = flyout, Padding = new Thickness(2) };
        panel.Children.Add(button);
        return panel;
    }

    // ---- navigation ----------------------------------------------------------

    WinButton MakeNavLink()
    {
        var nav = _bridge.NavStack.Count > 0 ? _bridge.NavStack.Peek() : null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = WinHorizontalAlignment.Stretch };
        if (Children.Count > 0) row.Children.Add(Children[0].Element);
        row.Children.Add(new TextBlock { Text = "›", HorizontalAlignment = WinHorizontalAlignment.Right });
        var button = new WinButton { Content = row, HorizontalAlignment = WinHorizontalAlignment.Stretch };
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
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(Text(WinStyle.Emoji(Str("systemImage"))));
        panel.Children.Add(Text(Str("title")));
        return panel;
    }

    // F3 raster: a real bitmap from url / file / bytes; an SF-Symbol name falls back to the emoji glyph.
    FrameworkElement MakeImage()
    {
        try
        {
            var stretch = Str("contentMode") == "fill"
                ? Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                : Microsoft.UI.Xaml.Media.Stretch.Uniform;
            Microsoft.UI.Xaml.Media.Imaging.BitmapImage? source = null;
            if (Str("url") is { Length: > 0 } url)
                source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(url));
            else if (Str("file") is { Length: > 0 } file)
                source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(file, UriKind.RelativeOrAbsolute));
            else if (Str("bytes") is { Length: > 0 } b64)
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                using (var writer = new Windows.Storage.Streams.DataWriter(stream))
                {
                    writer.WriteBytes(bytes);
                    writer.StoreAsync().GetAwaiter().GetResult();
                    writer.DetachStream();
                }
                stream.Seek(0);
                bmp.SetSource(stream);
                source = bmp;
            }
            if (source is not null)
                return new Microsoft.UI.Xaml.Controls.Image { Source = source, Stretch = stretch };
        }
        catch { /* fall through to the glyph on any decode error */ }
        return Text(WinStyle.Emoji(Str("system")));
    }

    StackPanel MakeProgress()
    {
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = WinHorizontalAlignment.Center };
        if (Props.GetValueOrDefault("label") is string text) panel.Children.Add(Text(text));
        if (Num("value") is { } value)
            panel.Children.Add(new ProgressBar { Value = value, Maximum = 1, Width = 200 });
        else
            panel.Children.Add(new ProgressRing { IsActive = true });
        return panel;
    }

    StackPanel MakeGauge()
    {
        var panel = new StackPanel { Spacing = 4 };
        if (Props.GetValueOrDefault("label") is string text) panel.Children.Add(Text(text));
        panel.Children.Add(new ProgressBar { Minimum = Num("min") ?? 0, Maximum = Num("max") ?? 1, Value = Num("value") ?? 0, HorizontalAlignment = WinHorizontalAlignment.Stretch });
        return panel;
    }

    // ---- modifiers -----------------------------------------------------------

    void ApplyModifiers()
    {
        // Shape fill from foregroundColor/background modifier.
        if (Inner is Shape shape)
        {
            var fill = ModColor("foregroundColor") ?? ModColor("background");
            if (fill is { } f) shape.Fill = new SolidColorBrush(f);
            if (shape.Width == 0) shape.Width = 40;
            if (shape.Height == 0) shape.Height = 40;
        }

        FrameworkElement current = Inner;
        Thickness? padding = null;
        Windows.UI.Color? background = null;
        Microsoft.UI.Xaml.Media.Brush? backgroundBrush = null;   // F5: gradient fill takes precedence over the flat color
        Windows.UI.Color? borderColor = null;
        double borderWidth = 1, corner = 0;
        var transforms = new Microsoft.UI.Xaml.Media.TransformGroup();  // F4: scale/offset/rotation compose here
        string? transformOrigin = null;
        (double radius, double dx, double dy, Windows.UI.Color color)? shadow = null;

        foreach (var m in Modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    padding = new Thickness(N(m, "leading"), N(m, "top"), N(m, "trailing"), N(m, "bottom"));
                    break;
                case "background":
                    if (m.GetValueOrDefault("gradient") is string grad) backgroundBrush = WinStyle.Gradient(grad);
                    else background = WinStyle.Color(m.GetValueOrDefault("value") as string);
                    break;
                case "material":
                    // F6: a translucent tint fallback (a full Acrylic brush is a follow-up).
                    var mtint = (m.GetValueOrDefault("value") as string) switch
                    { "ultraThin" => 0.55, "thin" => 0.65, "thick" => 0.85, _ => 0.75 };
                    var mdark = (m.GetValueOrDefault("dark") as string) == "true";
                    var mbase = mdark ? Windows.UI.Color.FromArgb(255, 20, 20, 22) : Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    mbase.A = (byte)(mtint * 255);
                    backgroundBrush = new SolidColorBrush(mbase);
                    break;
                case "cornerRadius":
                    corner = N(m, "radius");
                    break;
                case "border":
                    borderColor = WinStyle.Color(m.GetValueOrDefault("color") as string);
                    borderWidth = N(m, "width", 1);
                    if (N(m, "cornerRadius") > 0) corner = N(m, "cornerRadius");
                    break;
                case "shadow":
                    // Wire shape mirrors Web/Skia: radius + x/y offset + an optional color token. The default
                    // matches Web's box-shadow fallback (black @ 35%).
                    shadow = (N(m, "radius", 4), N(m, "x"), N(m, "y"),
                        WinStyle.Color(m.GetValueOrDefault("color") as string) ?? Windows.UI.Color.FromArgb(0x59, 0, 0, 0));
                    break;
                case "opacity":
                    Inner.Opacity = N(m, "amount", 1);
                    break;
                case "disabled":
                    var disabled = (m.GetValueOrDefault("value") as string) == "true";
                    if (Inner is Control dc) dc.IsEnabled = !disabled;   // native greyed-out + disabled state
                    else { current.IsHitTestVisible = !disabled; if (disabled) Inner.Opacity = 0.5; }
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
                    // WinUI Phase 1: implicit theme transitions animate this element's layout repositioning
                    // when it moves/resizes. Per-property (opacity/scale) animation via the Composition API
                    // is a follow-up; spring/curve/duration are honored there, not by theme transitions.
                    current.Transitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection
                    {
                        new Microsoft.UI.Xaml.Media.Animation.RepositionThemeTransition(),
                    };
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
                        if (N(m, "amount", 1) >= 2)
                            current.DoubleTapped += (_, _) => _bridge.Emit(ev, null);
                        else
                            current.Tapped += (_, _) => _bridge.Emit(ev, null);
                    }
                    break;
                case "onLongPress":
                    if (m.GetValueOrDefault("event") is string lev)
                    {
                        // Holding fires for touch/pen; RightTapped is the mouse/trackpad equivalent of a press-hold.
                        current.Holding += (_, e) => { if (e.HoldingState == Microsoft.UI.Input.HoldingState.Started) _bridge.Emit(lev, null); };
                        current.RightTapped += (_, _) => _bridge.Emit(lev, null);
                    }
                    break;
                case "onSwipe":
                    if (m.GetValueOrDefault("event") is string sev)
                    {
                        var dir = m.GetValueOrDefault("value") as string;
                        // Translate* (not the default System) is required for ManipulationCompleted to raise.
                        current.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX
                                                 | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY;
                        current.ManipulationCompleted += (_, e) =>
                        {
                            var dx = e.Cumulative.Translation.X;
                            var dy = e.Cumulative.Translation.Y;
                            var matched = Math.Abs(dx) > Math.Abs(dy)
                                ? (dx < 0 ? dir == "left" : dir == "right")
                                : (dy < 0 ? dir == "up" : dir == "down");
                            if (matched && (Math.Abs(dx) > 40 || Math.Abs(dy) > 40)) _bridge.Emit(sev, null);
                        };
                    }
                    break;
                case "onDrag":
                    // F1 continuous drag → "<phase>;tx,ty;lx,ly;vx,vy" via manipulation events.
                    if (m.GetValueOrDefault("event") is string dev)
                    {
                        current.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX
                                                 | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY;
                        current.ManipulationStarted += (_, e) =>
                            _bridge.Emit(dev, System.FormattableString.Invariant($"b;0,0;{e.Position.X},{e.Position.Y};0,0"));
                        current.ManipulationDelta += (_, e) =>
                            _bridge.Emit(dev, System.FormattableString.Invariant($"c;{e.Cumulative.Translation.X},{e.Cumulative.Translation.Y};{e.Position.X},{e.Position.Y};0,0"));
                        current.ManipulationCompleted += (_, e) =>
                            _bridge.Emit(dev, System.FormattableString.Invariant($"e;{e.Cumulative.Translation.X},{e.Cumulative.Translation.Y};{e.Position.X},{e.Position.Y};{e.Velocities.Linear.X},{e.Velocities.Linear.Y}"));
                    }
                    break;
                case "onMagnify":
                    // F1 pinch → cumulative scale factor via the Scale manipulation.
                    if (m.GetValueOrDefault("event") is string mev)
                    {
                        current.ManipulationMode |= Microsoft.UI.Xaml.Input.ManipulationModes.Scale;
                        current.ManipulationDelta += (_, e) =>
                            _bridge.Emit(mev, e.Cumulative.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    break;
                case "foregroundColor":
                    if (Inner is TextBlock tbf && WinStyle.Brush(m.GetValueOrDefault("value") as string) is { } fg) tbf.Foreground = fg;
                    break;
                case "font":
                    if (Inner is TextBlock tbn && WinStyle.Font(m.GetValueOrDefault("value") as string) is { } font)
                    { tbn.FontSize = font.size; tbn.FontWeight = font.weight; }
                    break;
            }
        }

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
            if (corner > 0) wrapper.CornerRadius = new CornerRadius(corner);
            wrapper.HorizontalAlignment = Inner.HorizontalAlignment;
            Element = wrapper;
        }
        else
        {
            Element = Inner;
        }

        // The shadow wraps whatever the modifier chain produced (so it hugs the padded/bordered box).
        if (shadow is { } sh) Element = WithShadow(Element, sh.radius, sh.dx, sh.dy, sh.color);
    }

    /// <summary>Casts a drop shadow behind <paramref name="content"/>.
    /// <para>XAML's <c>ThemeShadow</c> (element <c>Shadow</c> + a <c>Translation</c> Z offset) is the simpler
    /// recipe but exposes no radius/color/offset knobs — it derives everything from the Z depth — so it cannot
    /// honor the wire props. The Composition <c>DropShadow</c> takes BlurRadius / Offset / Color directly, so
    /// that is what is used: an empty, hit-test-invisible Border sits behind the content in a wrapper Grid and
    /// hosts a SpriteVisual whose only paint is the shadow.</para>
    /// <para>Degradation: with no alpha mask the silhouette is the content's bounding rectangle, so a rounded
    /// or non-rectangular element still casts a square shadow (Web/Skia follow the corner radius).</para></summary>
    static FrameworkElement WithShadow(FrameworkElement content, double radius, double dx, double dy, Windows.UI.Color color)
    {
        var host = new Border
        {
            IsHitTestVisible = false,
            HorizontalAlignment = content.HorizontalAlignment,
            VerticalAlignment = content.VerticalAlignment,
        };
        var wrapper = new WinGrid
        {
            HorizontalAlignment = content.HorizontalAlignment,
            VerticalAlignment = content.VerticalAlignment,
        };
        wrapper.Children.Add(host);       // behind
        wrapper.Children.Add(content);    // in front

        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(host).Compositor;
        var drop = compositor.CreateDropShadow();
        drop.BlurRadius = (float)radius;
        drop.Offset = new System.Numerics.Vector3((float)dx, (float)dy, 0f);
        drop.Color = color;
        var sprite = compositor.CreateSpriteVisual();
        sprite.Shadow = drop;
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(host, sprite);

        // The sprite has no layout of its own: keep it (and the host) matched to the measured content.
        void Resize(double w, double h)
        {
            host.Width = w;
            host.Height = h;
            sprite.Size = new System.Numerics.Vector2((float)w, (float)h);
        }
        content.SizeChanged += (_, e) => Resize(e.NewSize.Width, e.NewSize.Height);
        if (content.ActualWidth > 0) Resize(content.ActualWidth, content.ActualHeight);
        return wrapper;
    }

    Windows.UI.Color? ModColor(string type)
        => WinStyle.Color(Modifiers.FirstOrDefault(m => m["type"] as string == type)?.GetValueOrDefault("value") as string);

    // ---- patch application ---------------------------------------------------

    public void UpdateProps(JsonElement props, JsonElement modifiers)
    {
        Props = ReadDict(props);
        Modifiers = ReadDictArray(modifiers);

        switch (Type)
        {
            case "Text": ((TextBlock)Inner).Text = Str("text"); break;
            case "ZStack": ApplyZStackAlignment((WinGrid)Inner); break;
            case "Button": ((WinButton)Inner).Content = Str("title"); break;
            case "TextField": SyncText((TextBox)Inner, Str("text")); break;
            case "Toggle": if (((ToggleSwitch)Inner).IsOn != Bool("value")) ((ToggleSwitch)Inner).IsOn = Bool("value"); break;
            case "Slider": SyncSlider(); break;
            case "DisclosureGroup": ((Expander)Inner).IsExpanded = Bool("expanded"); break;
            case "TabView": SyncTabView(); break;
            case "Sheet": SyncDialog(ref _sheet, sheet: true); break;
            case "Alert": SyncDialog(ref _alert, sheet: false); break;
            default: _customRenderer?.Update(Inner, RenderCtx()); break;
        }
    }

    static void SyncText(TextBox tb, string value) { if (tb.Text != value) tb.Text = value; }
    void SyncSlider() { var s = (WinSlider)Inner; if (Math.Abs(s.Value - (Num("value") ?? 0)) > 0.0001) s.Value = Num("value") ?? 0; }

    ContentDialog? _sheet;
    ContentDialog? _alert;

    void SyncDialog(ref ContentDialog? dialog, bool sheet)
    {
        if (Bool("presented"))
        {
            if (dialog is not null) return;
            dialog = new ContentDialog
            {
                XamlRoot = _bridge.Host.XamlRoot,
                Title = sheet ? "" : Str("title"),
                Content = sheet && Children.Count > 1 ? Children[1].Element : (object)Str("message"),
                PrimaryButtonText = sheet ? "Close" : "OK",
            };
            var captured = dialog;
            dialog.PrimaryButtonClick += (_, _) => _bridge.Emit(Id, "false");
            dialog.Closed += (_, _) => _bridge.Emit(Id, "false");
            _ = captured.ShowAsync();
        }
        else if (dialog is not null)
        {
            dialog.Hide();
            dialog = null;
        }
    }

    public void SetChildren(JsonElement children)
    {
        // Reconcile the child WinNode list, reusing elements by key. setChildren only fires on a keyed
        // key-sequence change (insert/remove/move), where surviving rows keep identical content — so
        // reusing a matched row's already-built element both preserves its control state and IS the
        // recycling. Non-keyed containers rebuild.
        ReconcileChildren(children);

        if (_childHost is { } host)      // chrome-wrapped container (List): re-lay rows into its panel
        {
            LayoutListRows(host);
            return;
        }
        if (Inner is Panel panel)        // direct panel container (Stack/Form/Section/Group/ZStack)
        {
            panel.Children.Clear();
            foreach (var child in Children) panel.Children.Add(child.Element);
            if (panel is WinGrid zgrid && Type == "ZStack") ApplyZStackAlignment(zgrid);  // re-laid children lose it
        }
    }

    void ReconcileChildren(JsonElement children)
    {
        var keyed = Props.GetValueOrDefault("keyed") as bool? == true;
        var byKey = new Dictionary<string, WinNode>();
        if (keyed)
            foreach (var c in Children)
                if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<WinNode>();
        foreach (var el in children.EnumerateArray())
        {
            var key = keyed && el.TryGetProperty("props", out var p) && p.TryGetProperty("key", out var kp)
                ? kp.GetString() : null;
            next.Add(key is not null && byKey.TryGetValue(key, out var reuse) && reuse.Type == el.GetProperty("type").GetString()
                ? reuse
                : Build(el, _bridge));
        }
        Children.Clear();
        Children.AddRange(next);
    }

    // ---- helpers -------------------------------------------------------------

    static WinHorizontalAlignment AlignH(string? t) => t switch
    {
        "leading" or "topLeading" or "bottomLeading" => WinHorizontalAlignment.Left,
        "trailing" or "topTrailing" or "bottomTrailing" => WinHorizontalAlignment.Right,
        _ => WinHorizontalAlignment.Center,
    };

    static WinVerticalAlignment AlignV(string? t) => t switch
    {
        "top" or "topLeading" or "topTrailing" => WinVerticalAlignment.Top,
        "bottom" or "bottomLeading" or "bottomTrailing" => WinVerticalAlignment.Bottom,
        _ => WinVerticalAlignment.Center,
    };

    string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
    static double? Num(Dictionary<string, object?> m, string key) => m.TryGetValue(key, out var v) && v is double d ? d : null;
    static double N(Dictionary<string, object?> m, string key, double fallback = 0) => m.TryGetValue(key, out var v) && v is double d ? d : fallback;

    static Windows.Foundation.Point OriginPoint(string? t)
    {
        double fx = t is "leading" or "topLeading" or "bottomLeading" ? 0
                  : t is "trailing" or "topTrailing" or "bottomTrailing" ? 1 : 0.5;
        double fy = t is "top" or "topLeading" or "topTrailing" ? 0
                  : t is "bottom" or "bottomLeading" or "bottomTrailing" ? 1 : 0.5;
        return new Windows.Foundation.Point(fx, fy);
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

/// <summary>Lightweight WinUI navigation stack (header + content) for NavigationStack/Link.</summary>
sealed class WinNavController
{
    ContentControl _content = null!;
    TextBlock _title = null!;
    WinButton _back = null!;
    readonly List<(FrameworkElement element, string title)> _stack = new();

    public FrameworkElement Build(FrameworkElement root)
    {
        var grid = new WinGrid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(8) };
        _back = new WinButton { Content = "‹ Back", Visibility = Visibility.Collapsed };
        _back.Click += (_, _) => Pop();
        _title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = WinVerticalAlignment.Center };
        header.Children.Add(_back);
        header.Children.Add(_title);
        WinGrid.SetRow(header, 0);
        grid.Children.Add(header);

        _content = new ContentControl { Content = root, HorizontalContentAlignment = WinHorizontalAlignment.Stretch, VerticalContentAlignment = WinVerticalAlignment.Stretch };
        WinGrid.SetRow(_content, 1);
        grid.Children.Add(_content);

        _stack.Add((root, ""));
        return grid;
    }

    public void Push(FrameworkElement destination, string title)
    {
        _content.Content = destination;
        _stack.Add((destination, title));
        _title.Text = title;
        _back.Visibility = Visibility.Visible;
    }

    void Pop()
    {
        if (_stack.Count <= 1) return;
        _stack.RemoveAt(_stack.Count - 1);
        var prev = _stack[^1];
        _content.Content = prev.element;
        _title.Text = prev.title;
        _back.Visibility = _stack.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
}
