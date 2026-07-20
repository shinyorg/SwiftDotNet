using System.Globalization;
using System.Text.Json;

namespace SwiftDotNet;

/// <summary>A node in the retained GTK widget tree — mirrors the wire node and holds its live widget.</summary>
sealed class GtkNode
{
    static int _cssSeq;

    public required string Id { get; init; }
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<GtkNode> Children { get; } = new();
    public Gtk.Widget Widget { get; private set; } = null!;

    GtkBridge _bridge = null!;
    Gtk.CssProvider? _cssProvider;
    string? _cssClass;
    NavController? _nav;      // NavigationStack only
    Gtk.Window? _sheet;      // Sheet only

    public static GtkNode Build(JsonElement e, GtkBridge bridge)
    {
        var node = new GtkNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        node._bridge = bridge;

        // NavigationStack must register its controller BEFORE building children so
        // NavigationLinks inside can capture it.
        if (node.Type == "NavigationStack")
        {
            node._nav = new NavController();
            bridge.NavStack.Push(node._nav);
        }

        foreach (var child in e.GetProperty("children").EnumerateArray())
            node.Children.Add(Build(child, bridge));

        if (node.Type == "NavigationStack") bridge.NavStack.Pop();

        node.Widget = node.CreateWidget();
        node.ApplyModifiers();
        return node;
    }

    // ---- widget construction -------------------------------------------------

    Gtk.Widget CreateWidget() => Type switch
    {
        "Text" => Label(Str("text")),
        "Button" => MakeButton(),
        "Spacer" => Spacer(),
        "Divider" => Gtk.Separator.New(Gtk.Orientation.Horizontal),
        "VStack" => Stack(Gtk.Orientation.Vertical),
        "HStack" => Stack(Gtk.Orientation.Horizontal),
        "ZStack" => MakeZStack(),
        "ScrollView" => MakeScroll(),
        "Grid" => MakeGrid(),
        "List" => MakeList(),
        "Form" => MakeForm(),
        "Section" => MakeSection(),
        "Group" => Stack(Gtk.Orientation.Vertical),
        "DisclosureGroup" => MakeDisclosure(),
        "TabView" => MakeTabView(),
        "Tab" => Children.Count > 0 ? Children[0].Widget : Label(""),
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
        "NavigationStack" => _nav!.Build(Children[0].Widget),
        "NavigationLink" => MakeNavLink(),
        "Sheet" => Children.Count > 0 ? Children[0].Widget : Label(""),
        "Alert" => Children.Count > 0 ? Children[0].Widget : Label(""),
        "WebView" => MakeWebView(),
        "Image" => MakeImage(),
        "Label" => MakeLabel(),
        "ProgressView" => MakeProgress(),
        "Gauge" => MakeGauge(),
        "Link" => Gtk.LinkButton.NewWithLabel(Str("url"), Str("title")),
        "Rectangle" => Shape(0),
        "Circle" => Shape(9999),
        "Capsule" => Shape(9999),
        "RoundedRectangle" => Shape(Num("cornerRadius") ?? 8),
        _ => CustomOrPlaceholder(),
    };

    IGtkRenderer? _customRenderer;

    Gtk.Widget CustomOrPlaceholder()
    {
        _customRenderer = GtkRenderers.Get(Type);
        return _customRenderer is { } r ? r.Create(RenderCtx()) : Label($"⚠️ {Type}");
    }

    GtkRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    static Gtk.Label Label(string text)
    {
        var l = Gtk.Label.New(text);
        l.SetWrap(true);
        return l;
    }

    Gtk.Widget MakeWebView()
    {
        // WebKitGTK isn't referenced (keeps the GTK backend dependency-free / no native shim), so we
        // surface a link to the content instead of embedding a browser engine.
        var url = Str("url");
        return string.IsNullOrEmpty(url)
            ? Label("🌐 Web content (unavailable on GTK without WebKitGTK)")
            : Gtk.LinkButton.NewWithLabel(url, "Open web page ↗");
    }

    Gtk.Widget MakeButton()
    {
        var b = Gtk.Button.NewWithLabel(Str("title"));
        b.Halign = Gtk.Align.Center;
        b.OnClicked += (_, _) => _bridge.Emit(Id, null);
        return b;
    }

    static Gtk.Widget Spacer()
    {
        var b = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        b.Hexpand = true;
        b.Vexpand = true;
        return b;
    }

