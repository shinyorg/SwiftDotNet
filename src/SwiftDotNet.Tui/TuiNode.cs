using System.Globalization;
using System.Text.Json;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Geometry;
using XenoAtom.Terminal.UI.Input;
using XenoAtom.Terminal.UI.Styling;

// SwiftDotNet's DSL and Terminal.UI use the *same names* for their view/control vocabularies — VStack,
// Button, Grid, Slider, Link, Color, Style, Rectangle. Inside `namespace SwiftDotNet` the DSL types win,
// so every terminal control this interpreter constructs is aliased with a T-prefix. It reads as noise
// until you remember what this file is: the one place the two vocabularies have to be told apart.
using TStyle = XenoAtom.Terminal.UI.Style;
using TColor = XenoAtom.Terminal.UI.Color;
using TButton = XenoAtom.Terminal.UI.Controls.Button;
using TVStack = XenoAtom.Terminal.UI.Controls.VStack;
using THStack = XenoAtom.Terminal.UI.Controls.HStack;
using TZStack = XenoAtom.Terminal.UI.Controls.ZStack;
using TGrid = XenoAtom.Terminal.UI.Controls.Grid;
using TGroup = XenoAtom.Terminal.UI.Controls.Group;
using TSlider = XenoAtom.Terminal.UI.Controls.Slider<double>;
using TColorPicker = XenoAtom.Terminal.UI.Controls.ColorPicker;
using TLink = XenoAtom.Terminal.UI.Controls.Link;

namespace SwiftDotNet;

/// <summary>A node in the retained terminal visual tree — mirrors the wire node and holds its live visual.</summary>
sealed class TuiNode
{
    /// <summary>The node's structural path. Re-stamped by <see cref="Adopt"/> when a recycled row moves.</summary>
    public required string Id { get; set; }
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<TuiNode> Children { get; } = new();

    /// <summary>
    /// What this node contributes to its parent: always the <see cref="TuiSurface"/> wrapper, never the
    /// control itself. Keeping the wrapper unconditional is what lets a <c>.Background()</c> or
    /// <c>.Border()</c> that only appears on a later render take effect — the wire reports that as an
    /// <c>updateProps</c>, so there is no chance to restructure the tree at that point.
    /// </summary>
    public Visual Visual => _surface;

    readonly TuiSurface _surface = new();

    /// <summary>The visual <see cref="CreateVisual"/> produced. Prop sync and child hosting target this.</summary>
    Visual _content = null!;

    /// <summary>The control this node built, without its surface wrapper — see <see cref="TuiBridge.FindControl"/>.</summary>
    public Visual Content => _content;

    TuiBridge _bridge = null!;
    ITuiRenderer? _customRenderer;

    /// <summary>The panel that directly holds child visuals, when this node hosts children.</summary>
    Panel? _childHost;

    TuiNavController? _nav;          // NavigationStack only
    Dialog? _sheet;                  // Sheet only
    Dialog? _alert;                  // Alert only
    Popup? _menu;                    // Menu only
    TabControl? _tabs;               // TabView only

    /// <summary>Last value pushed to / read from a two-way control, to break the echo loop.</summary>
    string? _lastValue;

    public static TuiNode Build(JsonElement e, TuiBridge bridge)
    {
        var node = new TuiNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        node._bridge = bridge;

        // NavigationStack must register its controller BEFORE building children so NavigationLinks
        // inside can capture it.
        if (node.Type == "NavigationStack")
        {
            node._nav = new TuiNavController();
            bridge.NavStack.Push(node._nav);
        }

        foreach (var child in e.GetProperty("children").EnumerateArray())
            node.Children.Add(Build(child, bridge));

        if (node.Type == "NavigationStack") bridge.NavStack.Pop();

        node._content = node.CreateVisual();
        node._surface.Child = node._content;

        // The surface, not the control, is what the parent lays out — so it has to inherit the control's
        // own sizing intent. Without this a container that asked to stretch (a Form, a Section, a List)
        // gets shrink-wrapped by its wrapper, and every row inside it is measured against the narrowest
        // sibling instead of the full width.
        node._defaultHorizontal = node._content.HorizontalAlignment;
        node._defaultVertical = node._content.VerticalAlignment;

        node.ApplyModifiers();
        return node;
    }

    Align _defaultHorizontal = Align.Start;
    Align _defaultVertical = Align.Start;

    // ---- visual construction -------------------------------------------------

