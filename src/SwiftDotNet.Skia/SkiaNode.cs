using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// A node in the retained Skia scene tree — mirrors the wire node and owns its computed layout box and
/// paint state. Unlike the widget-backed backends (GTK/WinUI/Web) there is no native control underneath:
/// each node measures, arranges, paints, and hit-tests itself directly on an <see cref="SKCanvas"/>.
/// Patches (<c>updateProps</c>/<c>setChildren</c>) mutate this tree in place, keyed by structural id.
///
/// Split across two files: this one holds construction, layout, hit-testing and helpers;
/// <c>SkiaNodePaint.cs</c> holds the paint pass.
/// </summary>
sealed partial class SkiaNode
{
    // Id is refreshed when a keyed row is reused across a reconcile (its structural position moved), so
    // events still route to the current render's action table. Type is fixed for a node's lifetime.
    public string Id { get; private set; } = "";
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<SkiaNode> Children { get; } = new();

    SkiaBridge _bridge = null!;
    ISkiaRenderer? _custom;

    // ---- layout results (canvas coordinates) --------------------------------
    public SKRect Frame { get; private set; }
    SKRect _content;                 // Frame minus padding insets
    SKSize _measured;                // outer measured size
    readonly List<SKSize> _childMeasured = new();
    float _gridCellW, _gridCellH;    // Grid only

    // ---- per-node local (backend-owned) state -------------------------------
    int _tabIndex;                   // TabView: selected tab / page
    internal float ScrollOffset;     // ScrollView / List / Form: vertical scroll
    internal float ScrollMax;        // max scroll offset for the current layout
    SkiaNode? _navOwner;             // NavigationLink → its enclosing NavigationStack
    internal SkiaNode? PushedContent;// NavigationStack: currently pushed destination (or null)
    internal string PushedTitle = "";

    // ========================================================================
    //  Construction / patching
    // ========================================================================

    public static SkiaNode Build(JsonElement e, SkiaBridge bridge)
    {
        var node = new SkiaNode
        {
            Id = e.GetProperty("id").GetString()!,
            Type = e.GetProperty("type").GetString()!,
            Props = ReadDict(e.GetProperty("props")),
            Modifiers = ReadDictArray(e.GetProperty("modifiers")),
        };
        node._bridge = bridge;

        // A NavigationStack must be visible to NavigationLinks built beneath it.
        if (node.Type == "NavigationStack") bridge.NavStack.Push(node);
        if (node.Type == "NavigationLink" && bridge.NavStack.Count > 0) node._navOwner = bridge.NavStack.Peek();

        foreach (var child in e.GetProperty("children").EnumerateArray())
            node.Children.Add(Build(child, bridge));

        if (node.Type == "NavigationStack") bridge.NavStack.Pop();

        if (!IsBuiltIn(node.Type)) node._custom = SkiaRenderers.Get(node.Type);
        node.SyncTabIndex();
        return node;
    }

    public void UpdateProps(JsonElement props, JsonElement modifiers)
    {
        Props = ReadDict(props);
        Modifiers = ReadDictArray(modifiers);
        SyncTabIndex();
    }

    /// <summary>When a TabView's selected index is bound, C# state is the source of truth: mirror it into
    /// the engine-local index so a programmatic change switches the page.</summary>
    void SyncTabIndex()
    {
        if (Type == "TabView" && Props.GetValueOrDefault("selectedIndex") is double d)
            _tabIndex = Math.Max(0, (int)d);
    }

    /// <summary>Report a user-driven tab/page change back to C# when the index is bound.</summary>
    void EmitTabIndexIfBound()
    {
        if (HasProp("selectedIndex")) _bridge.Emit(Id, _tabIndex.ToString(CultureInfo.InvariantCulture));
    }

    public void SetChildren(JsonElement children)
    {
        if (Type == "NavigationStack") _bridge.NavStack.Push(this);

        // Keyed containers (a keyed List) reconcile children by their "key" prop so a reused row keeps its
        // SkiaNode instance — preserving nested scroll offsets, animation clocks and custom-renderer state —
        // instead of being torn down and rebuilt. Non-keyed containers keep the simple clear-and-rebuild.
        if (Props.GetValueOrDefault("keyed") as bool? == true)
            ReconcileKeyedChildren(children);
        else
        {
            Children.Clear();
            foreach (var childElement in children.EnumerateArray())
                Children.Add(Build(childElement, _bridge));
        }

        if (Type == "NavigationStack") _bridge.NavStack.Pop();
    }

