using System.Globalization;
using System.Text.Json;
using SwiftDotNet;

namespace SwiftDotNet.Graphics;

/// <summary>
/// A node in the retained scene tree — mirrors the wire node and owns its computed layout box and
/// paint state. Unlike the widget-backed backends (GTK/WinUI/Web) there is no native control underneath:
/// each node measures, arranges, paints, and hit-tests itself directly on an <see cref="ICanvas"/>.
/// Patches (<c>updateProps</c>/<c>setChildren</c>) mutate this tree in place, keyed by structural id.
///
/// Split across three files: this one holds construction, layout, hit-testing and helpers;
/// <c>VisualNodePaint.cs</c> holds the paint pass and <c>VisualNodeOverlay.cs</c> the presented layer.
/// </summary>
sealed partial class VisualNode
{
    // Id is refreshed when a keyed row is reused across a reconcile (its structural position moved), so
    // events still route to the current render's action table. Type is fixed for a node's lifetime.
    public string Id { get; private set; } = "";
    public required string Type { get; init; }
    public Dictionary<string, object?> Props { get; private set; } = new();
    public List<Dictionary<string, object?>> Modifiers { get; private set; } = new();
    public List<VisualNode> Children { get; } = new();

    VisualBridge _bridge = null!;
    IVisualRenderer? _custom;

    // ---- layout results (canvas coordinates) --------------------------------
    public Rect Frame { get; private set; }
    Rect _content;                 // Frame minus padding insets
    Size _measured;                // outer measured size
    readonly List<Size> _childMeasured = new();
    float _gridCellW, _gridCellH;    // grid List only (uniform cells)
    float[]? _gridColW, _gridRowH;   // Grid: resolved track sizes
    GridSpan[]? _gridSpans;          // Grid: each child's resolved cell

    // ---- per-node local (backend-owned) state -------------------------------
    int _tabIndex;                   // TabView: selected tab / page
    internal float ScrollOffset;     // ScrollView / List / Form: vertical scroll
    internal float ScrollMax;        // max scroll offset for the current layout
    VisualNode? _navOwner;             // NavigationLink → its enclosing NavigationStack
    internal VisualNode? PushedContent;// NavigationStack: currently pushed destination (or null)
    internal string PushedTitle = "";

    // ========================================================================
    //  Construction / patching
    // ========================================================================