    Gtk.Widget Stack(Gtk.Orientation orientation)
    {
        var box = Gtk.Box.New(orientation, (int)(Num("spacing") ?? 0));
        var cross = Props.GetValueOrDefault("alignment") as string;
        if (orientation == Gtk.Orientation.Vertical)
            box.Halign = cross is null ? Gtk.Align.Center : GtkStyle.AlignOf(cross);
        else
            box.Valign = cross is null ? Gtk.Align.Center : GtkStyle.VAlignOf(cross);
        foreach (var c in Children) box.Append(c.Widget);
        return box;
    }

    Gtk.Widget MakeZStack()
    {
        var overlay = Gtk.Overlay.New();
        if (Children.Count > 0) overlay.SetChild(Children[0].Widget);
        for (var i = 1; i < Children.Count; i++) overlay.AddOverlay(Children[i].Widget);
        return overlay;
    }

    Gtk.Widget MakeScroll()
    {
        var inner = Gtk.Box.New(
            Str("axis") == "horizontal" ? Gtk.Orientation.Horizontal : Gtk.Orientation.Vertical, 12);
        inner.Halign = Gtk.Align.Center;
        foreach (var c in Children) inner.Append(c.Widget);
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetChild(inner);
        scroll.Vexpand = true;
        scroll.Hexpand = true;
        return scroll;
    }

    Gtk.Widget MakeGrid()
    {
        var cols = (int)(Num("columns") ?? 2);
        var sp = (int)(Num("spacing") ?? 8);
        var grid = Gtk.Grid.New();
        grid.RowSpacing = sp;
        grid.ColumnSpacing = sp;
        for (var i = 0; i < Children.Count; i++)
            grid.Attach(Children[i].Widget, i % cols, i / cols, 1, 1);
        return grid;
    }

    /// <summary>The widget that directly holds child rows when it isn't a plain <c>Gtk.Box</c> (List's
    /// ListBox). SetChildren re-appends children here rather than to <see cref="Widget"/>.</summary>
    Gtk.Widget? _childHost;

    Gtk.Widget MakeList()
    {
        // Grid / horizontal lists use a Grid / horizontal Box (keyed live-reorder is supported for the
        // default vertical ListBox via _childHost; grid/horizontal rebuild on change).
        if (Str("layout") == "grid")
        {
            var cols = Math.Max(1, (int)(Num("columns") ?? 2));
            var grid = Gtk.Grid.New();
            grid.ColumnSpacing = 8;
            grid.RowSpacing = 8;
            for (var i = 0; i < Children.Count; i++)
                grid.Attach(Children[i].Widget, i % cols, i / cols, 1, 1);
            return grid;
        }
        if (Str("axis") == "horizontal")
        {
            var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
            foreach (var c in Children) box.Append(c.Widget);
            var scroll = Gtk.ScrolledWindow.New();
            scroll.SetChild(box);
            return scroll;
        }

        var listBox = Gtk.ListBox.New();
        listBox.AddCssClass("boxed-list");
        _childHost = listBox;
        foreach (var c in Children) listBox.Append(c.Widget);

        // Selection: use the ListBox's native single/multiple selection. Row activation (a tap) emits the
        // row's key to C#; the activated row's live index maps into the reconciled Children list.
        if (Str("selectionMode").Length > 0)
        {
            listBox.SetSelectionMode(Str("selectionMode") == "multiple" ? Gtk.SelectionMode.Multiple : Gtk.SelectionMode.Single);
            listBox.OnRowActivated += (_, args) =>
            {
                var idx = args.Row.GetIndex();
                if (idx >= 0 && idx < Children.Count && Children[idx].Props.GetValueOrDefault("key") is string key)
                    _bridge.Emit(Id, key);
            };
        }
        return listBox;
    }

