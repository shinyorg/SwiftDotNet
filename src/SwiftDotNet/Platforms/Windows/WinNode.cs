using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

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
        "Spacer" => new Border { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch },
        "Divider" => new Border { Height = 1, Background = WinStyle.Brush("secondary"), HorizontalAlignment = HorizontalAlignment.Stretch },
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
        "Image" => Text(WinStyle.Emoji(Str("system"))),
        "Label" => MakeLabel(),
        "ProgressView" => MakeProgress(),
        "Gauge" => MakeGauge(),
        "Link" => new HyperlinkButton { Content = Str("title"), NavigateUri = Uri.TryCreate(Str("url"), UriKind.Absolute, out var u) ? u : null },
        "Rectangle" => new Rectangle(),
        "Circle" => new Ellipse(),
        "Capsule" => new Rectangle { RadiusX = 999, RadiusY = 999 },
        "RoundedRectangle" => new Rectangle { RadiusX = Num("cornerRadius") ?? 8, RadiusY = Num("cornerRadius") ?? 8 },
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

    FrameworkElement MakeButton()
    {
        var b = new Button { Content = Str("title"), HorizontalAlignment = HorizontalAlignment.Center };
        b.Click += (_, _) => _bridge.Emit(Id, null);
        return b;
    }

    StackPanel Stack(Orientation orientation)
    {
        var panel = new StackPanel { Orientation = orientation, Spacing = Num("spacing") ?? 0 };
        var align = Props.GetValueOrDefault("alignment") as string;
        if (orientation == Orientation.Vertical)
            panel.HorizontalAlignment = align is null ? HorizontalAlignment.Center : AlignH(align);
        else
            panel.VerticalAlignment = align is null ? VerticalAlignment.Center : AlignV(align);
        foreach (var c in Children) panel.Children.Add(c.Element);
        return panel;
    }

    Grid MakeZStack()
    {
        var grid = new Grid();
        foreach (var c in Children) grid.Children.Add(c.Element);
        return grid;
    }

    ScrollViewer MakeScroll()
    {
        var inner = new StackPanel
        {
            Orientation = Str("axis") == "horizontal" ? Orientation.Horizontal : Orientation.Vertical,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var c in Children) inner.Children.Add(c.Element);
        return new ScrollViewer { Content = inner };
    }

    Grid MakeGrid()
    {
        var cols = (int)(Num("columns") ?? 2);
        var sp = Num("spacing") ?? 8;
        var grid = new Grid { ColumnSpacing = sp, RowSpacing = sp };
        for (var i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < Children.Count; i++)
        {
            var row = i / cols;
            if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var el = Children[i].Element;
            Grid.SetColumn(el, i % cols);
            Grid.SetRow(el, row);
            grid.Children.Add(el);
        }
        return grid;
    }

    Border MakeList()
    {
        var panel = new StackPanel();
        for (var i = 0; i < Children.Count; i++)
        {
            var row = new Border { Padding = new Thickness(16, 12, 16, 12), Child = Children[i].Element };
            panel.Children.Add(row);
            if (i < Children.Count - 1)
                panel.Children.Add(new Border { Height = 1, Background = WinStyle.Brush("secondary") });
        }
        return new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), BorderBrush = WinStyle.Brush("secondary"), Child = panel };
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
        var expander = new Expander { Header = Str("label"), Content = content, IsExpanded = Bool("expanded"), HorizontalAlignment = HorizontalAlignment.Stretch };
        expander.Expanding += (_, _) => _bridge.Emit(Id, "true");
        expander.Collapsed += (_, _) => _bridge.Emit(Id, "false");
        return expander;
    }

    TabView MakeTabView()
    {
        var tabs = new TabView { IsAddTabButtonVisible = false, CanReorderTabs = false, CanDragTabs = false };
        foreach (var tab in Children)
            tabs.TabItems.Add(new TabViewItem { Header = tab.Str("title"), Content = tab.Element, IsClosable = false });
        return tabs;
    }

    Button MakeMenu()
    {
        var flyout = new MenuFlyout();
        foreach (var c in Children)
        {
            var item = new MenuFlyoutItem { Text = c.Str("title") };
            var childId = c.Id;
            item.Click += (_, _) => _bridge.Emit(childId, null);
            flyout.Items.Add(item);
        }
        return new Button { Content = Str("label"), Flyout = flyout };
    }

    // ---- controls (two-way bound) -------------------------------------------

    TextBox MakeTextField()
    {
        var tb = new TextBox { PlaceholderText = Str("placeholder"), Text = Str("text"), HorizontalAlignment = HorizontalAlignment.Stretch };
        tb.TextChanged += (_, _) => _bridge.Emit(Id, tb.Text);
        return tb;
    }

    PasswordBox MakeSecureField()
    {
        var pb = new PasswordBox { PlaceholderText = Str("placeholder"), Password = Str("text"), HorizontalAlignment = HorizontalAlignment.Stretch };
        pb.PasswordChanged += (_, _) => _bridge.Emit(Id, pb.Password);
        return pb;
    }

    TextBox MakeTextEditor()
    {
        var tb = new TextBox { Text = Str("text"), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, HorizontalAlignment = HorizontalAlignment.Stretch };
        tb.TextChanged += (_, _) => _bridge.Emit(Id, tb.Text);
        return tb;
    }

    ToggleSwitch MakeToggle()
    {
        var ts = new ToggleSwitch { Header = Str("label"), IsOn = Bool("value") };
        ts.Toggled += (_, _) => _bridge.Emit(Id, ts.IsOn ? "true" : "false");
        return ts;
    }

    Slider MakeSlider()
    {
        var slider = new Slider { Minimum = Num("min") ?? 0, Maximum = Num("max") ?? 1, StepFrequency = 0.01, Value = Num("value") ?? 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        slider.ValueChanged += (_, e) => _bridge.Emit(Id, e.NewValue.ToString(CultureInfo.InvariantCulture));
        return slider;
    }

    StackPanel MakeStepper()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = VerticalAlignment.Center });
        var box = new NumberBox { Minimum = Num("min") ?? -1e9, Maximum = Num("max") ?? 1e9, SmallChange = 1, Value = Num("value") ?? 0, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
        box.ValueChanged += (_, _) => _bridge.Emit(Id, ((int)box.Value).ToString(CultureInfo.InvariantCulture));
        panel.Children.Add(box);
        return panel;
    }

    StackPanel MakePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = VerticalAlignment.Center });
        var combo = new ComboBox { ItemsSource = Children.Select(c => c.Str("text")).ToList(), SelectedIndex = (int)(Num("selection") ?? 0) };
        combo.SelectionChanged += (_, _) => _bridge.Emit(Id, combo.SelectedIndex.ToString(CultureInfo.InvariantCulture));
        panel.Children.Add(combo);
        return panel;
    }

    StackPanel MakeDatePicker()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = VerticalAlignment.Center });
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
        panel.Children.Add(new TextBlock { Text = Str("label"), VerticalAlignment = VerticalAlignment.Center });

        var colorPicker = new ColorPicker { IsMoreButtonVisible = false, IsColorSliderVisible = true, IsColorChannelTextInputVisible = false };
        if (WinStyle.Color(Str("value")) is { } c) colorPicker.Color = c;
        colorPicker.ColorChanged += (_, e) =>
            _bridge.Emit(Id, $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}");

        var swatch = new Border { Width = 32, Height = 24, CornerRadius = new CornerRadius(4), Background = WinStyle.Brush(Str("value")) };
        var flyout = new Flyout { Content = colorPicker };
        var button = new Button { Content = swatch, Flyout = flyout, Padding = new Thickness(2) };
        panel.Children.Add(button);
        return panel;
    }

    // ---- navigation ----------------------------------------------------------

    Button MakeNavLink()
    {
        var nav = _bridge.NavStack.Count > 0 ? _bridge.NavStack.Peek() : null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (Children.Count > 0) row.Children.Add(Children[0].Element);
        row.Children.Add(new TextBlock { Text = "›", HorizontalAlignment = HorizontalAlignment.Right });
        var button = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch };
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

    StackPanel MakeProgress()
    {
        var panel = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
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
        panel.Children.Add(new ProgressBar { Minimum = Num("min") ?? 0, Maximum = Num("max") ?? 1, Value = Num("value") ?? 0, HorizontalAlignment = HorizontalAlignment.Stretch });
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
        Windows.UI.Color? borderColor = null;
        double borderWidth = 1, corner = 0;

        foreach (var m in Modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    padding = new Thickness(N(m, "leading"), N(m, "top"), N(m, "trailing"), N(m, "bottom"));
                    break;
                case "background":
                    background = WinStyle.Color(m.GetValueOrDefault("value") as string);
                    break;
                case "cornerRadius":
                    corner = N(m, "radius");
                    break;
                case "border":
                    borderColor = WinStyle.Color(m.GetValueOrDefault("color") as string);
                    borderWidth = N(m, "width", 1);
                    if (N(m, "cornerRadius") > 0) corner = N(m, "cornerRadius");
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
                    Inner.RenderTransform = new ScaleTransform { ScaleX = N(m, "x", 1), ScaleY = N(m, "y", 1) };
                    Inner.RenderTransformOrigin = OriginPoint(m.GetValueOrDefault("value") as string);
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
                case "foregroundColor":
                    if (Inner is TextBlock tbf && WinStyle.Brush(m.GetValueOrDefault("value") as string) is { } fg) tbf.Foreground = fg;
                    break;
                case "font":
                    if (Inner is TextBlock tbn && WinStyle.Font(m.GetValueOrDefault("value") as string) is { } font)
                    { tbn.FontSize = font.size; tbn.FontWeight = font.weight; }
                    break;
            }
        }

        if (padding is not null || background is not null || borderColor is not null || corner > 0)
        {
            var wrapper = new Border { Child = Inner };
            if (padding is { } p) wrapper.Padding = p;
            if (background is { } bg) wrapper.Background = new SolidColorBrush(bg);
            if (borderColor is { } bc) { wrapper.BorderBrush = new SolidColorBrush(bc); wrapper.BorderThickness = new Thickness(borderWidth); }
            if (corner > 0) wrapper.CornerRadius = new CornerRadius(corner);
            wrapper.HorizontalAlignment = Inner.HorizontalAlignment;
            Element = wrapper;
        }
        else
        {
            Element = Inner;
        }
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
            case "Button": ((Button)Inner).Content = Str("title"); break;
            case "TextField": SyncText((TextBox)Inner, Str("text")); break;
            case "Toggle": if (((ToggleSwitch)Inner).IsOn != Bool("value")) ((ToggleSwitch)Inner).IsOn = Bool("value"); break;
            case "Slider": SyncSlider(); break;
            case "DisclosureGroup": ((Expander)Inner).IsExpanded = Bool("expanded"); break;
            case "Sheet": SyncDialog(ref _sheet, sheet: true); break;
            case "Alert": SyncDialog(ref _alert, sheet: false); break;
            default: _customRenderer?.Update(Inner, RenderCtx()); break;
        }
    }

    static void SyncText(TextBox tb, string value) { if (tb.Text != value) tb.Text = value; }
    void SyncSlider() { var s = (Slider)Inner; if (Math.Abs(s.Value - (Num("value") ?? 0)) > 0.0001) s.Value = Num("value") ?? 0; }

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
        if (Inner is not Panel panel) return;
        panel.Children.Clear();
        Children.Clear();
        foreach (var childElement in children.EnumerateArray())
        {
            var child = Build(childElement, _bridge);
            Children.Add(child);
            panel.Children.Add(child.Element);
        }
    }

    // ---- helpers -------------------------------------------------------------

    static HorizontalAlignment AlignH(string? t) => t switch
    {
        "leading" or "topLeading" or "bottomLeading" => HorizontalAlignment.Left,
        "trailing" or "topTrailing" or "bottomTrailing" => HorizontalAlignment.Right,
        _ => HorizontalAlignment.Center,
    };

    static VerticalAlignment AlignV(string? t) => t switch
    {
        "top" or "topLeading" or "topTrailing" => VerticalAlignment.Top,
        "bottom" or "bottomLeading" or "bottomTrailing" => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
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
    Button _back = null!;
    readonly List<(FrameworkElement element, string title)> _stack = new();

    public FrameworkElement Build(FrameworkElement root)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(8) };
        _back = new Button { Content = "‹ Back", Visibility = Visibility.Collapsed };
        _back.Click += (_, _) => Pop();
        _title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        header.Children.Add(_back);
        header.Children.Add(_title);
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _content = new ContentControl { Content = root, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(_content, 1);
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