    public static VisualNode Build(JsonElement e, VisualBridge bridge)
    {
        var node = new VisualNode
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

        if (!IsBuiltIn(node.Type)) node._custom = VisualRenderers.Get(node.Type);
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
        // VisualNode instance — preserving nested scroll offsets, animation clocks and custom-renderer state —
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
        var byKey = new Dictionary<string, VisualNode>();
        foreach (var c in Children)
            if (c.Props.GetValueOrDefault("key") is string k) byKey[k] = c;

        var next = new List<VisualNode>();
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

    // ---- keyframe timelines (.Keyframes(k => …)) ---------------------------
    // Unlike the pulse above, a timeline carries the whole shape of the animation on the wire, so this
    // drives real per-property values rather than a canned opacity fade. Tracks are parsed once per
    // distinct wire string and sampled on the same clock every frame.
    List<(string Property, List<Keyframe> Stops)>? _kfTracks;
    string? _kfWire;
    string _kfTrigger = "";
    double _kfElapsed;
    bool _kfDone, _kfArmed;

    List<(string Property, List<Keyframe> Stops)>? Keyframes()
    {
        var m = Mod("keyframes");
        if (m is null) return null;
        var wire = m.GetValueOrDefault("tracks") as string ?? "";
        if (wire != _kfWire) { _kfWire = wire; _kfTracks = KeyframeWire.Parse(wire); }
        return _kfTracks;
    }

    /// <summary>
    /// (Re)arms the timeline on first sight and whenever its <c>on:</c> trigger changes — a repeating
    /// timeline just runs, a one-shot replays from the top.
    /// </summary>
    void UpdateKeyframes()
    {
        var m = Mod("keyframes");
        if (m is null) return;
        var trig = m.GetValueOrDefault("trigger") as string ?? "";
        if (!_kfArmed) { _kfArmed = true; _kfTrigger = trig; return; }
        if (trig == _kfTrigger) return;
        _kfTrigger = trig;
        _kfElapsed = 0;
        _kfDone = false;
    }

    /// <summary>The current value of <paramref name="property"/>'s track, or null when the node has none.</summary>
    double? Kf(string property)
    {
        var tracks = Keyframes();
        if (tracks is null) return null;
        var m = Mod("keyframes")!;
        var phase = KeyframeWire.Phase(
            _kfElapsed,
            MNull(m, "duration") ?? 1,
            MNull(m, "delay") ?? 0,
            MNull(m, "repeatCount") is { } rc ? (int)rc : null,
            m.GetValueOrDefault("autoreverse") as string == "true",
            out _);
        var fallback = CurveFor(m.GetValueOrDefault("curve") as string);
        foreach (var (prop, stops) in tracks)
            if (prop == property)
                return KeyframeWire.Sample(stops, phase, fallback);
        return null;
    }

    static AnimationCurve CurveFor(string? token) => token switch
    {
        "linear" => AnimationCurve.Linear,
        "easeIn" => AnimationCurve.EaseIn,
        "easeOut" => AnimationCurve.EaseOut,
        "spring" => AnimationCurve.Spring,
        _ => AnimationCurve.EaseInOut,
    };

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
        // A keyframe timeline runs on its own clock alongside (not instead of) the implicit animation —
        // the two drive disjoint properties whenever both are present.
        if (Mod("keyframes") is { } kf && !_kfDone)
        {
            _kfElapsed += dt;
            KeyframeWire.Phase(
                _kfElapsed,
                MNull(kf, "duration") ?? 1,
                MNull(kf, "delay") ?? 0,
                MNull(kf, "repeatCount") is { } krc ? (int)krc : null,
                kf.GetValueOrDefault("autoreverse") as string == "true",
                out _kfDone);
            active = true;
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

    public Size Measure(Size available)
    {
        UpdateAnimation();
        UpdateKeyframes();
        var pad = Padding();
        var inner = new Size(
            Math.Max(0, available.Width - pad.Horizontal),
            Math.Max(0, available.Height - pad.Vertical));

        var content = MeasureContent(inner);
        var outer = new Size(content.Width + pad.Horizontal, content.Height + pad.Vertical);

        // An explicit .Frame() overrides the measured size on each axis independently; an animating height
        // reads from the interpolator instead. Size is immutable here (it was a mutable SKSize before), so
        // the overrides accumulate into locals and rebuild the value once.
        var (fw, fh) = FrameSize();
        var outerW = outer.Width;
        var outerH = outer.Height;
        if (fw is { } w) outerW = (float)w;
        // An explicit height track wins over the implicit-animation interpolator: it says exactly what the
        // height should be at this instant, where AnimH only knows where it started and where it's headed.
        if (Kf("height") is { } kh) outerH = (float)kh;
        else if (Mod("animation") is not null && Mod("frame")?.ContainsKey("height") == true) outerH = AnimH;
        else if (fh is { } h) outerH = (float)h;
        if (Mod("align") is not null || FillsWidth) outerW = available.Width;
        outer = new Size(outerW, outerH);

        _measured = outer;
        return outer;
    }

    Size MeasureContent(Size inner)
    {
        _childMeasured.Clear();
        switch (Type)
        {
            case "Text":
                return MeasureWrapped(Str("text"), Font(), inner.Width);
            case "Button":
            {
                var t = MeasureText(Str("title"), Font());
                return new Size(t.Width + 36, Math.Max(t.Height, 20) + 18);
            }
            case "Link":
                return MeasureText(Str("title"), Font());
            case "Image":
                // An SF Symbol measures as its glyph. A raster image carries `contentMode` (set by every
                // non-system Image factory) and is *greedy* — it fills the space offered, same convention as
                // shapes, with a .Frame overriding. Measuring it as a glyph collapsed unframed raster images
                // (e.g. the Controls ImageViewer's full-screen image) to nothing.
                return HasProp("contentMode") ? inner : MeasureText(Theme.Icon(Str("system")), IconFont(22));
            case "Label":
                return MeasureText(Theme.Icon(Str("systemImage")) + "  " + Str("title"), Font());
            case "Divider":
                return new Size(inner.Width, 1);
            case "Spacer":
                return new Size(0, 0);
            case "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle":
                // Shapes are greedy: they fill the space offered (a .Frame modifier overrides). SwiftUI parity.
                return inner;
            case "ProgressView":
                return new Size(inner.Width, HasProp("label") ? 44 : 6);
            case "Gauge":
                return new Size(inner.Width, HasProp("label") ? 48 : 26);
            case "WebView":
                return new Size(inner.Width, 120);

            // simple full-width control rows
            case "TextField" or "SecureField":
                return new Size(inner.Width, 40);
            case "TextEditor":
                return new Size(inner.Width, 100);
            case "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu":
                return new Size(inner.Width, 44);

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
            case "AbsoluteLayout":
                return MeasureAbsolute(inner);
            case "Tab":
                return MeasureFill(inner);
            case "NavigationStack":
            {
                var avail = new Size(inner.Width, Math.Max(0, inner.Height - NavBarHeight));
                if (Children.Count > 0) _childMeasured.Add(Children[0].Measure(avail));
                return inner;
            }
            case "NavigationLink":
                return MeasureNavLink(inner);
            case "Sheet" or "Alert" or "ActionSheet":
                return Children.Count > 0 ? Children[0].Measure(inner) : new Size(0, 0);
            case "TabView":
            {
                // Only the selected tab/page is shown — measure just its subtree so Arrange has data.
                var barH = Paged ? 28f : TabBarHeight;
                var childAvail = new Size(inner.Width, Math.Max(0, inner.Height - barH));
                if (_tabIndex < Children.Count) Children[_tabIndex].Measure(childAvail);
                return inner; // TabView fills
            }

            default:
                if (_custom is { } r) return r.Measure(RenderCtx(), inner);
                // A platform view is sized by the DSL, not by the control: the real control does not exist
                // until the host places it, and layout has to settle before that. `.Size(w, h)` on MauiView
                // writes the w/h props; without them a platform view fills the width and takes WebView's
                // default height.
                if (PlatformViews.IsRegistered(Type)) return PlatformViewSize(inner);
                return MeasureText("⚠️ " + Type, Font());
        }
    }

    Size MeasureStack(Size inner, bool horizontal)
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
            return new Size(crossV, mainV + gaps);
        }

        // Horizontal: measure everyone at the full offered width first (SwiftUI-ish: each child takes
        // its ideal size).
        var sizes = new Size[count];
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
                    var s = Children[i].Measure(new Size(share, inner.Height));
                    sizes[i] = s;
                    main += s.Width;
                    cross = Math.Max(cross, s.Height);
                }
            }
        }

        _childMeasured.AddRange(sizes);
        return new Size(main, cross);
    }

    // Scrollables measure their content like a VStack but report only the available height
    // (content taller than that scrolls). Section adds a header line.
    Size MeasureScrollable(Size inner)
    {
        var headerH = Type == "Section" && HasProp("header") ? 26f : 0f;
        var spacing = Type == "Section" ? 6f : (Type == "ScrollView" ? 12f : 10f);
        float contentH = headerH, cross = 0;
        var count = 0;
        foreach (var c in Children)
        {
            var s = c.Measure(new Size(inner.Width, inner.Height));
            _childMeasured.Add(s);
            contentH += s.Height;
            cross = Math.Max(cross, s.Width);
            count++;
        }
        if (count > 1) contentH += spacing * (count - 1);
        _naturalHeight = contentH;
        // Section reports its natural height (it lives inside a scrollable). ScrollView/List/Form cap to available.
        if (Type is "Section")
            return new Size(Math.Max(cross, inner.Width), contentH);
        return new Size(inner.Width, Math.Min(contentH, inner.Height));
    }

    float _naturalHeight;

    Size MeasureZ(Size inner)
    {
        float w = 0, h = 0;
        foreach (var c in Children)
        {
            var s = c.Measure(inner);
            _childMeasured.Add(s);
            w = Math.Max(w, s.Width);
            h = Math.Max(h, s.Height);
        }
        return new Size(w, h);
    }

    // ---- Grid ---------------------------------------------------------------
    // Two measure passes, because a cell's width decides how its content wraps: pass 1 takes each child's
    // natural size to size the content-driven (auto/flexible) columns, then pass 2 re-measures every child
    // at its final cell width so Text reports the height it will actually paint at.

    float ColumnGap => (float)(Num("columnSpacing") ?? Num("spacing") ?? 8);
    float RowGap => (float)(Num("rowSpacing") ?? Num("spacing") ?? 8);

    Size MeasureGrid(Size inner)
    {
        var colTracks = GridEngine.ParseTracks(StrOrNull("columnTracks"), (int)(Num("columns") ?? 2));
        var cols = colTracks.Length;

        var requested = new (int?, int?, int, int)[Children.Count];
        for (var i = 0; i < Children.Count; i++) requested[i] = Children[i].GridCellSpec();
        var spans = GridEngine.Place(cols, requested, out var rowCount);
        _gridSpans = spans;

        var colGap = ColumnGap;
        var rowGap = RowGap;

        // pass 1 — natural sizes
        var natural = new Size[Children.Count];
        for (var i = 0; i < Children.Count; i++) natural[i] = Children[i].Measure(inner);
        _gridColW = ResolveTracks(colTracks, inner.Width, colGap, spans, natural, horizontal: true);

        // pass 2 — re-measure inside the resolved cell
        for (var i = 0; i < Children.Count; i++)
        {
            var s = spans[i];
            var cellW = TrackExtent(_gridColW, s.Column, s.ColumnSpan, colGap);
            _childMeasured.Add(Children[i].Measure(new Size(cellW, inner.Height)));
        }

        var rowSpec = StrOrNull("rowTracks");
        // Rows default to Auto (hug their content) rather than Star — a grid should be as tall as it needs.
        var rowTracks = new GridTrack[Math.Max(1, rowCount)];
        var parsedRows = rowSpec is null ? null : GridEngine.ParseTracks(rowSpec, rowCount);
        for (var i = 0; i < rowTracks.Length; i++)
            rowTracks[i] = parsedRows is not null && i < parsedRows.Length ? parsedRows[i] : GridTrack.Auto;

        _gridRowH = ResolveTracks(rowTracks, inner.Height, rowGap, spans, _childMeasured.ToArray(), horizontal: false);

        return new Size(
            Total(_gridColW, colGap),
            Total(_gridRowH, rowGap));
    }

    static float Total(float[] sizes, float gap)
    {
        float total = 0;
        foreach (var s in sizes) total += s;
        return total + gap * Math.Max(0, sizes.Length - 1);
    }

    /// <summary>The span of tracks <c>[start, start+count)</c> including the gaps swallowed between them.</summary>
    static float TrackExtent(float[] sizes, int start, int count, float gap)
    {
        float total = 0;
        for (var i = start; i < start + count && i < sizes.Length; i++) total += sizes[i];
        return total + gap * Math.Max(0, Math.Min(count, sizes.Length - start) - 1);
    }

    /// <summary>
    /// Sizes one axis of tracks: Fixed takes its points, Auto/Flexible take the largest single-track child
    /// in them (Flexible then clamped to its bounds), and Star splits what's left by weight. A child
    /// spanning several tracks doesn't drive Auto sizing directly — instead any shortfall across its span
    /// is added to the last content-sized track it covers, which is what keeps a wide header from
    /// stretching only column 0.
    /// </summary>
    static float[] ResolveTracks(GridTrack[] tracks, float available, float gap, GridSpan[] spans, Size[] sizes, bool horizontal)
    {
        var n = tracks.Length;
        var resolved = new float[n];

        for (var i = 0; i < spans.Length && i < sizes.Length; i++)
        {
            var start = horizontal ? spans[i].Column : spans[i].Row;
            var span = horizontal ? spans[i].ColumnSpan : spans[i].RowSpan;
            if (span != 1 || start >= n) continue;
            if (tracks[start].Kind is GridTrackKind.Auto or GridTrackKind.Flexible)
                resolved[start] = Math.Max(resolved[start], horizontal ? sizes[i].Width : sizes[i].Height);
        }

        float starWeight = 0;
        for (var t = 0; t < n; t++)
        {
            switch (tracks[t].Kind)
            {
                case GridTrackKind.Fixed:
                    resolved[t] = (float)tracks[t].Value;
                    break;
                case GridTrackKind.Flexible:
                    resolved[t] = Math.Max(resolved[t], (float)tracks[t].Value);
                    if (tracks[t].Max is { } max) resolved[t] = Math.Min(resolved[t], (float)max);
                    break;
                case GridTrackKind.Star:
                    starWeight += (float)tracks[t].Value;
                    resolved[t] = 0;
                    break;
            }
        }

        // Spanning children: push any deficit into the last content-sized track they cover.
        for (var i = 0; i < spans.Length && i < sizes.Length; i++)
        {
            var start = horizontal ? spans[i].Column : spans[i].Row;
            var span = horizontal ? spans[i].ColumnSpan : spans[i].RowSpan;
            if (span <= 1 || start >= n) continue;

            // A span that crosses a Star track needs no help — the star pass below already hands it the
            // leftover. Growing a content-sized track here instead would *steal* that leftover, which is
            // exactly what a greedy spanning child (a shape, a raster image) would do to every star column.
            var hasStar = false;
            for (var t = start; t < start + span && t < n; t++)
                if (tracks[t].Kind == GridTrackKind.Star) hasStar = true;
            if (hasStar) continue;

            var want = horizontal ? sizes[i].Width : sizes[i].Height;
            var have = TrackExtent(resolved, start, span, gap);
            if (want <= have) continue;

            var target = -1;
            for (var t = start; t < start + span && t < n; t++)
                if (tracks[t].Kind is GridTrackKind.Auto or GridTrackKind.Flexible) target = t;
            if (target < 0) continue;   // an all-Fixed span is the author's call — don't fight it

            var grown = resolved[target] + (want - have);
            if (tracks[target].Max is { } cap) grown = Math.Min(grown, (float)cap);
            resolved[target] = grown;
        }

        if (starWeight > 0)
        {
            float used = gap * Math.Max(0, n - 1);
            for (var t = 0; t < n; t++)
                if (tracks[t].Kind != GridTrackKind.Star) used += resolved[t];
            var leftover = Math.Max(0, available - used);
            for (var t = 0; t < n; t++)
                if (tracks[t].Kind == GridTrackKind.Star)
                    resolved[t] = leftover * (float)tracks[t].Value / starWeight;
        }

        return resolved;
    }

    // ---- AbsoluteLayout -----------------------------------------------------

    /// <summary>
    /// A canvas: it claims the box it is offered (proportional child bounds have to resolve against
    /// *something*), and each child is measured at whatever size its <c>.LayoutBounds</c> declares —
    /// falling back to its natural size on the axes left auto.
    /// </summary>
    Size MeasureAbsolute(Size inner)
    {
        // Resolve fractions against the box this layout will actually get. A `.Frame(height:)` is applied
        // to the *outer* size after MeasureContent returns, so read it here too — otherwise a child
        // measured at 0.5 of the available height and then arranged at 0.5 of the framed height would
        // wrap its text against the wrong width.
        var (fw, fh) = FrameSize();
        var host = new Size((float)(fw ?? inner.Width), (float)(fh ?? inner.Height));

        foreach (var c in Children)
        {
            var b = c.LayoutBoundsSpec();
            var w = b?.Width is { } dw
                ? (float)((b!.Value.Flags & LayoutFlags.WidthProportional) != 0 ? dw * host.Width : dw)
                : (float?)null;
            var h = b?.Height is { } dh
                ? (float)((b!.Value.Flags & LayoutFlags.HeightProportional) != 0 ? dh * host.Height : dh)
                : (float?)null;

            var natural = c.Measure(new Size(w ?? host.Width, h ?? host.Height));
            _childMeasured.Add(new Size(w ?? natural.Width, h ?? natural.Height));
        }
        return host;
    }

    void ArrangeAbsolute()
    {
        for (var i = 0; i < Children.Count; i++)
        {
            var m = _childMeasured[i];
            var b = Children[i].LayoutBoundsSpec();
            // A child that never declared bounds sits at the origin at its natural size, so a forgotten
            // .LayoutBounds is visible rather than invisible.
            var (x, y, w, h) = b is { } spec
                ? AbsoluteLayoutBounds.Resolve(spec.X, spec.Y, spec.Width, spec.Height, spec.Flags,
                    _content.Width, _content.Height, m.Width, m.Height)
                : (0, 0, m.Width, m.Height);

            var left = _content.Left + (float)x;
            var top = _content.Top + (float)y;
            Children[i].Arrange(new Rect(left, top, left + (float)w, top + (float)h));
        }
    }

    Size MeasureFill(Size inner)
    {
        if (Children.Count > 0)
        {
            var s = Children[0].Measure(inner);
            _childMeasured.Add(s);
        }
        return inner;
    }

    Size MeasureNavLink(Size inner)
    {
        // child 0 = label (shown as a row); child 1 = destination (measured only when pushed)
        var label = Children.Count > 0 ? Children[0].Measure(inner) : new Size(0, 0);
        _childMeasured.Add(label);
        return new Size(inner.Width, Math.Max(label.Height, 22) + 8);
    }

    Size MeasureDisclosure(Size inner)
    {
        var h = 40f; // header row
        if (Bool("expanded"))
            foreach (var c in Children)
            {
                var s = c.Measure(inner);
                h += s.Height + 6;
            }
        return new Size(inner.Width, h);
    }

    // ---- Arrange -------------------------------------------------------------

    public void Arrange(Rect rect)
    {
        Frame = rect;
        var pad = Padding();
        _content = new Rect(rect.Left + pad.Left, rect.Top + pad.Top, rect.Right - pad.Right, rect.Bottom - pad.Bottom);

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
            case "AbsoluteLayout":
                ArrangeAbsolute();
                break;
            case "Tab":
                if (Children.Count > 0) Children[0].Arrange(_content);
                break;
            case "NavigationStack":
                if (Children.Count > 0)
                    Children[0].Arrange(new Rect(_content.Left, _content.Top + NavBarHeight, _content.Right, _content.Bottom));
                break;
            case "NavigationLink":
                if (Children.Count > 0) Children[0].Arrange(new Rect(_content.Left, _content.Top, _content.Right - 20, _content.Bottom));
                break;
            case "Sheet" or "Alert" or "ActionSheet":
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
                child.Arrange(new Rect(cursor, y, cursor + cw, y + m.Height));
                cursor += cw;
            }
            else
            {
                var ch = child.Type == "Spacer" ? spacerEach : m.Height;
                var x = CrossPos(_content.Left, _content.Width, m.Width, CrossToken(), vertical: false);
                child.Arrange(new Rect(x, cursor, x + m.Width, cursor + ch));
                cursor += ch;
            }
        }
    }

    bool IsGridList => Type == "List" && Str("layout") == "grid";

    // A grid List measures uniform cells (like Grid) but reports only the available height and remembers
    // the natural height so it can scroll vertically.
    Size MeasureScrollableGrid(Size inner)
    {
        var cols = Math.Max(1, (int)(Num("columns") ?? 2));
        var spacing = (float)(Num("spacing") ?? 8);
        float cellW = 0, cellH = 0;
        var cellAvail = new Size(Math.Max(0, (inner.Width - (cols - 1) * spacing) / cols), inner.Height);
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
        return new Size(inner.Width, Math.Min(_naturalHeight, inner.Height));
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
            Children[i].Arrange(new Rect(x, y, x + _gridCellW, y + _gridCellH));
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
            Children[i].Arrange(new Rect(x, y, x + cw, y + m.Height));
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
            Children[i].Arrange(new Rect(x, y, x + cw, y + m.Height));
        }
    }

    void ArrangeGrid()
    {
        if (_gridSpans is null || _gridColW is null || _gridRowH is null) return;

        var colGap = ColumnGap;
        var rowGap = RowGap;
        // The Grid's `alignment` prop places each child inside its cell; null centers, as SwiftUI does.
        var token = Props.GetValueOrDefault("alignment") as string;

        for (var i = 0; i < Children.Count && i < _gridSpans.Length; i++)
        {
            var s = _gridSpans[i];
            var cellX = _content.Left + TrackExtent(_gridColW, 0, s.Column, colGap) + (s.Column > 0 ? colGap : 0);
            var cellY = _content.Top + TrackExtent(_gridRowH, 0, s.Row, rowGap) + (s.Row > 0 ? rowGap : 0);
            var cellW = TrackExtent(_gridColW, s.Column, s.ColumnSpan, colGap);
            var cellH = TrackExtent(_gridRowH, s.Row, s.RowSpan, rowGap);

            var m = _childMeasured[i];
            var w = Math.Min(m.Width, cellW);
            var h = Math.Min(m.Height, cellH);
            var x = CrossPos(cellX, cellW, w, token, vertical: false);
            var y = CrossPos(cellY, cellH, h, token, vertical: true);
            Children[i].Arrange(new Rect(x, y, x + w, y + h));
        }
    }

    /// <summary>This node's <c>gridCell</c> modifier, as the tuple <see cref="GridEngine.Place"/> wants.</summary>
    (int? Column, int? Row, int ColumnSpan, int RowSpan) GridCellSpec()
    {
        var m = Mod("gridCell");
        if (m is null) return (null, null, 1, 1);
        return (
            MNull(m, "column") is { } c ? (int)c : null,
            MNull(m, "row") is { } r ? (int)r : null,
            MNull(m, "columnSpan") is { } cs ? Math.Max(1, (int)cs) : 1,
            MNull(m, "rowSpan") is { } rs ? Math.Max(1, (int)rs) : 1);
    }

    /// <summary>This node's <c>layoutBounds</c> modifier, or null when it never declared one.</summary>
    (double X, double Y, double? Width, double? Height, LayoutFlags Flags)? LayoutBoundsSpec()
    {
        var m = Mod("layoutBounds");
        if (m is null) return null;
        return (
            MNull(m, "x") ?? 0,
            MNull(m, "y") ?? 0,
            MNull(m, "width"),
            MNull(m, "height"),
            AbsoluteLayoutBounds.Parse(m.GetValueOrDefault("flags") as string));
    }

    void ArrangeTabView()
    {
        if (Paged)
        {
            // carousel: selected page fills, minus a dot strip at the bottom
            var pageRect = new Rect(_content.Left, _content.Top, _content.Right, _content.Bottom - 28);
            if (_tabIndex < Children.Count) Children[_tabIndex].Arrange(pageRect);
        }
        else
        {
            var barTop = _content.Bottom - TabBarHeight;
            var contentRect = new Rect(_content.Left, _content.Top, _content.Right, barTop);
            if (_tabIndex < Children.Count) Children[_tabIndex].Arrange(contentRect); // selected Tab
        }
    }

    void ArrangeDisclosure()
    {
        if (!Bool("expanded")) return;
        var y = _content.Top + 40;
        foreach (var c in Children)
        {
            var s = c.Measure(new Size(_content.Width - 12, _content.Height));
            c.Arrange(new Rect(_content.Left + 12, y, _content.Right, y + s.Height));
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

    public bool HitTest(Point p)
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

    void ControlTap(Point p)
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
                // Open the swatch popover (engine-local, like a Menu) rather than blind-cycling the
                // palette — you have to be able to *choose* a colour, not just step past it.
                MenuOpen = !MenuOpen;
                break;
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
    public bool DispatchGesture(Point p, string modType, string? direction)
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
    internal VisualNode? NodeWithModAt(Point p, string modType)
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

    /// <summary>
    /// Find the innermost <em>continuously</em> scrubbable control under a point — today just
    /// <c>Slider</c>, the one built-in whose value is a position rather than a step. Discrete controls
    /// (Stepper/Picker/DatePicker/ColorPicker) deliberately do not qualify: they advance once per tap, so
    /// letting a finger drag them would fire them once per pointer-move event.
    /// </summary>
    internal VisualNode? ScrubbableAt(Point p)
    {
        if (!Frame.Contains(p)) return null;
        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Type == "TabView" && i != _tabIndex) continue;
            if (Children[i].ScrubbableAt(p) is { } inner) return inner;
        }
        return Type is "Slider" && !IsDisabled ? this : null;
    }

    /// <summary>Set a scrubbable control's value from a pointer position (the same math a tap uses).</summary>
    internal void ScrubTo(Point p) => ControlTap(p);

    /// <summary>Children that are actually on screen (a TabView shows only its selected tab) — for the overlay walk.</summary>
    internal IEnumerable<VisualNode> VisibleOverlayChildren()
    {
        if (Type == "TabView")
            return _tabIndex < Children.Count ? new[] { Children[_tabIndex] } : Array.Empty<VisualNode>();
        return Children;
    }

    /// <summary>Find the innermost scrollable node under a point (for wheel/drag scrolling).</summary>
    public VisualNode? ScrollableAt(Point p)
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

    bool FillsWidth => Type is "AbsoluteLayout"
        or "Divider" or "ProgressView" or "Gauge" or "WebView"
        or "TextField" or "SecureField" or "TextEditor"
        or "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu"
        or "DisclosureGroup" or "NavigationLink"
        // A platform view with no explicit width is greedy, like the WebView it generalises.
        || (Num("w") is not > 0 && PlatformViews.IsRegistered(Type));

    static bool IsBuiltIn(string type) => type is
        "Text" or "Button" or "Link" or "Image" or "Label" or "Divider" or "Spacer"
        or "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle"
        or "ProgressView" or "Gauge" or "WebView"
        or "TextField" or "SecureField" or "TextEditor"
        or "Toggle" or "Slider" or "Stepper" or "Picker" or "DatePicker" or "ColorPicker" or "Menu"
        or "DisclosureGroup" or "HStack" or "VStack" or "Group"
        or "ScrollView" or "List" or "Form" or "Section" or "ZStack" or "Grid" or "AbsoluteLayout"
        or "Tab" or "TabView" or "NavigationStack" or "NavigationLink"
        or "Sheet" or "Alert" or "ActionSheet";

    /// <summary>
    /// True when this node is drawn by a real OS control floated above the canvas rather than painted.
    /// Requires both that the type is registered (<see cref="PlatformViews"/>) <em>and</em> that the bridge
    /// has a host able to place one — a headless or game-engine host keeps the painted placeholder.
    /// </summary>
    internal bool IsPlatformView => _bridge.PlatformViewHost is not null && PlatformViews.IsRegistered(Type);

    /// <summary>Layout size of a platform view: the declared <c>w</c>/<c>h</c> props, else fill × 120.</summary>
    Size PlatformViewSize(Size inner) => new(
        Num("w") is { } w and > 0 ? (float)w : inner.Width,
        Num("h") is { } h and > 0 ? (float)h : 120);

    /// <summary>The rasterizer's font provider, reached through the bridge that owns this tree.</summary>
    IFontProvider Fonts => _bridge.Fonts;

    Font Font() => Theme.MakeFont(Mod("font")?.GetValueOrDefault("value") as string, Fonts);

    /// <summary>
    /// The face used for SF-Symbol stand-in glyphs. Deliberately the default face rather than the node's
    /// font: the stand-ins are emoji, and a bold/heavy text face often lacks them.
    /// </summary>
    Font IconFont(float size) => Fonts.Get(size, bold: false);

    string? AlignToken() => Mod("align")?.GetValueOrDefault("value") as string
        ?? (Mod("frame")?.GetValueOrDefault("alignment") as string);

    Color ForegroundColor(bool dark) => Theme.Color(Mod("foregroundColor")?.GetValueOrDefault("value") as string, dark);
    Color? ForegroundColorOptional(bool dark) =>
        Mod("foregroundColor")?.GetValueOrDefault("value") is string t ? Theme.Color(t, dark) : null;
    Color? BackgroundColor(bool dark) =>
        Mod("background")?.GetValueOrDefault("value") is string t ? Theme.Color(t, dark) : null;

    float RawOpacity => (float)(MNull(Mod("opacity"), "amount") ?? 1);

    // A keyframe track carries an *absolute* value, so where one exists it replaces the static modifier
    // rather than scaling it — that is what makes `.Track(Prop.Opacity, …)` predictable next to a plain
    // `.Opacity()`. The pulse (AnimO) still applies to nodes with no opacity track.
    float Opacity() => Kf("opacity") is { } o ? (float)o : AnimO;
    bool IsDisabled => Mod("disabled")?.GetValueOrDefault("value") as string == "true";
    (double x, double y, string anchor)? Scale()
    {
        var m = Mod("scaleEffect");
        var uniform = Kf("scale");
        var kx = Kf("scaleX") ?? uniform;
        var ky = Kf("scaleY") ?? uniform;
        if (m is null && kx is null && ky is null) return null;
        var anchor = m?.GetValueOrDefault("value") as string ?? "center";
        return (kx ?? MNull(m, "x") ?? 1, ky ?? MNull(m, "y") ?? 1, anchor);
    }

    // F4 transforms: translation (no layout effect) and rotation around an anchor.
    (double x, double y)? Offset()
    {
        var m = Mod("offset");
        var kx = Kf("offsetX");
        var ky = Kf("offsetY");
        if (m is null && kx is null && ky is null) return null;
        return (kx ?? MNull(m, "x") ?? 0, ky ?? MNull(m, "y") ?? 0);
    }

    (double degrees, string anchor)? Rotation()
    {
        var m = Mod("rotation");
        var kr = Kf("rotation");
        if (m is null && kr is null) return null;
        return (kr ?? MNull(m, "degrees") ?? 0, m?.GetValueOrDefault("value") as string ?? "center");
    }

    // F5 gradient background: painted in place of the flat background fill when present.
    internal Gradient? BackgroundGradient(bool dark)
    {
        if (Mod("background")?.GetValueOrDefault("gradient") is not string spec) return null;
        return Gradient.Parse(spec, Frame, dark);
    }

    // F3 raster: decode once and cache (keyed by the source string) so paint doesn't decode per frame.
    // bytes/file decode synchronously; url goes through ImageLoader's async cache and paints as soon
    // as the fetch lands (the loader invalidates the bridge, which schedules the repaint).
    IImage? _rasterImage;
    string? _rasterKey;
    internal IImage? RasterImage()
    {
        if (HasProp("url")) return ImageLoader.Get(Str("url"), _bridge.Images, _bridge.RequestRepaint);

        var src = HasProp("bytes") ? "b:" + Str("bytes")
                : HasProp("file") ? "f:" + Str("file")
                : null;
        if (src is null) return null;
        if (src == _rasterKey) return _rasterImage;
        _rasterKey = src;
        (_rasterImage as IDisposable)?.Dispose();
        try
        {
            _rasterImage = src[0] == 'b'
                ? _bridge.Images.Decode(Convert.FromBase64String(Str("bytes")))
                : _bridge.Images.DecodeFile(Str("file"));
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

    (Color color, double width)? Border(bool dark)
    {
        var m = Mod("border");
        if (m?.GetValueOrDefault("color") is string c) return (Theme.Color(c, dark), MNull(m, "width") ?? 1);
        return null;
    }

    /// <summary>The <c>.Shadow()</c> modifier as a paint-ready spec, or null when the node casts none.</summary>
    Shadow? DropShadow()
    {
        var m = Mod("shadow");
        if (m is null) return null;
        var col = m.GetValueOrDefault("color") is string c ? Theme.Color(c, false) : new Color(0, 0, 0, 90);
        return new Shadow(
            (float)(MNull(m, "x") ?? 0),
            (float)(MNull(m, "y") ?? 0),
            (float)(MNull(m, "radius") ?? 4),
            col);
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

    // A width/height track drives layout, so unlike the transforms it has to land here rather than in the
    // paint pass — Measure reads this and the arranged rect follows.
    (double? w, double? h) FrameSize()
    {
        var m = Mod("frame");
        var kw = Kf("width");
        var kh = Kf("height");
        if (m is null) return (kw, kh);
        return (kw ?? MNull(m, "width"), kh ?? MNull(m, "height"));
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

    VisualRenderContext RenderCtx() => new(Id, Props, _bridge.Emit);

    Dictionary<string, object?>? Mod(string type)
    {
        foreach (var m in Modifiers)
            if (m.GetValueOrDefault("type") as string == type) return m;
        return null;
    }

    internal string TextProp() => Str("text");
    bool HasProp(string key) => Props.ContainsKey(key);
    string Str(string key) => Props.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
    string? StrOrNull(string key) => Props.TryGetValue(key, out var v) ? v as string : null;
    double? Num(string key) => Props.TryGetValue(key, out var v) && v is double d ? d : null;
    bool Bool(string key) => Props.TryGetValue(key, out var v) && v is bool b && b;

    static double? MNull(Dictionary<string, object?>? m, string key) =>
        m is not null && m.TryGetValue(key, out var v) && v is double d ? d : null;

    Size MeasureText(string text, Font font) => string.IsNullOrEmpty(text)
        ? new Size(0, font.Metrics.LineHeight)
        : TextLayout.MeasureLine(text, font, Fonts);

    // Wraps `text` to `maxWidth`, caching the broken lines for the paint pass. Returns the block size.
    List<string>? _wrapLines;
    Size MeasureWrapped(string text, Font font, float maxWidth)
    {
        _wrapLines = TextLayout.Wrap(text, font, maxWidth, Fonts);
        var w = 0f;
        foreach (var line in _wrapLines) w = Math.Max(w, Fonts.Measure(line, font));
        return new Size(w, font.Metrics.LineHeight * _wrapLines.Count);
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