    Gtk.Widget MakeForm()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 16);
        foreach (var c in Children) box.Append(c.Widget);
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetChild(box);
        scroll.Vexpand = true;
        return scroll;
    }

    Gtk.Widget MakeSection()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 6);
        if (Props.GetValueOrDefault("header") is string header)
        {
            var h = Label(header);
            h.AddCssClass("heading");
            h.Halign = Gtk.Align.Start;
            box.Append(h);
        }
        foreach (var c in Children) box.Append(c.Widget);
        return box;
    }

    Gtk.Widget MakeDisclosure()
    {
        var expander = Gtk.Expander.New(Str("label"));
        expander.SetExpanded(Bool("expanded"));
        var inner = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        foreach (var c in Children) inner.Append(c.Widget);
        expander.SetChild(inner);
        expander.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "expanded")
                _bridge.Emit(Id, expander.GetExpanded() ? "true" : "false");
        };
        return expander;
    }

    Gtk.Widget MakeTabView()
    {
        var notebook = Gtk.Notebook.New();
        foreach (var tab in Children) // each is a Tab node
            notebook.AppendPage(tab.Widget, Gtk.Label.New(tab.Str("title")));
        if (Props.ContainsKey("selectedIndex"))
        {
            notebook.Page = (int)(Num("selectedIndex") ?? 0);
            notebook.OnSwitchPage += (_, args) => _bridge.Emit(Id, ((int)args.PageNum).ToString());
        }
        return notebook;
    }

    void SyncTabView()
    {
        if (Widget is Gtk.Notebook nb && Props.ContainsKey("selectedIndex"))
        {
            var idx = (int)(Num("selectedIndex") ?? 0);
            if (nb.Page != idx) nb.Page = idx;
        }
    }

    Gtk.Widget MakeMenu()
    {
        var menuButton = Gtk.MenuButton.New();
        menuButton.SetLabel(Str("label"));
        var pop = Gtk.Popover.New();
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        foreach (var c in Children)
        {
            var item = Gtk.Button.NewWithLabel(c.Str("title"));
            item.AddCssClass("flat");
            var childId = c.Id;
            item.OnClicked += (_, _) => { pop.Popdown(); _bridge.Emit(childId, null); };
            box.Append(item);
        }
        pop.SetChild(box);
        menuButton.SetPopover(pop);
        return menuButton;
    }

    // ---- controls (two-way bound) -------------------------------------------

    Gtk.Widget MakeEntry(bool secure)
    {
        var entry = Gtk.Entry.New();
        entry.Hexpand = true;
        if (secure) entry.SetVisibility(false);
        entry.SetPlaceholderText(Str("placeholder"));
        entry.SetText(Str("text"));
        entry.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "text") _bridge.Emit(Id, entry.GetText());
        };
        return entry;
    }

    Gtk.Widget MakeTextEditor()
    {
        var view = Gtk.TextView.New();
        view.GetBuffer().SetText(Str("text"), -1);
        view.GetBuffer().OnChanged += (buffer, _) =>
        {
            buffer.GetStartIter(out var start);
            buffer.GetEndIter(out var end);
            _bridge.Emit(Id, buffer.GetText(start, end, false));
        };
        var scroll = Gtk.ScrolledWindow.New();
        scroll.SetChild(view);
        scroll.SetSizeRequest(-1, 100);
        return scroll;
    }

    Gtk.Widget MakeToggle()
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var label = Label(Str("label"));
        label.Hexpand = true;
        label.Halign = Gtk.Align.Start;
        var sw = Gtk.Switch.New();
        sw.SetActive(Bool("value"));
        sw.Valign = Gtk.Align.Center;
        sw.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "active") _bridge.Emit(Id, sw.GetActive() ? "true" : "false");
        };
        row.Append(label);
        row.Append(sw);
        return row;
    }

    Gtk.Widget MakeSlider()
    {
        var scale = Gtk.Scale.NewWithRange(Gtk.Orientation.Horizontal, Num("min") ?? 0, Num("max") ?? 1, 0.01);
        scale.SetValue(Num("value") ?? 0);
        scale.Hexpand = true;
        scale.OnValueChanged += (_, _) =>
            _bridge.Emit(Id, scale.GetValue().ToString(CultureInfo.InvariantCulture));
        return scale;
    }

    Gtk.Widget MakeStepper()
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var label = Label(Str("label"));
        label.Hexpand = true;
        label.Halign = Gtk.Align.Start;
        var spin = Gtk.SpinButton.NewWithRange(Num("min") ?? -1e9, Num("max") ?? 1e9, 1);
        spin.SetValue(Num("value") ?? 0);
        spin.OnValueChanged += (_, _) =>
            _bridge.Emit(Id, ((int)spin.GetValue()).ToString(CultureInfo.InvariantCulture));
        row.Append(label);
        row.Append(spin);
        return row;
    }

    Gtk.Widget MakePicker()
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var label = Label(Str("label"));
        label.Hexpand = true;
        label.Halign = Gtk.Align.Start;
        var options = Children.Select(c => c.Str("text")).ToArray();
        var dropdown = Gtk.DropDown.NewFromStrings(options);
        dropdown.SetSelected((uint)(Num("selection") ?? 0));
        dropdown.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() == "selected")
                _bridge.Emit(Id, ((int)dropdown.GetSelected()).ToString(CultureInfo.InvariantCulture));
        };
        row.Append(label);
        row.Append(dropdown);
        return row;
    }

    Gtk.Widget MakeDatePicker()
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var label = Label(Str("label"));
        label.Hexpand = true;
        label.Halign = Gtk.Align.Start;

        var seconds = Num("value") ?? 0;
        var date = DateTimeOffset.FromUnixTimeSeconds((long)seconds).LocalDateTime;
        var menu = Gtk.MenuButton.New();
        menu.SetLabel(date.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));

        var calendar = Gtk.Calendar.New();
        calendar.OnDaySelected += (_, _) =>
            _bridge.Emit(Id, calendar.GetDate().ToUnix().ToString(CultureInfo.InvariantCulture));
        var pop = Gtk.Popover.New();
        pop.SetChild(calendar);
        menu.SetPopover(pop);

        row.Append(label);
        row.Append(menu);
        return row;
    }

    Gtk.Widget MakeColorPicker()
    {
        var row = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        var label = Label(Str("label"));
        label.Hexpand = true;
        label.Halign = Gtk.Align.Start;

        var button = Gtk.ColorDialogButton.New(Gtk.ColorDialog.New());
        var rgba = new Gdk.RGBA();
        if (rgba.Parse(Str("value"))) button.SetRgba(rgba);
        button.OnNotify += (_, e) =>
        {
            if (e.Pspec.GetName() != "rgba") return;
            var c = button.GetRgba();
            var hex = $"#{(int)(c.Red * 255):X2}{(int)(c.Green * 255):X2}{(int)(c.Blue * 255):X2}";
            _bridge.Emit(Id, hex);
        };
        row.Append(label);
        row.Append(button);
        return row;
    }

    // ---- navigation ----------------------------------------------------------

    Gtk.Widget MakeNavLink()
    {
        var nav = _bridge.NavStack.Count > 0 ? _bridge.NavStack.Peek() : null;
        var button = Gtk.Button.New();
        button.AddCssClass("flat");
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        if (Children.Count > 0) box.Append(Children[0].Widget);
        var chevron = Gtk.Label.New("›");
        chevron.Hexpand = true;
        chevron.Halign = Gtk.Align.End;
        box.Append(chevron);
        button.SetChild(box);

        if (Children.Count > 1)
        {
            var dest = Children[1].Widget;
            var title = Children[1].TitleOf();
            button.OnClicked += (_, _) => nav?.Push(dest, title);
        }
        return button;
    }

    string TitleOf() =>
        Modifiers.FirstOrDefault(m => m["type"] as string == "navigationTitle")?["value"] as string ?? "";

    // ---- display -------------------------------------------------------------

    Gtk.Widget MakeLabel()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Horizontal, 6);
        box.Append(Label(GtkStyle.Emoji(Str("systemImage"))));
        box.Append(Label(Str("title")));
        return box;
    }

    // F3 raster: load a real bitmap into a Gtk.Picture from a file path or PNG bytes; an SF-Symbol name
    // falls back to the emoji glyph. (Remote URLs aren't fetched synchronously here — a documented gap;
    // pass file/bytes on GTK.)
    Gtk.Widget MakeImage()
    {
        try
        {
            if (Props.GetValueOrDefault("file") is string file && file.Length > 0)
            {
                var pic = Gtk.Picture.NewForFilename(file);
                pic.ContentFit = Str("contentMode") == "fill" ? Gtk.ContentFit.Cover : Gtk.ContentFit.Contain;
                return pic;
            }
            if (Props.GetValueOrDefault("bytes") is string b64 && b64.Length > 0)
            {
                var bytes = Convert.FromBase64String(b64);
                var gbytes = GLib.Bytes.New(bytes);
                var texture = Gdk.Texture.NewFromBytes(gbytes);
                var pic = Gtk.Picture.NewForPaintable(texture);
                pic.ContentFit = Str("contentMode") == "fill" ? Gtk.ContentFit.Cover : Gtk.ContentFit.Contain;
                return pic;
            }
        }
        catch { /* fall through to a placeholder glyph on any decode error */ }
        // Only fall back to a glyph when an SF Symbol was actually requested; a raster-only image that
        // failed to load renders empty so the caller's own placeholder shows through.
        return Label(Str("system").Length > 0 ? GtkStyle.Emoji(Str("system")) : "");
    }

    Gtk.Widget MakeProgress()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        if (Props.GetValueOrDefault("label") is string text) box.Append(Label(text));
        if (Num("value") is { } value)
        {
            var bar = Gtk.ProgressBar.New();
            bar.SetFraction(value);
            box.Append(bar);
        }
        else
        {
            var spinner = Gtk.Spinner.New();
            spinner.Start();
            box.Append(spinner);
        }
        return box;
    }

    Gtk.Widget MakeGauge()
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 4);
        if (Props.GetValueOrDefault("label") is string text) box.Append(Label(text));
        var level = Gtk.LevelBar.New();
        level.SetMinValue(Num("min") ?? 0);
        level.SetMaxValue(Num("max") ?? 1);
        level.SetValue(Num("value") ?? 0);
        box.Append(level);
        return box;
    }

    Gtk.Widget Shape(double cornerRadius)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        box.SetSizeRequest(40, 40);
        // Fill + radius are applied via CSS in ApplyModifiers (foregroundColor → background for shapes).
        AddCss(box, $"border-radius:{cornerRadius.ToString(CultureInfo.InvariantCulture)}px;", shapeFill: true);
        return box;
    }

    // ---- modifiers -----------------------------------------------------------

    void ApplyModifiers()
    {
        var w = Widget;
        foreach (var m in Modifiers)
        {
            switch (m["type"] as string)
            {
                case "frame":
                    var width = Num(m, "width");
                    var height = Num(m, "height");
                    w.SetSizeRequest(width is { } fw ? (int)fw : -1, height is { } fh ? (int)fh : -1);
                    if (m.GetValueOrDefault("alignment") is string fa) { w.Halign = GtkStyle.AlignOf(fa); w.Valign = GtkStyle.VAlignOf(fa); }
                    break;
                case "align":
                    w.Hexpand = true;
                    w.Halign = GtkStyle.AlignOf(m.GetValueOrDefault("value") as string);
                    break;
                case "opacity":
                    w.Opacity = Num(m, "amount") ?? 1;
                    break;
                case "disabled":
                    // GTK greys out and blocks input on insensitive widgets automatically.
                    w.Sensitive = (m.GetValueOrDefault("value") as string) != "true";
                    break;
                case "scaleEffect":
                case "rotation":
                    // GTK4 has no generic per-widget scale/rotate transform — documented no-op (F4).
                    // (`offset` IS supported, via a CSS margin translation applied in GtkStyle.BuildCss.)
                    break;
                case "onTapGesture":
                    if (m.GetValueOrDefault("event") is string ev)
                    {
                        var required = (int)(Num(m, "amount") ?? 1);
                        var gesture = Gtk.GestureClick.New();
                        gesture.OnReleased += (_, args) => { if (args.NPress >= required) _bridge.Emit(ev, null); };
                        w.AddController(gesture);
                    }
                    break;
                case "onLongPress":
                    if (m.GetValueOrDefault("event") is string lev)
                    {
                        var lp = Gtk.GestureLongPress.New();
                        lp.OnPressed += (_, _) => _bridge.Emit(lev, null);
                        w.AddController(lp);
                    }
                    break;
                case "onSwipe":
                    if (m.GetValueOrDefault("event") is string sev)
                    {
                        var dir = m.GetValueOrDefault("value") as string;
                        var sw = Gtk.GestureSwipe.New();
                        sw.OnSwipe += (_, args) =>
                        {
                            double vx = args.VelocityX, vy = args.VelocityY;
                            var matched = Math.Abs(vx) > Math.Abs(vy)
                                ? (vx < 0 ? dir == "left" : dir == "right")
                                : (vy < 0 ? dir == "up" : dir == "down");
                            if (matched && (Math.Abs(vx) > 40 || Math.Abs(vy) > 40)) _bridge.Emit(sev, null);
                        };
                        w.AddController(sw);
                    }
                    break;
                case "onDrag":
                    // F1 continuous drag → the "<phase>;tx,ty;lx,ly;vx,vy" grammar. GestureDrag gives the
                    // start point and running offset; velocity is unavailable here, sent as 0.
                    if (m.GetValueOrDefault("event") is string dev)
                    {
                        var gd = Gtk.GestureDrag.New();
                        double sx = 0, sy = 0;
                        gd.OnDragBegin += (_, a) => { sx = a.StartX; sy = a.StartY; _bridge.Emit(dev, $"b;0,0;{F(sx)},{F(sy)};0,0"); };
                        gd.OnDragUpdate += (_, a) => _bridge.Emit(dev, $"c;{F(a.OffsetX)},{F(a.OffsetY)};{F(sx + a.OffsetX)},{F(sy + a.OffsetY)};0,0");
                        gd.OnDragEnd += (_, a) => _bridge.Emit(dev, $"e;{F(a.OffsetX)},{F(a.OffsetY)};{F(sx + a.OffsetX)},{F(sy + a.OffsetY)};0,0");
                        w.AddController(gd);
                    }
                    break;
                case "onMagnify":
                    // F1 pinch → cumulative scale factor. GestureZoom reports the running scale delta.
                    if (m.GetValueOrDefault("event") is string mev)
                    {
                        var gz = Gtk.GestureZoom.New();
                        gz.OnScaleChanged += (_, a) => _bridge.Emit(mev, F(a.Scale));
                        w.AddController(gz);
                    }
                    break;
            }
        }

        var css = GtkStyle.BuildCss(Modifiers, shapeFill: IsShape);
        if (css.Length > 0) AddCss(w, css, IsShape);
    }

    bool IsShape => Type is "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle";

    void AddCss(Gtk.Widget w, string body, bool shapeFill)
    {
        _cssClass ??= "sdn-" + System.Threading.Interlocked.Increment(ref _cssSeq);
        if (_cssProvider is null)
        {
            _cssProvider = Gtk.CssProvider.New();
            var display = Gdk.Display.GetDefault();
            if (display is not null)
                Gtk.StyleContext.AddProviderForDisplay(display, _cssProvider, 800);
            w.AddCssClass(_cssClass);
        }
        _cssProvider.LoadFromString($".{_cssClass} {{ {body} }}");
    }

    // ---- patch application ---------------------------------------------------

    public void UpdateProps(JsonElement props, JsonElement modifiers)
    {
        Props = ReadDict(props);
        Modifiers = ReadDictArray(modifiers);

        switch (Type)
        {
            case "Text": ((Gtk.Label)Widget).SetText(Str("text")); break;
            case "Button": ((Gtk.Button)Widget).SetLabel(Str("title")); break;
            case "Toggle": SyncToggle(); break;
            case "Slider": SyncSlider(); break;
            case "Sheet": SyncSheet(); break;
            case "Alert": SyncAlert(); break;
            case "DisclosureGroup": ((Gtk.Expander)Widget).SetExpanded(Bool("expanded")); break;
            case "TabView": SyncTabView(); break;
            default: _customRenderer?.Update(Widget, RenderCtx()); break;
        }

        // Re-apply CSS-driven modifiers (fill/border/etc. may have changed).
        var css = GtkStyle.BuildCss(Modifiers, IsShape);
        if (css.Length > 0) AddCss(Widget, css, IsShape);
    }

    void SyncToggle()
    {
        var sw = (Gtk.Switch)((Gtk.Box)Widget).GetLastChild()!;
        if (sw.GetActive() != Bool("value")) sw.SetActive(Bool("value"));
    }

    void SyncSlider()
    {
        var scale = (Gtk.Scale)Widget;
        if (Math.Abs(scale.GetValue() - (Num("value") ?? 0)) > 0.0001) scale.SetValue(Num("value") ?? 0);
    }

    Gtk.Window? _alert;

    void SyncAlert()
    {
        if (!Bool("presented"))
        {
            if (_alert is { } open) { _alert = null; open.Close(); }
            return;
        }
        if (_alert is not null) return;

        _alert = Gtk.Window.New();
        _alert.SetModal(true);
        _alert.SetResizable(false);
        _alert.Title = Str("title");
        if (Widget.GetRoot() as Gtk.Window is { } parent) _alert.SetTransientFor(parent);

        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 12);
        box.SetMarginTop(20);
        box.SetMarginBottom(20);
        box.SetMarginStart(24);
        box.SetMarginEnd(24);
        var title = Gtk.Label.New(Str("title"));
        title.AddCssClass("title-3");
        var message = Gtk.Label.New(Str("message"));
        message.SetWrap(true);
        var ok = Gtk.Button.NewWithLabel("OK");
        ok.AddCssClass("suggested-action");
        ok.Halign = Gtk.Align.End;
        ok.OnClicked += (_, _) => _alert?.Close();
        box.Append(title);
        box.Append(message);
        box.Append(ok);
        _alert.SetChild(box);

        _alert.OnCloseRequest += (_, _) => { _alert = null; _bridge.Emit(Id, "false"); return false; };
        _alert.Present();
    }

    void SyncSheet()
    {
        var window = Widget.GetRoot() as Gtk.Window;
        if (Bool("presented"))
        {
            if (_sheet is not null || Children.Count < 2) return;
            _sheet = Gtk.Window.New();
            _sheet.Title = "";
            _sheet.SetModal(true);
            _sheet.SetDefaultSize(360, 260);
            if (window is not null) _sheet.SetTransientFor(window);
            _sheet.SetChild(Children[1].Widget);
            _sheet.OnCloseRequest += (_, _) => { _bridge.Emit(Id, "false"); return false; };
            _sheet.Present();
        }
        else if (_sheet is not null)
        {
            _sheet.Close();
            _sheet = null;
        }
    }

    public void SetChildren(JsonElement children)
    {
        // The child-hosting widget: a List's ListBox, else the container's own Box.
        var host = _childHost ?? Widget as Gtk.Widget;
        if (host is not (Gtk.Box or Gtk.ListBox)) return;

        // Detach current rows, reconcile by key (reused rows keep their widget — recycling), re-append in
        // order. setChildren only fires on a key-sequence change, so surviving rows' content is unchanged.
        foreach (var child in Children) Detach(host, child.Widget);
        ReconcileChildren(children);
        foreach (var child in Children) Append(host, child.Widget);
    }

    void ReconcileChildren(JsonElement children)
    {
        var keyed = Props.GetValueOrDefault("keyed") as bool? == true;
        var byKey = new Dictionary<string, GtkNode>();
        if (keyed)
            foreach (var c in Children)
                if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<GtkNode>();
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

    static void Detach(Gtk.Widget host, Gtk.Widget child)
    {
        if (host is Gtk.Box b) b.Remove(child);
        else if (host is Gtk.ListBox lb) lb.Remove(child);
    }

    static void Append(Gtk.Widget host, Gtk.Widget child)
    {
        if (host is Gtk.Box b) b.Append(child);
        else if (host is Gtk.ListBox lb) lb.Append(child);
    }

    // ---- helpers -------------------------------------------------------------

    string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    static string F(double v) => v.ToString(CultureInfo.InvariantCulture);
    double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;
    static double? Num(Dictionary<string, object?> m, string key) => m.TryGetValue(key, out var v) && v is double d ? d : null;

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

/// <summary>A lightweight GTK navigation stack (header + content area) for NavigationStack/Link.</summary>
sealed class NavController
{
    Gtk.Box _content = null!;
    Gtk.Label _title = null!;
    Gtk.Button _back = null!;
    readonly List<(Gtk.Widget widget, string title)> _stack = new();

    public Gtk.Widget Build(Gtk.Widget root)
    {
        var box = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        var header = Gtk.Box.New(Gtk.Orientation.Horizontal, 8);
        header.AddCssClass("toolbar");
        _back = Gtk.Button.NewWithLabel("‹ Back");
        _back.AddCssClass("flat");
        _back.SetVisible(false);
        _back.OnClicked += (_, _) => Pop();
        _title = Gtk.Label.New("");
        _title.AddCssClass("heading");
        header.Append(_back);
        header.Append(_title);

        _content = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        _content.Vexpand = true;
        _content.Append(root);

        box.Append(header);
        box.Append(_content);
        _stack.Add((root, ""));
        return box;
    }

    public void Push(Gtk.Widget destination, string title)
    {
        var current = _content.GetFirstChild();
        if (current is not null) _content.Remove(current);
        _content.Append(destination);
        _stack.Add((destination, title));
        _title.SetText(title);
        _back.SetVisible(true);
    }

    void Pop()
    {
        if (_stack.Count <= 1) return;
        _content.Remove(_stack[^1].widget);
        _stack.RemoveAt(_stack.Count - 1);
        var prev = _stack[^1];
        _content.Append(prev.widget);
        _title.SetText(prev.title);
        _back.SetVisible(_stack.Count > 1);
    }
}