    Visual CreateVisual() => Type switch
    {
        "Text" => Text(Str("text")),
        "Button" => MakeButton(),
        "Spacer" => new TuiSpacer(),
        "Divider" => new Rule(),
        "VStack" => Stack(vertical: true),
        "HStack" => Stack(vertical: false),
        "ZStack" => MakeZStack(),
        "ScrollView" => MakeScroll(),
        "Grid" => MakeGrid(),
        "List" => MakeList(),
        "Form" => MakeForm(),
        "Section" => MakeSection(),
        "Group" => Stack(vertical: true),
        "DisclosureGroup" => MakeDisclosure(),
        "TabView" => MakeTabView(),
        "Tab" => Children.Count > 0 ? Children[0].Visual : Text(""),
        "Menu" => MakeMenu(),
        "TextField" => MakeEntry(secure: false),
        "SecureField" => MakeEntry(secure: true),
        "TextEditor" => MakeTextEditor(),
        "Toggle" => MakeToggle(),
        "Slider" => MakeSlider(),
        "Stepper" => MakeStepper(),
        "Picker" => MakePicker(),
        "DatePicker" => MakeDatePicker(),
        "ColorPicker" => MakeColorPicker(),
        "NavigationStack" => _nav!.Build(Children.Count > 0 ? Children[0].Visual : Text("")),
        "NavigationLink" => MakeNavLink(),
        "Sheet" => Children.Count > 0 ? Children[0].Visual : Text(""),
        "Alert" => Children.Count > 0 ? Children[0].Visual : Text(""),
        "WebView" => MakeWebView(),
        "Image" => TuiImage.Create(this, _bridge),
        "Label" => MakeLabel(),
        "ProgressView" => MakeProgress(),
        "Gauge" => MakeGauge(),
        "Link" => new TLink(Str("url"), Str("title")),
        "Rectangle" => new TuiShape(),
        "Circle" => new TuiShape { IsEllipse = true },
        "Capsule" => new TuiShape { CornerRadius = 99 },
        "RoundedRectangle" => new TuiShape { CornerRadius = TuiStyle.Cols(Num("cornerRadius") ?? 8) },
        _ => CustomOrPlaceholder(),
    };

    Visual CustomOrPlaceholder()
    {
        _customRenderer = TuiRenderers.Get(Type);
        return _customRenderer is { } r ? r.Create(RenderCtx()) : Text($"⚠ {Type}");
    }

    TuiRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    static TextBlock Text(string text) => new(text) { Wrap = true };

    Visual MakeWebView()
    {
        // A terminal cannot embed a browser engine, so — exactly like the GTK backend without WebKitGTK —
        // surface an OSC-8 hyperlink to the content instead. Modern terminals make it clickable.
        var url = Str("url");
        return url.Length == 0
            ? Text("🌐 Web content (not embeddable in a terminal)")
            : new TLink(url, "Open web page ↗");
    }

    Visual MakeButton()
    {
        var button = new TButton(Text(Str("title")));
        button.Click(() => _bridge.Emit(Id, null));
        return button;
    }

    Visual Stack(bool vertical)
    {
        var spacing = (int)Math.Round(TuiStyle.Num(Props!, "spacing"));
        var cross = Props.GetValueOrDefault("alignment") as string;

        // SwiftUI's rule: a stack hugs its content *unless* it holds a Spacer, in which case it becomes
        // greedy on its layout axis. Terminal.UI resolves size by alignment before flex gets a say, so a
        // shrink-wrapped stack would give a Spacer nothing to absorb — "left" and "right" would end up
        // adjacent instead of pushed to the two ends. Detecting the Spacer here is what restores the rule.
        var greedy = Children.Any(c => c.Type == "Spacer");

        Panel panel;
        if (vertical)
        {
            var v = new TVStack { Spacing = TuiStyle.Rows(spacing) };
            v.HorizontalAlignment = cross is null ? Align.Center : TuiStyle.AlignOf(cross);
            if (greedy) v.VerticalAlignment = Align.Stretch;
            panel = v;
        }
        else
        {
            var h = new THStack { Spacing = TuiStyle.Cols(spacing) };
            h.VerticalAlignment = cross is null ? Align.Center : TuiStyle.VAlignOf(cross);
            if (greedy) h.HorizontalAlignment = Align.Stretch;
            panel = h;
        }
        _childHost = panel;
        foreach (var c in Children) panel.Children.Add(c.Visual);
        return panel;
    }

    Visual MakeZStack()
    {
        var z = new TZStack();
        _childHost = z;
        foreach (var c in Children) z.Children.Add(c.Visual);
        SyncZStackAlignment();
        return z;
    }

    /// <summary>ZStack's <c>alignment</c> prop → per-child alignment, constraining only the named axes.
    /// This is what lands the Controls library's overlays (Toast, Dialog, FloatingPanel) at the bottom or
    /// top edge instead of centring them.</summary>
    void SyncZStackAlignment()
    {
        var token = Props.GetValueOrDefault("alignment") as string;
        foreach (var c in Children) TuiStyle.ApplyAlignment(c.Visual, token);
    }

    Visual MakeScroll()
    {
        var horizontal = Str("axis") == "horizontal";
        Panel inner = horizontal ? new THStack { Spacing = 2 } : new TVStack { Spacing = 1 };
        _childHost = inner;
        foreach (var c in Children) inner.Children.Add(c.Visual);
        return new ScrollViewer(inner, focusable: true)
        {
            HorizontalScrollEnabled = horizontal,
            VerticalScrollEnabled = !horizontal,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    Visual MakeGrid()
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var gap = TuiStyle.Cols(Num("spacing") ?? 8);
        var grid = new TGrid { ColumnGap = gap, RowGap = Math.Max(0, gap / 2), AutoGrowRows = true };
        for (var i = 0; i < cols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star(1) });
        for (var i = 0; i < Children.Count; i++)
            grid.Cells.Add(new GridCell(Children[i].Visual) { Row = i / cols, Column = i % cols });
        return grid;
    }