    /// <summary>Match incoming children against retained ones by their <c>key</c> prop; reuse (and adopt fresh
    /// data into) the survivors, build the newcomers, and drop the rest — preserving per-row backend state.</summary>
    void ReconcileKeyedChildren(JsonElement children)
    {
        var byKey = new Dictionary<string, SkiaNode>();
        foreach (var c in Children)
            if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<SkiaNode>();
        foreach (var el in children.EnumerateArray())
        {
            var key = el.GetProperty("props").TryGetProperty("key", out var kp) ? kp.GetString() : null;
            var type = el.GetProperty("type").GetString();
            if (key is not null && byKey.Remove(key, out var reuse) && reuse.Type == type)
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

    /// <summary>Refresh this reused node from an incoming wire node: its (moved) structural id, props and
    /// modifiers, then its subtree. Descendants are reused positionally when their shape is unchanged so their
    /// ids are re-stamped and local state survives; any shape change rebuilds that subtree fresh.</summary>
    void Adopt(JsonElement e)
    {
        Id = e.GetProperty("id").GetString()!;
        Props = ReadDict(e.GetProperty("props"));
        Modifiers = ReadDictArray(e.GetProperty("modifiers"));

        var incoming = e.GetProperty("children");
        var count = incoming.GetArrayLength();

        // A keyed descendant container reconciles by key; otherwise adopt positionally when the shape matches.
        if (Props.GetValueOrDefault("keyed") as bool? == true)
        {
            if (Type == "NavigationStack") _bridge.NavStack.Push(this);
            ReconcileKeyedChildren(incoming);
            if (Type == "NavigationStack") _bridge.NavStack.Pop();
            return;
        }

        var sameShape = count == Children.Count;
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
            Children.Clear();
            if (Type == "NavigationStack") _bridge.NavStack.Push(this);
            foreach (var childEl in incoming.EnumerateArray())
                Children.Add(Build(childEl, _bridge));
            if (Type == "NavigationStack") _bridge.NavStack.Pop();
        }
    }

    // ========================================================================
    //  LAYOUT — Measure (intrinsic size) then Arrange (final rects)
    // ========================================================================

    // ---- implicit animation (.Animation(spec, on:) ) -----------------------
    bool _animInit;
    string _animTrigger = "";
    double _animT = 1, _animDur = 0.3;
    string? _animCurve;
    float _fromO = 1, _toO = 1, _fromH, _toH;

    // F4 repeating animation (shimmer/pulse): self-playing, no trigger. Matches the Web backend's
    // `sdn-pulse` keyframes so the effect reads identically everywhere — opacity oscillates between the
    // resting value and PulseFloor, autoreversing (yo-yo) or restarting each cycle.
    const float PulseFloor = 0.4f;
    double _pulsePhase;          // 0..1 position within the current cycle
    int _pulseDir = 1;           // +1 forward, -1 reversing (autoreverse only)
    int _pulseCycles;            // completed cycles, for a finite repeatCount
    bool _pulseDone;

    int? RepeatCount => MNull(Mod("animation"), "repeatCount") is { } rc ? (int)rc : null;
    bool AutoReverse => Mod("animation")?.GetValueOrDefault("autoreverse") as string == "true";
    bool Repeating => RepeatCount is not null && !_pulseDone;

    // Detect a trigger change and (re)arm interpolation of the animatable props (opacity, frame height).
    void UpdateAnimation()
    {
        var anim = Mod("animation");
        if (anim is null) return;
        if (RepeatCount is not null) { _animDur = Math.Max(0.05, MNull(anim, "duration") ?? 0.3); _animCurve = anim.GetValueOrDefault("curve") as string; return; }
        var trig = anim.GetValueOrDefault("trigger") as string ?? "";
        var targetO = (float)(MNull(Mod("opacity"), "amount") ?? 1);
        var targetH = (float)(MNull(Mod("frame"), "height") ?? 0);
        if (!_animInit) { _animInit = true; _animTrigger = trig; _fromO = _toO = targetO; _fromH = _toH = targetH; _animT = 1; return; }
        if (trig != _animTrigger)
        {
            _fromO = AnimO; _fromH = AnimH;           // start from where we currently are
            _toO = targetO; _toH = targetH;
            _animTrigger = trig; _animT = 0;
            _animDur = Math.Max(0.05, MNull(anim, "duration") ?? 0.3);
            _animCurve = anim.GetValueOrDefault("curve") as string;
        }
        else if (_animT >= 1) { _toO = targetO; _toH = targetH; } // settle to any non-animated change
    }

    float Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return _animCurve switch
        {
            "linear" => (float)t,
            "easeIn" => (float)(t * t),
            "easeOut" => (float)(t * (2 - t)),
            "spring" => (float)(1 - Math.Exp(-6 * t) * Math.Cos(t * Math.PI * 1.5)), // decaying settle
            _ => (float)(t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2),       // easeInOut
        };
    }

    float AnimO
    {
        get
        {
            if (Mod("animation") is null) return RawOpacity;
            if (RepeatCount is not null) return RawOpacity * (1 - (1 - PulseFloor) * Ease(_pulsePhase));
            return _fromO + (_toO - _fromO) * Ease(_animT);
        }
    }
    float AnimH => _fromH + (_toH - _fromH) * Ease(_animT);
    bool Animating => Mod("animation") is not null && (Repeating || _animT < 1);

    /// <summary>Advance this node's animation clock by <paramref name="dt"/>s; returns true while still animating.</summary>
    public bool Tick(double dt)
    {
        var active = false;
        if (Mod("animation") is not null)
        {
            if (RepeatCount is { } repeat)
            {
                if (!_pulseDone) { AdvancePulse(dt, repeat); active = true; }
            }
            else if (_animT < 1) { _animT = Math.Min(1, _animT + dt / _animDur); active = true; }
        }
        foreach (var c in Children) active |= c.Tick(dt);
        return active;
    }

    // Free-running oscillator for a repeating animation. autoreverse yo-yos within a cycle; otherwise each
    // cycle restarts from 0. repeat < 0 runs forever; a finite count settles back to the resting value.
    void AdvancePulse(double dt, int repeat)
    {
        _pulsePhase += _pulseDir * dt / _animDur;
        if (_pulsePhase is >= 0 and <= 1) return;

        if (AutoReverse)
        {
            _pulseDir = -_pulseDir;
            _pulsePhase = Math.Clamp(_pulsePhase, 0, 1);
            if (_pulseDir > 0) _pulseCycles++;   // a full there-and-back is one cycle
        }
        else { _pulsePhase = 0; _pulseCycles++; }

        if (repeat >= 0 && _pulseCycles >= repeat) { _pulseDone = true; _pulsePhase = 0; _pulseDir = 1; }
    }

    public SKSize Measure(SKSize available)
    {
        UpdateAnimation();
        var pad = Padding();
        var inner = new SKSize(
            Math.Max(0, available.Width - pad.Horizontal),
            Math.Max(0, available.Height - pad.Vertical));

        var content = MeasureContent(inner);
        var outer = new SKSize(content.Width + pad.Horizontal, content.Height + pad.Vertical);

        var (fw, fh) = FrameSize();
        if (fw is { } w) outer.Width = (float)w;
        if (Mod("animation") is not null && Mod("frame")?.ContainsKey("height") == true) outer.Height = AnimH;
        else if (fh is { } h) outer.Height = (float)h;
        if (Mod("align") is not null || FillsWidth) outer.Width = available.Width;

        _measured = outer;
        return outer;
    }

    SKSize MeasureContent(SKSize inner)
    {
        _childMeasured.Clear();
        switch (Type)
        {
            case "Text":
                return MeasureWrapped(Str("text"), Font(), inner.Width);
            case "Button":
            {
                var t = MeasureText(Str("title"), Font());
                return new SKSize(t.Width + 36, Math.Max(t.Height, 20) + 18);
            }
            case "Link":
                return MeasureText(Str("title"), Font());
            case "Image":
                // An SF Symbol measures as its glyph. A raster image carries `contentMode` (set by every
                // non-system Image factory) and is *greedy* — it fills the space offered, same convention as
                // shapes, with a .Frame overriding. Measuring it as a glyph collapsed unframed raster images
                // (e.g. the Controls ImageViewer's full-screen image) to nothing.
                return HasProp("contentMode") ? inner : MeasureText(SkiaTheme.Icon(Str("system")), IconFont(22));
            case "Label":
                return MeasureText(SkiaTheme.Icon(Str("systemImage")) + "  " + Str("title"), Font());
            case "Divider":
                return new SKSize(inner.Width, 1);
            case "Spacer":
                return new SKSize(0, 0);
            case "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle":
                // Shapes are greedy: they fill the space offered (a .Frame modifier overrides). SwiftUI parity.
                return inner;
            case "ProgressView":
                return new SKSize(inner.Width, HasProp("label") ? 44 : 6);
            case "Gauge":
                return new SKSize(inner.Width, HasProp("label") ? 48 : 26);
            case "WebView":
                return new SKSize(inner.Width, 120);

            // simple full-width control rows
            case "TextField" or "SecureField":
                return new SKSize(inner.Width, 40);
            case "TextEditor":
                return new SKSize(inner.Width, 100);
            case "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu":
                return new SKSize(inner.Width, 44);

            case "DisclosureGroup":
                return MeasureDisclosure(inner);

            // stacks / containers
            case "HStack":
                return MeasureStack(inner, horizontal: true);
            case "VStack" or "Group":
                return MeasureStack(inner, horizontal: false);
            case "List" when IsGridList:
                return MeasureScrollableGrid(inner);
            case "ScrollView" or "List" or "Form" or "Section":
                return MeasureScrollable(inner);
            case "ZStack":
                return MeasureZ(inner);
            case "Grid":
                return MeasureGrid(inner);
            case "Tab":
                return MeasureFill(inner);
            case "NavigationStack":
            {
                var avail = new SKSize(inner.Width, Math.Max(0, inner.Height - NavBarHeight));
                if (Children.Count > 0) _childMeasured.Add(Children[0].Measure(avail));
                return inner;
            }
            case "NavigationLink":
                return MeasureNavLink(inner);
            case "Sheet" or "Alert":
                return Children.Count > 0 ? Children[0].Measure(inner) : new SKSize(0, 0);
            case "TabView":
            {
                // Only the selected tab/page is shown — measure just its subtree so Arrange has data.
                var barH = Paged ? 28f : TabBarHeight;
                var childAvail = new SKSize(inner.Width, Math.Max(0, inner.Height - barH));
                if (_tabIndex < Children.Count) Children[_tabIndex].Measure(childAvail);
                return inner; // TabView fills
            }

            default:
                if (_custom is { } r) return r.Measure(RenderCtx(), inner);
                return MeasureText("⚠️ " + Type, Font());
        }
    }

    SKSize MeasureStack(SKSize inner, bool horizontal)
    {
        var spacing = (float)(Num("spacing") ?? 8);
        var count = Children.Count;
        var gaps = count > 1 ? spacing * (count - 1) : 0;

        if (!horizontal)
        {
            float mainV = 0, crossV = 0;
            foreach (var c in Children)
            {
                var s = c.Measure(inner);
                _childMeasured.Add(s);
                mainV += s.Height;
                crossV = Math.Max(crossV, s.Width);
            }
            return new SKSize(crossV, mainV + gaps);
        }

        // Horizontal: measure everyone at the full offered width first (SwiftUI-ish: each child takes
        // its ideal size).
        var sizes = new SKSize[count];
        float main = gaps, cross = 0;
        for (var i = 0; i < count; i++)
        {
            var s = Children[i].Measure(inner);
            sizes[i] = s;
            main += s.Width;
            cross = Math.Max(cross, s.Height);
        }

        // Only if that overflows the row do greedy children (TextField, Slider, anything with a
        // maxWidth frame — each of which claimed the *whole* width) give ground and share what's
        // left over after the fixed-size ones. Rows that already fit are untouched, so this changes
        // nothing except the overflow case — e.g. HStack(TextField, "Send"), which otherwise measures
        // wider than its parent and gets centred to a negative x, clipping at both edges.
        if (main > inner.Width)
        {
            var greedy = new List<int>();
            float fixedW = 0;
            for (var i = 0; i < count; i++)
            {
                if (Children[i].GreedyWidth) greedy.Add(i);
                else fixedW += sizes[i].Width;
            }

            if (greedy.Count > 0)
            {
                var share = Math.Max(0, inner.Width - fixedW - gaps) / greedy.Count;
                main = gaps + fixedW;
                cross = 0;
                foreach (var s in sizes) cross = Math.Max(cross, s.Height);
                foreach (var i in greedy)
                {
                    var s = Children[i].Measure(new SKSize(share, inner.Height));
                    sizes[i] = s;
                    main += s.Width;
                    cross = Math.Max(cross, s.Height);
                }
            }
        }

        _childMeasured.AddRange(sizes);
        return new SKSize(main, cross);
    }