    Visual MakeList()
    {
        // Grid and horizontal lists lay out differently but reconcile the same way; the default vertical
        // list is a VStack inside a ScrollViewer, which is what makes keyed row recycling in SetChildren
        // a simple re-append rather than a rebuild.
        if (Str("layout") == "grid")
        {
            var cols = Math.Max(1, (int)(Num("columns") ?? 2));
            var grid = new TGrid { ColumnGap = 1, RowGap = 0, AutoGrowRows = true };
            for (var i = 0; i < cols; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star(1) });
            for (var i = 0; i < Children.Count; i++)
                grid.Cells.Add(new GridCell(Children[i].Visual) { Row = i / cols, Column = i % cols });
            return grid;
        }

        var horizontal = Str("axis") == "horizontal";
        Panel rows = horizontal ? new THStack { Spacing = 2 } : new TVStack { Spacing = 0 };
        rows.HorizontalAlignment = Align.Stretch;
        _childHost = rows;
        foreach (var c in Children) AttachRow(c);

        return new ScrollViewer(rows, focusable: true)
        {
            HorizontalScrollEnabled = horizontal,
            VerticalScrollEnabled = !horizontal,
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    /// <summary>
    /// Adds a row to the list host, wiring row activation when the list is selectable. A terminal has no
    /// list-row concept of its own, so selection is a key binding on the row itself: focus it with
    /// Tab/arrows and press Enter, which emits the row's key exactly as the GTK backend's
    /// <c>OnRowActivated</c> does.
    /// </summary>
    void AttachRow(TuiNode row)
    {
        _childHost!.Children.Add(row.Visual);
        if (Str("selectionMode").Length == 0) return;
        if (row.Props.GetValueOrDefault("key") is not string key) return;

        row.Visual.IsTabStop = true;
        row.Visual.AddKeyBinding(new KeyGesture(TerminalKey.Enter), () => _bridge.Emit(Id, key));
        row.Visual.AddKeyBinding(new KeyGesture(TerminalKey.Space), () => _bridge.Emit(Id, key));
        row.Visual.PointerPressed(() => _bridge.Emit(Id, key));
    }

    Visual MakeForm()
    {
        var box = new TVStack { Spacing = 1, HorizontalAlignment = Align.Stretch };
        _childHost = box;
        foreach (var c in Children) box.Children.Add(c.Visual);
        return new ScrollViewer(box, focusable: true)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
    }

    Visual MakeSection()
    {
        var box = new TVStack { Spacing = 0, HorizontalAlignment = Align.Stretch };
        _childHost = box;
        foreach (var c in Children) box.Children.Add(c.Visual);
        // A Section's header becomes the box caption — the terminal idiom for a titled group.
        return Props.GetValueOrDefault("header") is string header && header.Length > 0
            ? new TGroup(Text(header), box) { HorizontalAlignment = Align.Stretch }
            : box;
    }

    Visual MakeDisclosure()
    {
        var inner = new TVStack { Spacing = 0, HorizontalAlignment = Align.Stretch };
        _childHost = inner;
        foreach (var c in Children) inner.Children.Add(c.Visual);

        var collapsible = new Collapsible(Text(Str("label")), inner) { IsExpanded = Bool("expanded") };
        collapsible.ExpandedChanged(() => _bridge.Emit(Id, collapsible.IsExpanded ? "true" : "false"));
        return collapsible;
    }

    Visual MakeTabView()
    {
        _tabs = new TabControl { HorizontalAlignment = Align.Stretch, VerticalAlignment = Align.Stretch };
        foreach (var tab in Children)   // each child is a Tab node
            _tabs.AddTab(Text(tab.Str("title")), tab.Visual);
        if (Props.ContainsKey("selectedIndex"))
        {
            _tabs.SelectedIndex = (int)(Num("selectedIndex") ?? 0);
            _tabs.SelectionChanged(() => _bridge.Emit(Id, _tabs.SelectedIndex.ToString(CultureInfo.InvariantCulture)));
        }
        return _tabs;
    }

    void SyncTabView()
    {
        if (_tabs is null || !Props.ContainsKey("selectedIndex")) return;
        var idx = (int)(Num("selectedIndex") ?? 0);
        if (_tabs.SelectedIndex != idx && idx >= 0 && idx < _tabs.Tabs.Count) _tabs.SelectedIndex = idx;
    }

    Visual MakeMenu()
    {
        var items = new TVStack { Spacing = 0 };
        foreach (var c in Children)
        {
            var childId = c.Id;
            var item = new TButton(Text(c.Str("title")));
            item.Click(() => { _menu?.Close(); _bridge.Emit(childId, null); });
            items.Children.Add(item);
        }

        var trigger = new TButton(Text(Str("label").Length > 0 ? Str("label") + " ▾" : "▾"));
        _menu = new Popup(items) { Anchor = trigger, Placement = PopupPlacement.Below, CloseOnTab = true };
        trigger.Click(() => _menu.Show());
        return trigger;
    }

    // ---- controls (two-way bound) -------------------------------------------

    /// <summary>
    /// Watches a control's own value and emits it back to C# when the user — rather than a patch —
    /// changed it. Reads inside the callback are tracked by the framework, so it re-runs precisely when
    /// the value moves; <see cref="_lastValue"/> is the feedback-loop guard that stops the echo of our
    /// own <c>updateProps</c> write from being reported as a user edit.
    /// </summary>
    void WatchValue(Visual visual, Func<string?> read)
    {
        _lastValue = read() ?? "";
        visual.RegisterDynamicUpdate(_ =>
        {
            var current = read() ?? "";
            if (current == _lastValue) return;
            _lastValue = current;
            _bridge.Emit(Id, current);
        });
    }

    Visual MakeEntry(bool secure)
    {
        var entry = new TextBox(Str("text"))
        {
            IsPassword = secure,
            Placeholder = Str("placeholder"),
            HorizontalAlignment = Align.Stretch,
        };
        WatchValue(entry, () => entry.Text);
        return entry;
    }

    Visual MakeTextEditor()
    {
        var editor = new TextArea(Str("text"))
        {
            WordWrap = true,
            MinHeight = 5,
            HorizontalAlignment = Align.Stretch,
        };
        WatchValue(editor, () => editor.Text);
        return editor;
    }

    Visual MakeToggle()
    {
        // Terminal.UI's Switch carries its own content, so the label rides inside it rather than in a
        // separate row — that keeps the whole thing one focus stop, which is what a keyboard user wants.
        var toggle = new Switch(Text(Str("label"))) { IsOn = Bool("value") };
        toggle.Toggled(() => _bridge.Emit(Id, toggle.IsOn ? "true" : "false"));
        return toggle;
    }

    Visual MakeSlider()
    {
        // ORDER MATTERS, and not in the obvious way. Slider<T> clamps and step-snaps on every write to
        // Value, against whatever Minimum/Maximum/Step are at that moment — and its defaults are a 0-10
        // range with a step of 1. So the three-argument constructor is unusable here (it applies Value
        // before Maximum, turning 42 into 10), and Step must be set before Value or the DSL's usual 0..1
        // range snaps every fractional value to 0. Range, then step, then value.
        var min = Num("min") ?? 0;
        var max = Num("max") ?? 1;
        var slider = new TSlider { ShowValueLabel = true, HorizontalAlignment = Align.Stretch };
        slider.Minimum = min;
        slider.Maximum = max;
        slider.Step = (max - min) / 100;
        slider.LargeStep = (max - min) / 10;
        slider.Value = Math.Clamp(Num("value") ?? 0, min, max);
        slider.ValueChanged(() => _bridge.Emit(Id, slider.Value.ToString(CultureInfo.InvariantCulture)));
        return slider;
    }

    Visual MakeStepper()
    {
        var value = Num("value") ?? 0;
        var min = Num("min") ?? -1e9;
        var max = Num("max") ?? 1e9;
        var display = Text(value.ToString("0.##", CultureInfo.InvariantCulture));

        var minus = new TButton(Text("−"));
        var plus = new TButton(Text("+"));
        minus.Click(() => Step(-1));
        plus.Click(() => Step(+1));

        void Step(int delta)
        {
            var next = Math.Clamp((Num("value") ?? 0) + delta, min, max);
            _bridge.Emit(Id, ((int)next).ToString(CultureInfo.InvariantCulture));
        }

        _stepperDisplay = display;
        return new THStack(Text(Str("label")), new TuiSpacer(), minus, display, plus) { Spacing = 1 };
    }

    TextBlock? _stepperDisplay;

    Visual MakePicker()
    {
        var options = Children.Select(c => c.Str("text")).ToArray();
        var select = new Select<string>(options, (int)(Num("selection") ?? 0));
        select.SelectionChanged(() =>
            _bridge.Emit(Id, select.SelectedIndex.ToString(CultureInfo.InvariantCulture)));
        _picker = select;
        return new THStack(Text(Str("label")), new TuiSpacer(), select) { Spacing = 1 };
    }

    Select<string>? _picker;

    Visual MakeDatePicker()
    {
        // No calendar control exists in a terminal toolkit, so the date is edited as ISO text and
        // re-encoded to the wire's unix seconds on each valid edit. An unparseable value is ignored
        // rather than clamped, so a half-typed date doesn't jump the bound state around.
        var date = DateTimeOffset.FromUnixTimeSeconds((long)(Num("value") ?? 0)).LocalDateTime;
        var entry = new TextBox(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        {
            Placeholder = "yyyy-MM-dd",
        };
        _lastValue = entry.Text;
        entry.RegisterDynamicUpdate(_ =>
        {
            var text = entry.Text;
            if (text == _lastValue) return;
            _lastValue = text;
            if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
                _bridge.Emit(Id, new DateTimeOffset(parsed.ToUniversalTime())
                    .ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        });
        _dateEntry = entry;
        return new THStack(Text(Str("label")), new TuiSpacer(), entry) { Spacing = 1 };
    }

    TextBox? _dateEntry;

    Visual MakeColorPicker()
    {
        var picker = new TColorPicker(TuiStyle.Parse(Str("value")) ?? Colors.White) { ShowPalette = true };
        _lastValue = Str("value");
        picker.RegisterDynamicUpdate(_ =>
        {
            var hex = picker.Value.ToHexString(includeAlpha: false);
            if (hex == _lastValue) return;
            _lastValue = hex;
            _bridge.Emit(Id, hex);
        });
        _colorPicker = picker;
        return new THStack(Text(Str("label")), new TuiSpacer(), picker) { Spacing = 1 };
    }

    TColorPicker? _colorPicker;

    // ---- navigation ----------------------------------------------------------

    Visual MakeNavLink()
    {
        var nav = _bridge.NavStack.Count > 0 ? _bridge.NavStack.Peek() : null;

        // NOTE: no Spacer between the label and the chevron. Terminal.UI's Button centres its content
        // regardless of the content's own alignment, so a Spacer would collapse to nothing — the chevron
        // sits beside the label rather than pinned to the right edge. Keeping the Button is the right
        // trade: in a terminal, correct focus and keyboard activation matter far more than where the
        // chevron lands, and rolling our own focusable row would give up both.
        var row = new THStack { Spacing = 1 };
        if (Children.Count > 0) row.Children.Add(Children[0].Visual);
        row.Children.Add(Text("›"));

        var button = new TButton(row) { HorizontalAlignment = Align.Stretch };
        if (Children.Count > 1)
        {
            var destination = Children[1].Visual;
            var title = Children[1].TitleOf();
            button.Click(() => nav?.Push(destination, title));
        }
        return button;
    }

    string TitleOf() =>
        Modifiers.FirstOrDefault(m => m["type"] as string == "navigationTitle")?["value"] as string ?? "";

    // ---- display -------------------------------------------------------------

    Visual MakeLabel() => new THStack(Text(TuiStyle.Glyph(Str("systemImage"))), Text(Str("title"))) { Spacing = 1 };

    Visual MakeProgress()
    {
        // A determinate ProgressView is a bar; an indeterminate one is a Spinner, which is the only one
        // of the two that a terminal can animate without a value to drive it.
        if (Num("value") is { } value)
        {
            var bar = new ProgressBar(Math.Clamp(value, 0, 1)) { HorizontalAlignment = Align.Stretch };
            _progress = bar;
            return Props.GetValueOrDefault("label") is string label && label.Length > 0
                ? new TVStack(Text(label), bar) { Spacing = 0, HorizontalAlignment = Align.Stretch }
                : bar;
        }
        var spinner = new Spinner(Text(Str("label"))) { IsActive = true };
        return spinner;
    }

    ProgressBar? _progress;

    Visual MakeGauge()
    {
        var min = Num("min") ?? 0;
        var max = Num("max") ?? 1;
        var span = Math.Abs(max - min) < 1e-9 ? 1 : max - min;
        var bar = new ProgressBar(Math.Clamp(((Num("value") ?? 0) - min) / span, 0, 1))
        {
            HorizontalAlignment = Align.Stretch,
        };
        _progress = bar;
        _gaugeRange = (min, span);
        return Props.GetValueOrDefault("label") is string label && label.Length > 0
            ? new TVStack(Text(label), bar) { Spacing = 0, HorizontalAlignment = Align.Stretch }
            : bar;
    }

    (double min, double span)? _gaugeRange;

    // ---- modifiers -----------------------------------------------------------

    /// <summary>
    /// Folds the node's modifier list onto its surface and content. Called on build and on every
    /// <c>updateProps</c>, and written to be idempotent: everything it touches is assigned
    /// unconditionally from the current modifier list, so a modifier that disappears is undone rather
    /// than left stuck at its last value.
    /// </summary>
    void ApplyModifiers()
    {
        var padding = Thickness.Zero;
        TStyle? fill = null;
        LineGlyphs? borderGlyphs = null;
        var borderStyle = TStyle.None;
        TColorOpt foreground = default;
        var textStyle = TextStyle.None;
        var opacity = 1.0;
        var enabled = true;
        int? width = null, height = null;
        string? frameAlignment = null, alignToken = null;

        foreach (var m in Modifiers)
        {
            switch (m["type"] as string)
            {
                case "padding":
                    padding = TuiStyle.PaddingOf(m);
                    break;
                case "background":
                    var bg = m.GetValueOrDefault("gradient") is string grad
                        ? TuiStyle.GradientStart(grad)
                        : TuiStyle.Parse(m.GetValueOrDefault("value") as string);
                    if (bg is { } bgc) fill = TStyle.None.WithBackground(bgc);
                    break;
                case "material":
                    // A terminal has no backdrop blur; a material reads as a flat surface tint, the same
                    // documented degradation the GTK backend makes.
                    fill = TStyle.None.WithBackground((m.GetValueOrDefault("dark") as string) == "true"
                        ? XenoAtom.Terminal.UI.Color.Rgb(0x14, 0x14, 0x16)
                        : XenoAtom.Terminal.UI.Color.Rgb(0xF2, 0xF2, 0xF5));
                    break;
                case "border":
                    borderGlyphs = TuiStyle.BorderGlyphs(TuiStyle.Num(m, "width", 1));
                    if (TuiStyle.Parse(m.GetValueOrDefault("color") as string) is { } bc)
                        borderStyle = TStyle.None.WithForeground(bc);
                    break;
                case "foregroundColor":
                    foreground = new TColorOpt(TuiStyle.Parse(m.GetValueOrDefault("value") as string));
                    break;
                case "font":
                    textStyle |= TuiStyle.Font(m.GetValueOrDefault("value") as string);
                    break;
                case "opacity":
                    opacity = TuiStyle.Num(m, "amount", 1);
                    break;
                case "disabled":
                    enabled = (m.GetValueOrDefault("value") as string) != "true";
                    break;
                case "frame":
                    if (m.TryGetValue("width", out var w) && w is double dw) width = TuiStyle.Cols(dw);
                    if (m.TryGetValue("height", out var h) && h is double dh) height = TuiStyle.Rows(dh);
                    frameAlignment = m.GetValueOrDefault("alignment") as string;
                    break;
                case "align":
                    alignToken = m.GetValueOrDefault("value") as string;
                    break;
                case "onTapGesture":
                    if (!_tapWired && m.GetValueOrDefault("event") is string ev)
                    {
                        _tapWired = true;
                        _content.IsTabStop = true;
                        _content.PointerPressed(() => _bridge.Emit(ev, null));
                        _content.AddKeyBinding(new KeyGesture(TerminalKey.Enter), () => _bridge.Emit(ev, null));
                    }
                    break;
            }
        }

        // A border draws on the surface's own edge cells, so the content has to be inset by one to
        // avoid being painted over. Everything else is a straight assignment.
        if (borderGlyphs is not null)
            padding = new Thickness(padding.Left + 1, padding.Top + 1, padding.Right + 1, padding.Bottom + 1);

        _surface.Padding = padding;
        _surface.BorderGlyphs = borderGlyphs;
        _surface.BorderStyle = borderStyle;
        _surface.Fill = fill;
        _surface.IsEnabled = enabled;
        // Cells have no alpha, so a nearly-transparent node is hidden outright rather than drawn solid.
        _surface.IsVisible = opacity > 0.05;

        if (width is { } cols) { _surface.MinWidth = cols; _surface.MaxWidth = cols; }
        if (height is { } rows) { _surface.MinHeight = rows; _surface.MaxHeight = rows; }

        // Reset to the control's own intent first, so an alignment modifier that disappears is undone
        // rather than latched — then let the modifiers that are present override it.
        _surface.HorizontalAlignment = _defaultHorizontal;
        _surface.VerticalAlignment = _defaultVertical;
        if (frameAlignment is not null) TuiStyle.ApplyAlignment(_surface, frameAlignment);
        if (alignToken is not null) _surface.HorizontalAlignment = TuiStyle.AlignOf(alignToken);

        ApplyForeground(foreground.Value, textStyle, opacity, fill);
    }

    bool _tapWired;

    /// <summary>Wraps a nullable colour so "no foregroundColor modifier" is distinguishable from "the
    /// modifier resolved to the theme default" — both are null, but only the second should clear a
    /// previously-set colour.</summary>
    readonly record struct TColorOpt(XenoAtom.Terminal.UI.Color? Value);

    /// <summary>
    /// Pushes the resolved foreground colour and text attributes onto the content. Shapes are the
    /// exception the Controls library relies on: <c>.ForegroundColor</c> on a shape is its <em>fill</em>,
    /// not its text colour, which is how every pill/badge in that library gets coloured.
    /// </summary>
    void ApplyForeground(XenoAtom.Terminal.UI.Color? color, TextStyle textStyle, double opacity, TStyle? fill)
    {
        if (color is { } c && opacity < 1)
        {
            // Fade toward whatever this node sits on: its own background when it has one, else black,
            // which is the safe assumption for the dominant dark-terminal case.
            var behind = fill?.TryGetBackground(out var b) == true ? b : Colors.Black;
            color = TuiStyle.Blend(c, behind, opacity);
        }

        if (_content is TuiShape shape)
        {
            shape.Fill = color is { } fc ? TStyle.None.WithBackground(fc) : TStyle.None;
            return;
        }

        if (color is null && textStyle == TextStyle.None) return;

        var style = TStyle.None.WithTextStyle(textStyle);
        if (color is { } fg) style = style.WithForeground(fg);

        switch (_content)
        {
            case TextBlock:
                _content.SetStyle(new TextBlockStyle { Foreground = color, TextStyle = textStyle });
                break;
            case Placeholder:
                _content.SetStyle(new PlaceholderStyle { Foreground = color, TextStyle = textStyle });
                break;
            default:
                // Controls that own their own multi-state styling (Button, Switch, Slider…) resolve
                // colours from the theme per state, so a single flat style would fight them. Leave those
                // alone rather than half-overriding them — documented in docs/backends/tui.md.
                break;
        }
    }

    // ---- patch application ---------------------------------------------------

    public void UpdateProps(JsonElement props, JsonElement modifiers)
    {
        Props = ReadDict(props);
        Modifiers = ReadDictArray(modifiers);

        switch (Type)
        {
            case "Text": ((TextBlock)_content).Text = Str("text"); break;
            case "Button": SetButtonTitle(Str("title")); break;
            case "Label": SetLabelText(); break;
            case "Link": SyncLink(); break;
            case "TextField" or "SecureField": SyncText(v => ((TextBox)v).Text = Str("text"), _content); break;
            case "TextEditor": SyncText(v => ((TextArea)v).Text = Str("text"), _content); break;
            case "Toggle": SyncToggle(); break;
            case "Slider": SyncSlider(); break;
            case "Stepper": SyncStepper(); break;
            case "Picker": SyncPicker(); break;
            case "DatePicker": SyncDatePicker(); break;
            case "ColorPicker": SyncColorPicker(); break;
            case "ProgressView" or "Gauge": SyncProgress(); break;
            case "DisclosureGroup": ((Collapsible)_content).IsExpanded = Bool("expanded"); break;
            case "TabView": SyncTabView(); break;
            case "ZStack": SyncZStackAlignment(); break;
            case "Sheet": SyncSheet(); break;
            case "Alert": SyncAlert(); break;
            case "Image": TuiImage.Update(this, _content, _bridge); break;
            default: _customRenderer?.Update(_content, RenderCtx()); break;
        }

        ApplyModifiers();
    }

    void SetButtonTitle(string title)
    {
        if (_content is TButton { Content: TextBlock label }) label.Text = title;
    }

    void SetLabelText()
    {
        if (_content is not THStack { Children.Count: 2 } row) return;
        if (row.Children[0] is TextBlock glyph) glyph.Text = TuiStyle.Glyph(Str("systemImage"));
        if (row.Children[1] is TextBlock title) title.Text = Str("title");
    }

    void SyncLink()
    {
        if (_content is not TLink link) return;
        link.Uri = Str("url");
        link.Text = Str("title");
    }

    /// <summary>
    /// Applies a text value from C# without it bouncing straight back as a user edit: the echo guard is
    /// updated first, so the dynamic-update watcher sees the new value as already-known.
    /// </summary>
    void SyncText(Action<Visual> apply, Visual target)
    {
        var next = Str("text");
        if (next == _lastValue) return;
        _lastValue = next;
        apply(target);
    }

    void SyncToggle()
    {
        if (_content is Switch sw && sw.IsOn != Bool("value")) sw.IsOn = Bool("value");
    }

    void SyncSlider()
    {
        if (_content is not TSlider slider) return;
        var value = Num("value") ?? 0;
        if (Math.Abs(slider.Value - value) > 1e-6) slider.Value = value;
    }

    void SyncStepper()
        => _stepperDisplay?.Text = (Num("value") ?? 0).ToString("0.##", CultureInfo.InvariantCulture);

    void SyncPicker()
    {
        if (_picker is null) return;
        var idx = (int)(Num("selection") ?? 0);
        if (_picker.SelectedIndex != idx && idx >= 0 && idx < _picker.Items.Count) _picker.SelectedIndex = idx;
    }

    void SyncDatePicker()
    {
        if (_dateEntry is null) return;
        var text = DateTimeOffset.FromUnixTimeSeconds((long)(Num("value") ?? 0))
            .LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (text == _lastValue) return;
        _lastValue = text;
        _dateEntry.Text = text;
    }

    void SyncColorPicker()
    {
        if (_colorPicker is null) return;
        var hex = Str("value");
        if (hex == _lastValue || TuiStyle.Parse(hex) is not { } color) return;
        _lastValue = hex;
        _colorPicker.Value = color;
    }

    void SyncProgress()
    {
        if (_progress is null) return;
        var value = Num("value") ?? 0;
        _progress.Value = _gaugeRange is { } range
            ? Math.Clamp((value - range.min) / range.span, 0, 1)
            : Math.Clamp(value, 0, 1);
    }

    void SyncSheet()
    {
        if (Bool("presented"))
        {
            if (_sheet is not null || Children.Count < 2) return;
            _sheet = new Dialog(Text(Str("title")), Children[1].Visual) { IsModal = true, IsDraggable = true };
            _bridge.Windows.AddWindow(_sheet);
            _sheet.Show();
        }
        else if (_sheet is { } open)
        {
            _sheet = null;
            open.Close();
            _bridge.Windows.RemoveWindow(open);
        }
    }

    void SyncAlert()
    {
        if (!Bool("presented"))
        {
            if (_alert is { } open)
            {
                _alert = null;
                open.Close();
                _bridge.Windows.RemoveWindow(open);
            }
            return;
        }
        if (_alert is not null) return;

        var ok = new TButton(Text("OK")) { HorizontalAlignment = Align.End };
        var body = new TVStack(Text(Str("message")), ok) { Spacing = 1 };
        _alert = new Dialog(Text(Str("title")), body) { IsModal = true };
        ok.Click(() =>
        {
            var dialog = _alert;
            _alert = null;
            if (dialog is null) return;
            dialog.Close();
            _bridge.Windows.RemoveWindow(dialog);
            _bridge.Emit(Id, "false");
        });
        _bridge.Windows.AddWindow(_alert);
        _alert.Show();
    }

    public void SetChildren(JsonElement children)
    {
        if (_childHost is null) return;

        // Reconcile by key first (reused rows keep their visual — that is the recycling), then re-append
        // in the new order. setChildren only fires on a key-sequence change, so surviving rows' content
        // is already correct and needs no further patching.
        _childHost.Children.Clear();
        ReconcileChildren(children);
        foreach (var child in Children)
        {
            if (Type == "List") AttachRow(child);
            else _childHost.Children.Add(child.Visual);
        }
    }

    void ReconcileChildren(JsonElement children)
    {
        var keyed = Props.GetValueOrDefault("keyed") as bool? == true;
        var byKey = new Dictionary<string, TuiNode>();
        if (keyed)
            foreach (var c in Children)
                if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<TuiNode>();
        foreach (var el in children.EnumerateArray())
        {
            var key = keyed && el.TryGetProperty("props", out var p) && p.TryGetProperty("key", out var kp)
                ? kp.GetString() : null;
            if (key is not null && byKey.TryGetValue(key, out var reuse) &&
                reuse.Type == el.GetProperty("type").GetString())
            {
                reuse.Adopt(el);
                next.Add(reuse);
            }
            else
                next.Add(Build(el, _bridge));
        }
        Children.Clear();
        Children.AddRange(next);
    }

    /// <summary>
    /// Refreshes a reused node from its incoming wire node. The critical part is the <b>id</b>: node ids
    /// are structural paths, so a row that moved from index 0 to index 1 is now addressed as
    /// <c>0.1</c> — and since <see cref="TuiBridge.Emit"/> routes by id, a recycled row that kept its old
    /// id would fire the action bound to whatever item now sits where it used to be. Descendants are
    /// adopted positionally while their shape matches, so their ids are re-stamped too and their live
    /// control state (caret position, scroll offset) survives the move.
    /// </summary>
    void Adopt(JsonElement e)
    {
        Id = e.GetProperty("id").GetString()!;
        UpdateProps(e.GetProperty("props"), e.GetProperty("modifiers"));

        var incoming = e.GetProperty("children");
        if (Props.GetValueOrDefault("keyed") as bool? == true)
        {
            SetChildren(incoming);
            return;
        }

        var sameShape = incoming.GetArrayLength() == Children.Count;
        if (sameShape)
        {
            var i = 0;
            foreach (var childEl in incoming.EnumerateArray())
            {
                if (Children[i].Type != childEl.GetProperty("type").GetString()) { sameShape = false; break; }
                i++;
            }
        }

        if (sameShape)
        {
            var i = 0;
            foreach (var childEl in incoming.EnumerateArray())
                Children[i++].Adopt(childEl);
        }
        else
        {
            SetChildren(incoming);
        }
    }

    // ---- helpers -------------------------------------------------------------

    internal string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    internal double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    internal bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;

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

/// <summary>
/// A lightweight terminal navigation stack for NavigationStack/NavigationLink: a title bar docked above
/// a swappable content area, with Esc and a Back button popping. The push/pop model matches the GTK
/// backend's <c>NavController</c> so the same shared views navigate identically on both.
/// </summary>
sealed class TuiNavController
{
    readonly ContentSwitcher _content = new();
    readonly TextBlock _title = new("");
    readonly TButton _back;
    readonly List<(Visual visual, string title)> _stack = new();

    public TuiNavController()
    {
        _back = new TButton(new TextBlock("‹ Back")) { IsVisible = false };
        _back.Click(Pop);
    }

    public Visual Build(Visual root)
    {
        _content.Children.Add(root);
        _content.SelectedIndex = 0;
        _content.HorizontalAlignment = Align.Stretch;
        _content.VerticalAlignment = Align.Stretch;
        _stack.Add((root, ""));

        var header = new THStack(_back, _title) { Spacing = 1, HorizontalAlignment = Align.Stretch };
        var dock = new DockLayout(header, _content, null!)
        {
            HorizontalAlignment = Align.Stretch,
            VerticalAlignment = Align.Stretch,
        };
        dock.AddKeyBinding(new KeyGesture(TerminalKey.Escape), Pop);
        return dock;
    }

    public void Push(Visual destination, string title)
    {
        // The destination visual is already parented inside the NavigationLink's own subtree, so it is
        // moved into the switcher rather than copied; popping puts the switcher back on the prior page.
        _content.Children.Add(destination);
        _content.SelectedIndex = _content.Children.Count - 1;
        _stack.Add((destination, title));
        _title.Text = title;
        _back.IsVisible = true;
    }

    void Pop()
    {
        if (_stack.Count <= 1) return;
        _content.Children.RemoveAt(_content.Children.Count - 1);
        _stack.RemoveAt(_stack.Count - 1);
        _content.SelectedIndex = _content.Children.Count - 1;
        _title.Text = _stack[^1].title;
        _back.IsVisible = _stack.Count > 1;
    }
}