    // Scrollables measure their content like a VStack but report only the available height
    // (content taller than that scrolls). Section adds a header line.
    SKSize MeasureScrollable(SKSize inner)
    {
        var headerH = Type == "Section" && HasProp("header") ? 26f : 0f;
        var spacing = Type == "Section" ? 6f : (Type == "ScrollView" ? 12f : 10f);
        float contentH = headerH, cross = 0;
        var count = 0;
        foreach (var c in Children)
        {
            var s = c.Measure(new SKSize(inner.Width, inner.Height));
            _childMeasured.Add(s);
            contentH += s.Height;
            cross = Math.Max(cross, s.Width);
            count++;
        }
        if (count > 1) contentH += spacing * (count - 1);
        _naturalHeight = contentH;
        // Section reports its natural height (it lives inside a scrollable). ScrollView/List/Form cap to available.
        if (Type is "Section")
            return new SKSize(Math.Max(cross, inner.Width), contentH);
        return new SKSize(inner.Width, Math.Min(contentH, inner.Height));
    }

    float _naturalHeight;

    SKSize MeasureZ(SKSize inner)
    {
        float w = 0, h = 0;
        foreach (var c in Children)
        {
            var s = c.Measure(inner);
            _childMeasured.Add(s);
            w = Math.Max(w, s.Width);
            h = Math.Max(h, s.Height);
        }
        return new SKSize(w, h);
    }

    SKSize MeasureGrid(SKSize inner)
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var spacing = (float)(Num("spacing") ?? 8);
        float cellW = 0, cellH = 0;
        foreach (var c in Children)
        {
            var s = c.Measure(inner);
            _childMeasured.Add(s);
            cellW = Math.Max(cellW, s.Width);
            cellH = Math.Max(cellH, s.Height);
        }
        _gridCellW = cellW;
        _gridCellH = cellH;
        var rows = (int)Math.Ceiling(Children.Count / (double)cols);
        var w = cols * cellW + (cols - 1) * spacing;
        var h = rows * cellH + Math.Max(0, rows - 1) * spacing;
        return new SKSize(w, h);
    }

    SKSize MeasureFill(SKSize inner)
    {
        if (Children.Count > 0)
        {
            var s = Children[0].Measure(inner);
            _childMeasured.Add(s);
        }
        return inner;
    }

    SKSize MeasureNavLink(SKSize inner)
    {
        // child 0 = label (shown as a row); child 1 = destination (measured only when pushed)
        var label = Children.Count > 0 ? Children[0].Measure(inner) : new SKSize(0, 0);
        _childMeasured.Add(label);
        return new SKSize(inner.Width, Math.Max(label.Height, 22) + 8);
    }

    SKSize MeasureDisclosure(SKSize inner)
    {
        var h = 40f; // header row
        if (Bool("expanded"))
            foreach (var c in Children)
            {
                var s = c.Measure(inner);
                h += s.Height + 6;
            }
        return new SKSize(inner.Width, h);
    }

    // ---- Arrange -------------------------------------------------------------

    public void Arrange(SKRect rect)
    {
        Frame = rect;
        var pad = Padding();
        _content = new SKRect(rect.Left + pad.Left, rect.Top + pad.Top, rect.Right - pad.Right, rect.Bottom - pad.Bottom);

        switch (Type)
        {
            case "HStack":
                ArrangeStack(horizontal: true);
                break;
            case "VStack" or "Group":
                ArrangeStack(horizontal: false);
                break;
            case "List" when IsGridList:
                ArrangeScrollableGrid();
                break;
            case "ScrollView" or "List" or "Form" or "Section":
                ArrangeScrollable();
                break;
            case "ZStack":
                ArrangeZ();
                break;
            case "Grid":
                ArrangeGrid();
                break;
            case "Tab":
                if (Children.Count > 0) Children[0].Arrange(_content);
                break;
            case "NavigationStack":
                if (Children.Count > 0)
                    Children[0].Arrange(new SKRect(_content.Left, _content.Top + NavBarHeight, _content.Right, _content.Bottom));
                break;
            case "NavigationLink":
                if (Children.Count > 0) Children[0].Arrange(new SKRect(_content.Left, _content.Top, _content.Right - 20, _content.Bottom));
                break;
            case "Sheet" or "Alert":
                if (Children.Count > 0) Children[0].Arrange(_content);
                break;
            case "TabView":
                ArrangeTabView();
                break;
            case "DisclosureGroup":
                ArrangeDisclosure();
                break;
        }
    }

    void ArrangeStack(bool horizontal)
    {
        var spacing = (float)(Num("spacing") ?? 8);
        float fixedMain = 0;
        var spacers = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            if (Children[i].Type == "Spacer") spacers++;
            else fixedMain += horizontal ? _childMeasured[i].Width : _childMeasured[i].Height;
        }
        if (Children.Count > 1) fixedMain += spacing * (Children.Count - 1);

        var extent = horizontal ? _content.Width : _content.Height;
        var free = Math.Max(0, extent - fixedMain);
        var spacerEach = spacers > 0 ? free / spacers : 0;
        var cursor = (horizontal ? _content.Left : _content.Top) + (spacers == 0 ? free / 2 : 0);

        for (var i = 0; i < Children.Count; i++)
        {
            if (i > 0) cursor += spacing;
            var child = Children[i];
            var m = _childMeasured[i];
            if (horizontal)
            {
                var cw = child.Type == "Spacer" ? spacerEach : m.Width;
                var y = CrossPos(_content.Top, _content.Height, m.Height, CrossToken(), vertical: true);
                child.Arrange(new SKRect(cursor, y, cursor + cw, y + m.Height));
                cursor += cw;
            }
            else
            {
                var ch = child.Type == "Spacer" ? spacerEach : m.Height;
                var x = CrossPos(_content.Left, _content.Width, m.Width, CrossToken(), vertical: false);
                child.Arrange(new SKRect(x, cursor, x + m.Width, cursor + ch));
                cursor += ch;
            }
        }
    }

    bool IsGridList => Type == "List" && Str("layout") == "grid";

    // A grid List measures uniform cells (like Grid) but reports only the available height and remembers
    // the natural height so it can scroll vertically.
    SKSize MeasureScrollableGrid(SKSize inner)
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var spacing = (float)(Num("spacing") ?? 8);
        float cellW = 0, cellH = 0;
        var cellAvail = new SKSize(Math.Max(0, (inner.Width - (cols - 1) * spacing) / cols), inner.Height);
        foreach (var c in Children)
        {
            var s = c.Measure(cellAvail);
            _childMeasured.Add(s);
            cellW = Math.Max(cellW, s.Width);
            cellH = Math.Max(cellH, s.Height);
        }
        // Cells fill their column width so the grid is evenly spaced.
        _gridCellW = Math.Max(cellW, cellAvail.Width);
        _gridCellH = cellH;
        var rows = (int)Math.Ceiling(Children.Count / (double)cols);
        _naturalHeight = rows * cellH + Math.Max(0, rows - 1) * spacing;
        return new SKSize(inner.Width, Math.Min(_naturalHeight, inner.Height));
    }

    void ArrangeScrollableGrid()
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var spacing = (float)(Num("spacing") ?? 8);
        ScrollMax = Math.Max(0, _naturalHeight - _content.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ScrollMax);
        var top = _content.Top - ScrollOffset;
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var x = _content.Left + col * (_gridCellW + spacing);
            var y = top + row * (_gridCellH + spacing);
            Children[i].Arrange(new SKRect(x, y, x + _gridCellW, y + _gridCellH));
        }
    }

    void ArrangeScrollable()
    {
        var spacing = Type == "Section" ? 6f : (Type == "ScrollView" ? 12f : 10f);
        var headerH = Type == "Section" && HasProp("header") ? 26f : 0f;

        // clamp scroll to content
        ScrollMax = Math.Max(0, _naturalHeight - _content.Height);
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ScrollMax);

        // Form/List/Section are leading-aligned (SwiftUI grouped rows); a plain ScrollView centers.
        var leading = Type is "Form" or "List" or "Section";
        var y = _content.Top + headerH - ScrollOffset;
        for (var i = 0; i < Children.Count; i++)
        {
            if (i > 0) y += spacing;
            var m = _childMeasured[i];
            var span = Children[i].FillsWidth || Children[i].Type is "Section" or "List" or "Form" or "Divider" or "ScrollView";
            var cw = span ? _content.Width : m.Width;
            var x = span ? _content.Left
                : leading ? _content.Left
                : CrossPos(_content.Left, _content.Width, m.Width, null, vertical: false);
            Children[i].Arrange(new SKRect(x, y, x + cw, y + m.Height));
            y += m.Height;
        }
    }

    void ArrangeZ()
    {
        var token = Str("alignment");
        for (var i = 0; i < Children.Count; i++)
        {
            var m = _childMeasured[i];
            var span = Children[i].FillsWidth;
            var cw = span ? _content.Width : m.Width;
            var x = span ? _content.Left : CrossPos(_content.Left, _content.Width, m.Width, token, vertical: false);
            var y = CrossPos(_content.Top, _content.Height, m.Height, token, vertical: true);
            Children[i].Arrange(new SKRect(x, y, x + cw, y + m.Height));
        }
    }

    void ArrangeGrid()
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var spacing = (float)(Num("spacing") ?? 8);
        for (var i = 0; i < Children.Count; i++)
        {
            var col = i % cols;
            var row = i / cols;
            var cellX = _content.Left + col * (_gridCellW + spacing);
            var cellY = _content.Top + row * (_gridCellH + spacing);
            var m = _childMeasured[i];
            // center the child within its cell
            var x = cellX + (_gridCellW - m.Width) / 2;
            var y = cellY + (_gridCellH - m.Height) / 2;
            Children[i].Arrange(new SKRect(x, y, x + m.Width, y + m.Height));
        }
    }

    void ArrangeTabView()
    {
        if (Paged)
        {
            // carousel: selected page fills, minus a dot strip at the bottom
            var pageRect = new SKRect(_content.Left, _content.Top, _content.Right, _content.Bottom - 28);
            if (_tabIndex < Children.Count) Children[_tabIndex].Arrange(pageRect);
        }
        else
        {
            var barTop = _content.Bottom - TabBarHeight;
            var contentRect = new SKRect(_content.Left, _content.Top, _content.Right, barTop);
            if (_tabIndex < Children.Count) Children[_tabIndex].Arrange(contentRect); // selected Tab
        }
    }

    void ArrangeDisclosure()
    {
        if (!Bool("expanded")) return;
        var y = _content.Top + 40;
        foreach (var c in Children)
        {
            var s = c.Measure(new SKSize(_content.Width - 12, _content.Height));
            c.Arrange(new SKRect(_content.Left + 12, y, _content.Right, y + s.Height));
            y += s.Height + 6;
        }
    }

    internal const float TabBarHeight = 56;
    internal const float NavBarHeight = 44;
    bool Paged => Str("style") == "page";
    internal bool MenuOpen;   // Menu popover open state (engine-local)

    // ========================================================================
    //  HIT TESTING — topmost interactive node under the point wins
    // ========================================================================

    public bool HitTest(SKPoint p)
    {
        if (!Frame.Contains(p)) return false;

        // TabView: the bottom bar switches tabs; otherwise forward into the selected tab only.
        if (Type == "TabView")
        {
            if (!Paged && p.Y >= _content.Bottom - TabBarHeight)
            {
                var n = Children.Count;
                if (n > 0)
                {
                    var idx = (int)((p.X - _content.Left) / (_content.Width / n));
                    _tabIndex = Math.Clamp(idx, 0, n - 1);
                    EmitTabIndexIfBound();
                }
                return true;
            }
            if (Paged)
            {
                _tabIndex = p.X < _content.MidX
                    ? Math.Max(0, _tabIndex - 1)
                    : Math.Min(Children.Count - 1, _tabIndex + 1);
                EmitTabIndexIfBound();
                return true;
            }
            return _tabIndex < Children.Count && Children[_tabIndex].HitTest(p);
        }

        // DisclosureGroup: tapping the header row toggles (emits expanded state to C#).
        if (Type == "DisclosureGroup" && p.Y <= _content.Top + 40)
        {
            _bridge.Emit(Id, Bool("expanded") ? "false" : "true");
            return true;
        }

        // NavigationLink: push its destination onto the enclosing stack (engine-local).
        if (Type == "NavigationLink" && _navOwner is { } nav && Children.Count > 1)
        {
            nav.PushedContent = Children[1];
            nav.PushedTitle = Children[1].NavTitle();
            return true;
        }

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            // Tab only exposes the selected child; a plain container exposes all.
            if (Type == "TabView" && i != _tabIndex) continue;
            if (Children[i].HitTest(p)) return true;
        }

        // Selectable List: a tap that no row control consumed selects that row (emits its key to C#).
        if (Type == "List" && HasProp("selectionMode"))
            for (var i = 0; i < Children.Count; i++)
                if (Children[i].Frame.Contains(p) && Children[i].Props.GetValueOrDefault("key") is string key)
                {
                    _bridge.Emit(Id, key);
                    return true;
                }

        // Tapping a text control focuses it (keyboard input then routes here).
        if (Type is "TextField" or "SecureField" or "TextEditor")
        {
            _bridge.FocusedId = IsDisabled ? _bridge.FocusedId : Id;
            return true;
        }
        // A Menu toggles its popover (engine-local overlay).
        if (Type == "Menu")
        {
            if (!IsDisabled) MenuOpen = !MenuOpen;
            return true;
        }

        // Controls that resolve a value from where you tapped (slider set, stepper +/-, picker cycle…).
        if (IsInteractiveControl)
        {
            if (!IsDisabled) ControlTap(p);
            return true;
        }
        // Generic taps: Button / Toggle / onTapGesture.
        if (SelfTap() is { } act)
        {
            if (!IsDisabled) act();
            return true;
        }
        return false;
    }

    bool IsInteractiveControl => Type is "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker";

    Action? SelfTap()
    {
        if (Type is "Button") return () => _bridge.Emit(Id, null);
        if (Type is "Toggle") return () => _bridge.Emit(Id, Bool("value") ? "false" : "true");
        if (Mod("onTapGesture")?.GetValueOrDefault("event") is string ev) return () => _bridge.Emit(ev, null);
        return null;
    }

    static readonly string[] Palette = { "#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF", "#5856D6", "#AF52DE" };

    void ControlTap(SKPoint p)
    {
        switch (Type)
        {
            case "Slider":
            {
                var min = Num("min") ?? 0;
                var max = Num("max") ?? 1;
                var t = Math.Clamp((p.X - (_content.Left + 10)) / Math.Max(1, _content.Width - 20), 0, 1);
                Emit(min + t * (max - min));
                break;
            }
            case "Stepper":
            {
                var v = (int)(Num("value") ?? 0);
                var min = (int)(Num("min") ?? int.MinValue);
                var max = (int)(Num("max") ?? int.MaxValue);
                v = Math.Clamp(p.X > _content.Right - 36 ? v + 1 : v - 1, min, max);
                Emit(v);
                break;
            }
            case "Picker":
            {
                if (Children.Count == 0) break;
                Emit(((int)(Num("selection") ?? 0) + 1) % Children.Count);
                break;
            }
            case "DatePicker":
                Emit((Num("value") ?? 0) + 86400);
                break;
            case "ColorPicker":
            {
                var idx = Array.IndexOf(Palette, Str("value"));
                _bridge.Emit(Id, Palette[(idx + 1 + Palette.Length) % Palette.Length]);
                break;
            }
        }
    }

    void Emit(double v) => _bridge.Emit(Id, v.ToString(CultureInfo.InvariantCulture));
    void Emit(int v) => _bridge.Emit(Id, v.ToString(CultureInfo.InvariantCulture));

    internal string NavTitle() =>
        Modifiers.FirstOrDefault(m => m.GetValueOrDefault("type") as string == "navigationTitle")
            ?.GetValueOrDefault("value") as string ?? "";

    // ========================================================================
    //  Scroll hit resolution (used by the host for wheel / drag)
    // ========================================================================

    /// <summary>
    /// Dispatch a long-press or swipe: the topmost node under <paramref name="p"/> carrying that gesture
    /// modifier emits (swipe also matches the direction token). Mirrors the tap path but for the
    /// timed/directional recognizers the host resolves from raw pointer streams.
    /// </summary>
    public bool DispatchGesture(SKPoint p, string modType, string? direction)
    {
        if (!Frame.Contains(p)) return false;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Type == "TabView" && i != _tabIndex) continue;
            if (Children[i].DispatchGesture(p, modType, direction)) return true;
        }
        if (Mod(modType)?.GetValueOrDefault("event") is string ev)
        {
            if (modType == "onSwipe" && direction is not null && Mod(modType)?.GetValueOrDefault("value") as string != direction)
                return false;
            _bridge.Emit(ev, null);
            return true;
        }
        return false;
    }

    // F1: deepest visible node under `p` that carries `modType` (onDrag/onMagnify), or null. Used to
    // capture a continuous-gesture target at gesture-begin so subsequent moves route to the same node.
    internal SkiaNode? NodeWithModAt(SKPoint p, string modType)
    {
        if (!Frame.Contains(p)) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Type == "TabView" && i != _tabIndex) continue;
            if (Children[i].NodeWithModAt(p, modType) is { } hit) return hit;
        }
        return Mod(modType)?.GetValueOrDefault("event") is string ? this : null;
    }

    internal string? ModEvent(string modType) => Mod(modType)?.GetValueOrDefault("event") as string;

    /// <summary>Children that are actually on screen (a TabView shows only its selected tab) — for the overlay walk.</summary>
    internal IEnumerable<SkiaNode> VisibleOverlayChildren()
    {
        if (Type == "TabView")
            return _tabIndex < Children.Count ? new[] { Children[_tabIndex] } : Array.Empty<SkiaNode>();
        return Children;
    }

    /// <summary>Find the innermost scrollable node under a point (for wheel/drag scrolling).</summary>
    public SkiaNode? ScrollableAt(SKPoint p)
    {
        if (!Frame.Contains(p)) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Type == "TabView" && i != _tabIndex) continue;
            if (Children[i].ScrollableAt(p) is { } inner) return inner;
        }
        return Type is "ScrollView" or "List" or "Form" && ScrollMax > 0 ? this : null;
    }

    // ========================================================================
    //  MODIFIER / PROP HELPERS
    // ========================================================================

    /// <summary>
    /// True when this node claims all the width offered to it — either because its type is
    /// inherently greedy, or because a <c>.Frame(maxWidth: …)</c>-style align modifier makes it so
    /// (both cases are what <see cref="Measure"/> widens to <c>available.Width</c>).
    /// Horizontal stacks use this to decide who shares the leftover space.
    /// </summary>
    internal bool GreedyWidth => Mod("align") is not null || FillsWidth;

    bool FillsWidth => Type is "Divider" or "ProgressView" or "Gauge" or "WebView"
        or "TextField" or "SecureField" or "TextEditor"
        or "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu"
        or "DisclosureGroup" or "NavigationLink";

    static bool IsBuiltIn(string type) => type is
        "Text" or "Button" or "Link" or "Image" or "Label" or "Divider" or "Spacer"
        or "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle"
        or "ProgressView" or "Gauge" or "WebView"
        or "TextField" or "SecureField" or "TextEditor"
        or "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu"
        or "DisclosureGroup" or "HStack" or "VStack" or "Group"
        or "ScrollView" or "List" or "Form" or "Section" or "ZStack" or "Grid"
        or "Tab" or "TabView" or "NavigationStack" or "NavigationLink" or "Sheet" or "Alert";

    SKFont Font() => SkiaTheme.MakeFont(Mod("font")?.GetValueOrDefault("value") as string);
    static SKFont IconFont(float size) => new(SKTypeface.Default, size);

    string? AlignToken() => Mod("align")?.GetValueOrDefault("value") as string
        ?? (Mod("frame")?.GetValueOrDefault("alignment") as string);

    SKColor ForegroundColor(bool dark) => SkiaTheme.Color(Mod("foregroundColor")?.GetValueOrDefault("value") as string, dark);
    SKColor? ForegroundColorOptional(bool dark) =>
        Mod("foregroundColor")?.GetValueOrDefault("value") is string t ? SkiaTheme.Color(t, dark) : null;
    SKColor? BackgroundColor(bool dark) =>
        Mod("background")?.GetValueOrDefault("value") is string t ? SkiaTheme.Color(t, dark) : null;

    float RawOpacity => (float)(MNull(Mod("opacity"), "amount") ?? 1);
    float Opacity() => AnimO;
    bool IsDisabled => Mod("disabled")?.GetValueOrDefault("value") as string == "true";
    (double x, double y, string anchor)? Scale()
    {
        var m = Mod("scaleEffect");
        if (m is null) return null;
        return (MNull(m, "x") ?? 1, MNull(m, "y") ?? 1, m.GetValueOrDefault("value") as string ?? "center");
    }

    // F4 transforms: translation (no layout effect) and rotation around an anchor.
    (double x, double y)? Offset()
    {
        var m = Mod("offset");
        return m is null ? null : (MNull(m, "x") ?? 0, MNull(m, "y") ?? 0);
    }

    (double degrees, string anchor)? Rotation()
    {
        var m = Mod("rotation");
        return m is null ? null : (MNull(m, "degrees") ?? 0, m.GetValueOrDefault("value") as string ?? "center");
    }

    // F5 gradient background: a shader painted in place of the flat background fill when present.
    internal SKShader? BackgroundShader(bool dark)
    {
        if (Mod("background")?.GetValueOrDefault("gradient") is not string spec) return null;
        return SkiaGradient.Shader(spec, Frame, dark);
    }

    // F3 raster: decode once and cache (keyed by the source string) so paint doesn't decode per frame.
    // bytes/file decode synchronously; url goes through SkiaImageLoader's async cache and paints as soon
    // as the fetch lands (the loader invalidates the bridge, which schedules the repaint).
    SKImage? _rasterImage;
    string? _rasterKey;
    internal SKImage? RasterImage()
    {
        if (HasProp("url")) return SkiaImageLoader.Get(Str("url"), _bridge);

        var src = HasProp("bytes") ? "b:" + Str("bytes")
                : HasProp("file") ? "f:" + Str("file")
                : null;
        if (src is null) return null;
        if (src == _rasterKey) return _rasterImage;
        _rasterKey = src;
        _rasterImage?.Dispose();
        try
        {
            if (src[0] == 'b') _rasterImage = SKImage.FromEncodedData(Convert.FromBase64String(Str("bytes")));
            else _rasterImage = SKImage.FromEncodedData(Str("file"));
        }
        catch { _rasterImage = null; }
        return _rasterImage;
    }

    float CornerRadius()
    {
        if (MNull(Mod("cornerRadius"), "radius") is { } r) return (float)r;
        if (MNull(Mod("border"), "cornerRadius") is { } br) return (float)br;
        return 0;
    }

    (SKColor color, double width)? Border(bool dark)
    {
        var m = Mod("border");
        if (m?.GetValueOrDefault("color") is string c) return (SkiaTheme.Color(c, dark), MNull(m, "width") ?? 1);
        return null;
    }

    (double x, double y, double radius, SKColor color)? Shadow()
    {
        var m = Mod("shadow");
        if (m is null) return null;
        var col = m.GetValueOrDefault("color") is string c ? SkiaTheme.Color(c, false) : new SKColor(0, 0, 0, 90);
        return (MNull(m, "x") ?? 0, MNull(m, "y") ?? 0, MNull(m, "radius") ?? 4, col);
    }

    EdgeInsets Padding()
    {
        var m = Mod("padding");
        if (m is null) return default;
        return new EdgeInsets(
            (float)(MNull(m, "leading") ?? 0),
            (float)(MNull(m, "top") ?? 0),
            (float)(MNull(m, "trailing") ?? 0),
            (float)(MNull(m, "bottom") ?? 0));
    }

    (double? w, double? h) FrameSize()
    {
        var m = Mod("frame");
        if (m is null) return (null, null);
        return (MNull(m, "width"), MNull(m, "height"));
    }

    // Cross-axis (or Z) positioning of a child of size `size` within [start, start+extent].
    static float CrossPos(float start, float extent, float size, string? token, bool vertical)
    {
        var leading = vertical
            ? token is "top" or "topLeading" or "topTrailing"
            : token is "leading" or "topLeading" or "bottomLeading";
        var trailing = vertical
            ? token is "bottom" or "bottomLeading" or "bottomTrailing"
            : token is "trailing" or "topTrailing" or "bottomTrailing";
        if (leading) return start;
        if (trailing) return Math.Max(start, start + extent - size);
        // Content wider than its slot would otherwise centre to a negative origin and clip on BOTH
        // edges; pin it to the leading edge so it only ever overflows one way.
        return size > extent ? start : start + (extent - size) / 2;
    }

    string? CrossToken() => Props.GetValueOrDefault("alignment") as string;

    SkiaRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    Dictionary<string, object?>? Mod(string type)
    {
        foreach (var m in Modifiers)
            if (m.GetValueOrDefault("type") as string == type) return m;
        return null;
    }

    internal string TextProp() => Str("text");
    bool HasProp(string key) => Props.ContainsKey(key);
    string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;

    static double? MNull(Dictionary<string, object?>? m, string key) =>
        m is not null && m.TryGetValue(key, out var v) && v is double d ? d : null;

    static SKSize MeasureText(string text, SKFont font)
    {
        var m = font.Metrics;
        var h = m.Descent - m.Ascent;
        var w = string.IsNullOrEmpty(text) ? 0 : font.MeasureText(text);
        return new SKSize(w, h);
    }

    // Wraps `text` to `maxWidth`, caching the broken lines for the paint pass. Returns the block size.
    List<string>? _wrapLines;
    SKSize MeasureWrapped(string text, SKFont font, float maxWidth)
    {
        _wrapLines = SkiaText.Wrap(text, font, maxWidth);
        var m = font.Metrics;
        var lineH = m.Descent - m.Ascent;
        float w = 0;
        foreach (var line in _wrapLines) w = Math.Max(w, font.MeasureText(line));
        return new SKSize(w, lineH * _wrapLines.Count);
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

/// <summary>Padding/inset amounts in canvas units.</summary>
readonly record struct EdgeInsets(float Left, float Top, float Right, float Bottom)
{
    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;
}
