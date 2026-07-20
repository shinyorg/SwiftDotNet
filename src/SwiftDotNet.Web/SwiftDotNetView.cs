using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace SwiftDotNet;

/// <summary>
/// Hosts a SwiftDotNet view hierarchy in Blazor. Renders the node tree to HTML via
/// <see cref="RenderTreeBuilder"/>; DOM events call back into C# → State → re-render.
/// <code>&lt;SwiftDotNetView Root="@(new ContentView())" /&gt;</code>
/// </summary>
public sealed class SwiftDotNetView : ComponentBase, IDisposable
{
    [Parameter] public View Root { get; set; } = default!;

    readonly WebBridge _bridge = new();
    readonly Dictionary<string, int> _tab = new();          // TabView selected index by node id
    readonly Dictionary<string, List<WebNode>> _nav = new(); // NavigationStack pushed screens by node id
    readonly Dictionary<string, CancellationTokenSource> _longPress = new(); // in-flight long-press timers by event id
    readonly Dictionary<string, (double X, double Y)> _swipeStart = new();    // pointer-down origin by event id
    readonly Dictionary<string, (double X, double Y)> _dragStart = new();      // F1 continuous-drag origin by event id
    readonly Dictionary<string, Dictionary<long, (double X, double Y)>> _pinchPoints = new(); // live pinch pointers by event id
    readonly Dictionary<string, double> _pinchStart = new();                   // two-finger spread at pinch start by event id
    readonly Dictionary<string, double> _wheelScale = new();                   // ctrl+wheel (trackpad pinch) factor by event id
    int _seq;

    protected override void OnInitialized()
    {
        _bridge.Changed += OnChanged;
        SwiftApp.Run(Root, _bridge);
    }

    void OnChanged() => InvokeAsync(StateHasChanged);
    public void Dispose() => _bridge.Changed -= OnChanged;

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        _seq = 0;
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "font-family:-apple-system,system-ui,sans-serif;min-height:100vh;");
        // Generic repeating-animation keyframes (F4): a subtle opacity pulse used by shimmer/pulse/spinner
        // effects declared via `.Animation(spec.Repeating(), …)`. Shipped by the library so any consumer gets it.
        b.OpenElement(_seq++, "style");
        b.AddMarkupContent(_seq++, "@keyframes sdn-pulse{from{opacity:1}to{opacity:.4}}");
        b.CloseElement();
        if (_bridge.Root is { } root) RenderNode(b, root);
        b.CloseElement();
    }

    void RenderNode(RenderTreeBuilder b, WebNode n)
    {
        switch (n.Type)
        {
            case "Text": Leaf(b, n, "span", n.S("text")); break;
            case "Image": ImageNode(b, n); break;
            case "Divider": Element(b, n, "hr", "border:none;border-top:1px solid #ccc;width:100%;", null); break;
            case "Spacer": Element(b, n, "div", "flex:1;", null); break;

            case "Button": Clickable(b, n, "button", n.S("title"), () => _bridge.Emit(n.Id, null),
                "cursor:pointer;padding:8px 16px;border-radius:8px;border:1px solid #c7c7cc;background:#f2f2f7;align-self:center;"); break;
            case "Link": Anchor(b, n); break;

            case "VStack": Stack(b, n, "column", n.S("alignment")); break;
            case "HStack": Stack(b, n, "row", n.S("alignment")); break;
            case "ZStack": ZStack(b, n); break;
            case "ScrollView": Container(b, n, "display:flex;flex-direction:column;gap:12px;align-items:center;overflow:auto;"); break;
            case "Grid": Grid(b, n); break;
            case "List": ListBox(b, n); break;
            case "Form": Container(b, n, "display:flex;flex-direction:column;gap:16px;"); break;
            case "Section": Section(b, n); break;
            case "Group": Container(b, n, "display:flex;flex-direction:column;"); break;
            case "DisclosureGroup": Disclosure(b, n); break;
            case "TabView": TabView(b, n); break;
            case "Tab": if (n.Children.Count > 0) RenderNode(b, n.Children[0]); break;
            case "Menu": Menu(b, n); break;

            case "TextField": Input(b, n, "text", n.S("text"), n.S("placeholder")); break;
            case "SecureField": Input(b, n, "password", n.S("text"), n.S("placeholder")); break;
            case "TextEditor": TextArea(b, n); break;
            case "Toggle": Toggle(b, n); break;
            case "Slider": Range(b, n); break;
            case "Stepper": Stepper(b, n); break;
            case "Picker": Picker(b, n); break;
            case "DatePicker": DatePicker(b, n); break;
            case "ColorPicker": ColorPicker(b, n); break;

            case "NavigationStack": NavigationStack(b, n); break;
            case "NavigationLink": NavigationLink(b, n); break;
            case "Sheet": Overlay(b, n, isSheet: true); break;
            case "Alert": Overlay(b, n, isSheet: false); break;

            case "WebView": WebFrame(b, n); break;
            case "Label": Label(b, n); break;
            case "ProgressView": Progress(b, n); break;
            case "Gauge": Gauge(b, n); break;

            case "Rectangle": Shape(b, n, "0"); break;
            case "Circle": Shape(b, n, "50%"); break;
            case "Capsule": Shape(b, n, "9999px"); break;
            case "RoundedRectangle": Shape(b, n, (n.N("cornerRadius") ?? 8).ToString(CultureInfo.InvariantCulture) + "px"); break;

            default: Custom(b, n); break;
        }
    }

    // ---- primitives ----------------------------------------------------------

    void Leaf(RenderTreeBuilder b, WebNode n, string tag, string text, string? baseStyle = null)
    {
        b.OpenElement(_seq++, tag);
        Style(b, n, baseStyle);
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void Element(RenderTreeBuilder b, WebNode n, string tag, string baseStyle, string? _)
    {
        b.OpenElement(_seq++, tag);
        Style(b, n, baseStyle);
        b.CloseElement();
    }

    void Clickable(RenderTreeBuilder b, WebNode n, string tag, string text, Action onClick, string baseStyle)
    {
        b.OpenElement(_seq++, tag);
        Style(b, n, baseStyle);
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, onClick));
        b.AddContent(_seq++, text);
        b.CloseElement();
    }

    void Anchor(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "a");
        Style(b, n, "color:#007AFF;");
        b.AddAttribute(_seq++, "href", n.S("url"));
        b.AddAttribute(_seq++, "target", "_blank");
        b.AddContent(_seq++, n.S("title"));
        b.CloseElement();
    }

    // ---- layout --------------------------------------------------------------

    void Stack(RenderTreeBuilder b, WebNode n, string dir, string? alignToken)
    {
        var gap = n.N("spacing") ?? 0;
        var align = alignToken switch { "leading" or "top" => "flex-start", "trailing" or "bottom" => "flex-end", _ => "center" };
        var style = $"display:flex;flex-direction:{dir};gap:{gap.ToString(CultureInfo.InvariantCulture)}px;align-items:{align};";
        Container(b, n, style);
    }

    void Container(RenderTreeBuilder b, WebNode n, string baseStyle)
    {
        b.OpenElement(_seq++, "div");
        Style(b, n, baseStyle);
        foreach (var c in n.Children) RenderNode(b, c);
        b.CloseElement();
    }

    void ZStack(RenderTreeBuilder b, WebNode n)
    {
        // The `alignment` prop (Alignment.Token()) positions every layer inside the stack, like SwiftUI's
        // ZStack(alignment:). Layers are stretched to the stack's grid cell rather than shrink-wrapped so a
        // *nested* ZStack — how Overlay/OverlayHost lowers Top/Bottom positioning — still has room to align
        // within. Re-read on every render, so a prop update repositions the layers.
        var (justify, align) = ZAlign(n.S("alignment"));
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:grid;");
        var layer = $"grid-area:1/1;display:flex;justify-content:{justify};align-items:{align};";
        foreach (var c in n.Children)
        {
            b.OpenElement(_seq++, "div");
            b.AddAttribute(_seq++, "style", layer);
            RenderNode(b, c);
            b.CloseElement();
        }
        b.CloseElement();
    }

    // ZStack alignment token → the flex (justify-content, align-items) pair for a layer.
    static (string Justify, string Align) ZAlign(string token) => token switch
    {
        "topLeading" => ("flex-start", "flex-start"),
        "top" => ("center", "flex-start"),
        "topTrailing" => ("flex-end", "flex-start"),
        "leading" => ("flex-start", "center"),
        "trailing" => ("flex-end", "center"),
        "bottomLeading" => ("flex-start", "flex-end"),
        "bottom" => ("center", "flex-end"),
        "bottomTrailing" => ("flex-end", "flex-end"),
        _ => ("center", "center"),
    };

    void Grid(RenderTreeBuilder b, WebNode n)
    {
        var cols = (int)(n.N("columns") ?? 2);
        var gap = n.N("spacing") ?? 8;
        Container(b, n, $"display:grid;grid-template-columns:repeat({cols},1fr);gap:{gap.ToString(CultureInfo.InvariantCulture)}px;justify-items:center;");
    }

    void ListBox(RenderTreeBuilder b, WebNode n)
    {
        // Grid / horizontal layout: lay rows out with CSS grid or a horizontal flex row.
        var layout = n.S("layout");
        var horizontal = n.S("axis") == "horizontal";
        if (layout == "grid" || horizontal)
        {
            var cols = (int)(n.N("columns") ?? 2);
            var container = layout == "grid"
                ? $"display:grid;grid-template-columns:repeat({cols},1fr);gap:8px;"
                : "display:flex;flex-direction:row;gap:8px;overflow-x:auto;";
            b.OpenElement(_seq++, "div");
            Style(b, n, container);
            foreach (var child in n.Children)
            {
                b.OpenElement(_seq++, "div");
                if (child.Props.GetValueOrDefault("key") is string gk) b.SetKey(gk);
                RenderNode(b, child);
                b.CloseElement();
            }
            b.CloseElement();
            return;
        }

        var selectable = n.S("selectionMode").Length > 0;
        b.OpenElement(_seq++, "div");
        Style(b, n, "border:1px solid #ddd;border-radius:10px;overflow:hidden;");
        for (var i = 0; i < n.Children.Count; i++)
        {
            var child = n.Children[i];
            var key = child.Props.GetValueOrDefault("key") as string;
            b.OpenElement(_seq++, "div");
            // Keyed rows carry a stable "key"; hand it to Blazor's diff so the row's DOM node (and its
            // input focus / scroll state) is preserved and merely moved across inserts and reorders.
            if (key is not null) b.SetKey(key);
            var bg = child.B("selected") ? "background:rgba(0,122,255,0.15);" : "";
            var cursor = selectable && key is not null ? "cursor:pointer;" : "";
            b.AddAttribute(_seq++, "style", "padding:12px 16px;" + cursor + bg + (i < n.Children.Count - 1 ? "border-bottom:1px solid #eee;" : ""));
            if (selectable && key is not null)
                b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () => _bridge.Emit(n.Id, key)));
            RenderNode(b, child);
            b.CloseElement();
        }
        b.CloseElement();
    }

    void Section(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;gap:6px;");
        if (n.Props.GetValueOrDefault("header") is string header)
        {
            b.OpenElement(_seq++, "div");
            b.AddAttribute(_seq++, "style", "font-weight:600;");
            b.AddContent(_seq++, header);
            b.CloseElement();
        }
        foreach (var c in n.Children) RenderNode(b, c);
        b.CloseElement();
    }

    void Disclosure(RenderTreeBuilder b, WebNode n)
    {
        var open = n.B("expanded");
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;gap:4px;width:100%;");
        Clickable(b, n, "button", (open ? "▾ " : "▸ ") + n.S("label"),
            () => _bridge.Emit(n.Id, open ? "false" : "true"),
            "cursor:pointer;text-align:left;background:none;border:none;font-size:16px;padding:6px 0;");
        if (open)
            foreach (var c in n.Children) RenderNode(b, c);
        b.CloseElement();
    }

    void TabView(RenderTreeBuilder b, WebNode n)
    {
        // When SelectedIndex is bound, C# state drives the selection (and clicks emit back); otherwise the
        // selection is engine-local in _tab.
        var bound = n.Props.ContainsKey("selectedIndex");
        var selected = bound ? (int)(n.N("selectedIndex") ?? 0) : _tab.GetValueOrDefault(n.Id);
        if (selected >= n.Children.Count || selected < 0) selected = 0;

        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;");
        // tab bar
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "display:flex;gap:8px;justify-content:center;padding:8px;border-bottom:1px solid #ddd;flex-wrap:wrap;");
        for (var i = 0; i < n.Children.Count; i++)
        {
            var tab = n.Children[i];
            var idx = i;
            b.OpenElement(_seq++, "button");
            b.AddAttribute(_seq++, "style", "cursor:pointer;padding:6px 14px;border-radius:14px;border:none;" +
                (i == selected ? "background:#007AFF;color:#fff;" : "background:transparent;"));
            b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () =>
            {
                if (bound) _bridge.Emit(n.Id, idx.ToString());
                else { _tab[n.Id] = idx; StateHasChanged(); }
            }));
            b.AddContent(_seq++, WebStyle.Emoji(tab.S("systemImage")) + " " + tab.S("title"));
            b.CloseElement();
        }
        b.CloseElement();
        // content
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "padding:16px;");
        if (selected < n.Children.Count) RenderNode(b, n.Children[selected]);
        b.CloseElement();
        b.CloseElement();
    }

    void Menu(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "details");
        Style(b, n, null);
        b.OpenElement(_seq++, "summary");
        b.AddAttribute(_seq++, "style", "cursor:pointer;color:#007AFF;");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        foreach (var c in n.Children)
        {
            var childId = c.Id;
            Clickable(b, c, "button", c.S("title"), () => _bridge.Emit(childId, null),
                "display:block;width:100%;text-align:left;padding:6px;background:none;border:none;cursor:pointer;");
        }
        b.CloseElement();
    }

    // ---- controls ------------------------------------------------------------

    void Input(RenderTreeBuilder b, WebNode n, string type, string value, string placeholder)
    {
        b.OpenElement(_seq++, "input");
        Style(b, n, "padding:8px 12px;border:1px solid #c7c7cc;border-radius:8px;width:100%;box-sizing:border-box;");
        // F9: keyboard type → HTML input type / inputmode; maxLength → the native attribute.
        var (htmlType, inputMode) = KeyboardFor(n.S("keyboard"), type);
        b.AddAttribute(_seq++, "type", htmlType);
        if (inputMode is not null) b.AddAttribute(_seq++, "inputmode", inputMode);
        if (n.N("maxLength") is { } max) b.AddAttribute(_seq++, "maxlength", ((int)max).ToString(CultureInfo.InvariantCulture));
        if (n.S("returnKey") is { Length: > 0 } rk) b.AddAttribute(_seq++, "enterkeyhint", rk);
        b.AddAttribute(_seq++, "value", value);
        b.AddAttribute(_seq++, "placeholder", placeholder);
        b.AddAttribute(_seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        b.CloseElement();
    }

    static (string type, string? inputMode) KeyboardFor(string keyboard, string fallbackType) => keyboard switch
    {
        "number" => ("text", "numeric"),
        "decimal" => ("text", "decimal"),
        "email" => ("email", null),
        "phone" => ("tel", null),
        "url" => ("url", null),
        _ => (fallbackType, null),
    };

    void TextArea(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "textarea");
        Style(b, n, "padding:8px;border:1px solid #c7c7cc;border-radius:8px;width:100%;min-height:100px;box-sizing:border-box;");
        b.AddAttribute(_seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        b.AddContent(_seq++, n.S("text"));
        b.CloseElement();
    }

    void Toggle(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "label");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;cursor:pointer;");
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "checkbox");
        if (n.B("value")) b.AddAttribute(_seq++, "checked", true);
        b.AddAttribute(_seq++, "style", "width:20px;height:20px;");
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value is bool bb && bb ? "true" : "false")));
        b.CloseElement();
        b.CloseElement();
    }

    void Range(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "input");
        Style(b, n, "width:100%;");
        b.AddAttribute(_seq++, "type", "range");
        b.AddAttribute(_seq++, "min", (n.N("min") ?? 0).ToString(CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "max", (n.N("max") ?? 1).ToString(CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "step", "0.01");
        b.AddAttribute(_seq++, "value", (n.N("value") ?? 0).ToString(CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        b.CloseElement();
    }

    void Stepper(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "label");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;gap:8px;");
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "number");
        b.AddAttribute(_seq++, "value", ((int)(n.N("value") ?? 0)).ToString(CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "style", "width:80px;padding:6px;");
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        b.CloseElement();
        b.CloseElement();
    }

    void Picker(RenderTreeBuilder b, WebNode n)
    {
        var selected = (int)(n.N("selection") ?? 0);
        b.OpenElement(_seq++, "label");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;gap:8px;");
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        b.OpenElement(_seq++, "select");
        b.AddAttribute(_seq++, "style", "padding:6px;");
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        for (var i = 0; i < n.Children.Count; i++)
        {
            b.OpenElement(_seq++, "option");
            b.AddAttribute(_seq++, "value", i.ToString());
            if (i == selected) b.AddAttribute(_seq++, "selected", true);
            b.AddContent(_seq++, n.Children[i].S("text"));
            b.CloseElement();
        }
        b.CloseElement();
        b.CloseElement();
    }

    void DatePicker(RenderTreeBuilder b, WebNode n)
    {
        var date = DateTimeOffset.FromUnixTimeSeconds((long)(n.N("value") ?? 0)).LocalDateTime;
        b.OpenElement(_seq++, "label");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;gap:8px;");
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "date");
        b.AddAttribute(_seq++, "value", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e =>
        {
            if (DateTime.TryParse(e.Value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
                _bridge.Emit(n.Id, new DateTimeOffset(d).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }));
        b.CloseElement();
        b.CloseElement();
    }

    void ColorPicker(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "label");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;gap:8px;");
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, n.S("label"));
        b.CloseElement();
        b.OpenElement(_seq++, "input");
        b.AddAttribute(_seq++, "type", "color");
        b.AddAttribute(_seq++, "value", WebStyle.Color(n.S("value")) ?? "#000000");
        b.AddAttribute(_seq++, "oninput", EventCallback.Factory.Create<ChangeEventArgs>(this, e => _bridge.Emit(n.Id, e.Value?.ToString())));
        b.CloseElement();
        b.CloseElement();
    }

    // ---- navigation & presentation -------------------------------------------

    void NavigationStack(RenderTreeBuilder b, WebNode n)
    {
        var stack = _nav.TryGetValue(n.Id, out var s) ? s : (_nav[n.Id] = new List<WebNode>());
        var current = stack.Count > 0 ? stack[^1] : (n.Children.Count > 0 ? n.Children[0] : null);
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;");
        // header
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "display:flex;align-items:center;gap:8px;padding:8px;border-bottom:1px solid #eee;");
        if (stack.Count > 0)
        {
            b.OpenElement(_seq++, "button");
            b.AddAttribute(_seq++, "style", "cursor:pointer;background:none;border:none;color:#007AFF;");
            b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () => { stack.RemoveAt(stack.Count - 1); StateHasChanged(); }));
            b.AddContent(_seq++, "‹ Back");
            b.CloseElement();
        }
        b.OpenElement(_seq++, "b");
        b.AddContent(_seq++, current is null ? "" : TitleOf(current));
        b.CloseElement();
        b.CloseElement();
        // content
        if (current is not null) RenderNode(b, current);
        b.CloseElement();
    }

    void NavigationLink(RenderTreeBuilder b, WebNode n)
    {
        WebNode? destination = n.Children.Count > 1 ? n.Children[1] : null;
        b.OpenElement(_seq++, "button");
        Style(b, n, "display:flex;justify-content:space-between;align-items:center;width:100%;cursor:pointer;background:none;border:none;padding:8px 0;font-size:16px;");
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () =>
        {
            if (destination is not null) PushNearestNav(destination);
        }));
        if (n.Children.Count > 0) RenderNode(b, n.Children[0]);
        b.OpenElement(_seq++, "span");
        b.AddContent(_seq++, "›");
        b.CloseElement();
        b.CloseElement();
    }

    void PushNearestNav(WebNode destination)
    {
        // The most-recently rendered NavigationStack owns the newest _nav list; push there.
        if (_nav.Count == 0) return;
        var key = _nav.Keys.OrderByDescending(k => k.Length).First();
        _nav[key].Add(destination);
        StateHasChanged();
    }

    void Overlay(RenderTreeBuilder b, WebNode n, bool isSheet)
    {
        // body (child 0)
        if (n.Children.Count > 0) RenderNode(b, n.Children[0]);
        if (!n.B("presented")) return;

        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "position:fixed;inset:0;background:rgba(0,0,0,0.4);display:flex;align-items:center;justify-content:center;z-index:1000;");
        b.OpenElement(_seq++, "div");
        b.AddAttribute(_seq++, "style", "background:#fff;border-radius:14px;padding:24px;min-width:280px;max-width:90%;display:flex;flex-direction:column;gap:12px;");
        if (isSheet)
        {
            if (n.Children.Count > 1) RenderNode(b, n.Children[1]);
        }
        else
        {
            b.OpenElement(_seq++, "b"); b.AddContent(_seq++, n.S("title")); b.CloseElement();
            b.OpenElement(_seq++, "div"); b.AddContent(_seq++, n.S("message")); b.CloseElement();
        }
        b.OpenElement(_seq++, "button");
        b.AddAttribute(_seq++, "style", "align-self:flex-end;cursor:pointer;padding:6px 16px;border-radius:8px;border:none;background:#007AFF;color:#fff;");
        b.AddAttribute(_seq++, "onclick", EventCallback.Factory.Create(this, () => _bridge.Emit(n.Id, "false")));
        b.AddContent(_seq++, isSheet ? "Close" : "OK");
        b.CloseElement();
        b.CloseElement();
        b.CloseElement();
    }

    // ---- display -------------------------------------------------------------

    void WebFrame(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "iframe");
        Style(b, n, "width:100%;height:300px;border:1px solid #ddd;border-radius:8px;");
        if (n.Props.GetValueOrDefault("url") is string url) b.AddAttribute(_seq++, "src", url);
        else if (n.Props.GetValueOrDefault("html") is string html) b.AddAttribute(_seq++, "srcdoc", html);
        b.AddAttribute(_seq++, "sandbox", "allow-scripts allow-same-origin allow-forms allow-popups");
        b.CloseElement();
    }

    void ImageNode(RenderTreeBuilder b, WebNode n)
    {
        // F3 raster: url / file (served path) / bytes (data URI) render an <img>; an SF-Symbol name
        // falls back to the emoji glyph (backends without SF Symbols).
        var src = n.S("url") is { Length: > 0 } u ? u
                : n.S("file") is { Length: > 0 } f ? f
                : n.S("bytes") is { Length: > 0 } bytes ? $"data:image/png;base64,{bytes}"
                : null;
        if (src is null)
        {
            // Raster-only image that hasn't loaded: render nothing rather than a stray bullet.
            if (n.S("system").Length > 0) Leaf(b, n, "span", WebStyle.Emoji(n.S("system")), "font-size:22px;");
            else Element(b, n, "span", "", null);
            return;
        }
        var fit = n.S("contentMode") == "fill" ? "cover" : "contain";
        b.OpenElement(_seq++, "img");
        Style(b, n, $"object-fit:{fit};max-width:100%;");
        b.AddAttribute(_seq++, "src", src);
        b.CloseElement();
    }

    void Label(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "span");
        Style(b, n, "display:inline-flex;gap:6px;align-items:center;");
        b.AddContent(_seq++, WebStyle.Emoji(n.S("systemImage")) + " " + n.S("title"));
        b.CloseElement();
    }

    void Progress(RenderTreeBuilder b, WebNode n)
    {
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;gap:4px;align-items:center;");
        if (n.Props.GetValueOrDefault("label") is string text)
        {
            b.OpenElement(_seq++, "span"); b.AddContent(_seq++, text); b.CloseElement();
        }
        b.OpenElement(_seq++, "progress");
        if (n.N("value") is { } v) { b.AddAttribute(_seq++, "value", v.ToString(CultureInfo.InvariantCulture)); b.AddAttribute(_seq++, "max", "1"); }
        b.AddAttribute(_seq++, "style", "width:200px;");
        b.CloseElement();
        b.CloseElement();
    }

    void Gauge(RenderTreeBuilder b, WebNode n)
    {
        var lo = n.N("min") ?? 0; var hi = n.N("max") ?? 1;
        var frac = hi > lo ? ((n.N("value") ?? 0) - lo) / (hi - lo) : 0;
        b.OpenElement(_seq++, "div");
        Style(b, n, "display:flex;flex-direction:column;gap:4px;");
        if (n.Props.GetValueOrDefault("label") is string text)
        {
            b.OpenElement(_seq++, "span"); b.AddContent(_seq++, text); b.CloseElement();
        }
        b.OpenElement(_seq++, "progress");
        b.AddAttribute(_seq++, "value", Math.Clamp(frac, 0, 1).ToString(CultureInfo.InvariantCulture));
        b.AddAttribute(_seq++, "max", "1");
        b.AddAttribute(_seq++, "style", "width:100%;");
        b.CloseElement();
        b.CloseElement();
    }

    void Shape(RenderTreeBuilder b, WebNode n, string radius)
    {
        b.OpenElement(_seq++, "div");
        Style(b, n, $"width:40px;height:40px;border-radius:{radius};background:#8E8E93;");
        b.CloseElement();
    }

    void Custom(RenderTreeBuilder b, WebNode n)
    {
        if (WebRenderers.GetComponent(n.Type) is { } componentType)
        {
            // Persistent component: keyed by node id so Blazor preserves the instance (and its JS map handle)
            // across renders instead of tearing it down.
            b.OpenComponent(_seq++, componentType);
            b.SetKey(n.Id);
            b.AddComponentParameter(_seq++, "NodeId", n.Id);
            b.AddComponentParameter(_seq++, "Props", n.Props);
            b.AddComponentParameter(_seq++, "Emit", (Action<string, string?>)_bridge.Emit);
            b.CloseComponent();
            return;
        }

        var r = WebRenderers.Get(n.Type);
        if (r is not null) r(b, new WebRenderContext(n.Id, n.Props, _bridge.Emit), ref _seq);
        else Leaf(b, n, "span", $"⚠️ {n.Type}", "color:#FF3B30;");
    }

    // ---- shared --------------------------------------------------------------

    void Style(RenderTreeBuilder b, WebNode n, string? baseStyle)
    {
        var isShape = n.Type is "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle";
        var magnify = n.Modifiers.FirstOrDefault(m => m["type"] as string == "onMagnify");
        var css = (baseStyle ?? "") + WebStyle.Modifiers(n.Modifiers, isShape)
            // A pinch target must own its pointer stream: without this the browser's own page zoom /
            // scroll swallows the second finger before pointermove ever reaches Blazor.
            + (magnify is not null ? "touch-action:none;" : "");
        if (css.Length > 0) b.AddAttribute(_seq++, "style", css);

        var tap = n.Modifiers.FirstOrDefault(m => m["type"] as string == "onTapGesture");
        if (tap?.GetValueOrDefault("event") is string ev)
        {
            var name = Amount(tap, "amount", 1) >= 2 ? "ondblclick" : "onclick";
            b.AddAttribute(_seq++, name, EventCallback.Factory.Create(this, () => _bridge.Emit(ev, null)));
        }

        var drag = n.Modifiers.FirstOrDefault(m => m["type"] as string == "onDrag");
        var longPress = n.Modifiers.FirstOrDefault(m => m["type"] as string == "onLongPress");
        var swipe = n.Modifiers.FirstOrDefault(m => m["type"] as string == "onSwipe");
        if (drag is null && magnify is null && longPress is null && swipe is null) return;

        // Drag, pinch, long-press and swipe all ride the same pointerdown/move/up/cancel attributes, and a
        // later AddAttribute replaces an earlier one of the same name, so they are wired together in one
        // handler set — a view carrying several (ImageViewer: drag + pinch) gets all of them.
        var dragEv = drag?.GetValueOrDefault("event") as string;
        var magEv = magnify?.GetValueOrDefault("event") as string;
        var lpEv = longPress?.GetValueOrDefault("event") as string;
        var lpDur = Amount(longPress, "amount", 0.5);
        var swEv = swipe?.GetValueOrDefault("event") as string;
        var swDir = swipe?.GetValueOrDefault("value") as string;

        b.AddAttribute(_seq++, "onpointerdown", EventCallback.Factory.Create<PointerEventArgs>(this, e =>
        {
            if (magEv is not null)
            {
                var pts = _pinchPoints.TryGetValue(magEv, out var p) ? p : _pinchPoints[magEv] = new();
                pts[e.PointerId] = (e.ClientX, e.ClientY);
                if (pts.Count == 2)
                {
                    _pinchStart[magEv] = Spread(pts);
                    // The second finger turns a pan into a pinch: close out any in-flight drag first so the
                    // handler sees a well-formed b…c…e sequence.
                    if (dragEv is not null && _dragStart.Remove(dragEv, out var ds))
                        _bridge.Emit(dragEv, $"e;{Inv(e.ClientX - ds.X)},{Inv(e.ClientY - ds.Y)};{Inv(e.OffsetX)},{Inv(e.OffsetY)};0,0");
                }
            }
            // F1 continuous drag: pointerdown/move/up emit the drag grammar ("<phase>;tx,ty;lx,ly;vx,vy").
            if (dragEv is not null && !Pinching(magEv))
            {
                _dragStart[dragEv] = (e.ClientX, e.ClientY);
                _bridge.Emit(dragEv, $"b;0,0;{Inv(e.OffsetX)},{Inv(e.OffsetY)};0,0");
            }
            if (lpEv is not null) StartLongPress(lpEv, lpDur);
            if (swEv is not null) _swipeStart[swEv] = (e.ClientX, e.ClientY);
        }));
        if (dragEv is not null || magEv is not null)
            b.AddAttribute(_seq++, "onpointermove", EventCallback.Factory.Create<PointerEventArgs>(this, e =>
            {
                // F1 pinch: two live pointers → the cumulative scale factor (current spread ÷ the spread at
                // the moment the second finger landed; 1.0 = unchanged), matching every other backend.
                if (magEv is not null && _pinchPoints.TryGetValue(magEv, out var pts) && pts.ContainsKey(e.PointerId))
                {
                    pts[e.PointerId] = (e.ClientX, e.ClientY);
                    if (pts.Count >= 2 && _pinchStart.TryGetValue(magEv, out var d0) && d0 > 0)
                    {
                        _bridge.Emit(magEv, Inv(Spread(pts) / d0));
                        return;     // the pinch owns the stream; don't pan at the same time
                    }
                }
                if (dragEv is not null && _dragStart.TryGetValue(dragEv, out var s))
                    _bridge.Emit(dragEv, $"c;{Inv(e.ClientX - s.X)},{Inv(e.ClientY - s.Y)};{Inv(e.OffsetX)},{Inv(e.OffsetY)};0,0");
            }));
        b.AddAttribute(_seq++, "onpointerup", EventCallback.Factory.Create<PointerEventArgs>(this, e =>
        {
            EndPinchPointer(magEv, e.PointerId);
            if (dragEv is not null && _dragStart.Remove(dragEv, out var s))
                _bridge.Emit(dragEv, $"e;{Inv(e.ClientX - s.X)},{Inv(e.ClientY - s.Y)};{Inv(e.OffsetX)},{Inv(e.OffsetY)};0,0");
            if (lpEv is not null) CancelLongPress(lpEv);
            if (swEv is not null) EndSwipe(swEv, swDir, e);
        }));
        b.AddAttribute(_seq++, "onpointercancel", EventCallback.Factory.Create<PointerEventArgs>(this, e =>
        {
            EndPinchPointer(magEv, e.PointerId);
            if (dragEv is not null) _dragStart.Remove(dragEv);
            if (lpEv is not null) CancelLongPress(lpEv);
            if (swEv is not null) _swipeStart.Remove(swEv);
        }));
        if (lpEv is not null)
            b.AddAttribute(_seq++, "onpointerleave", EventCallback.Factory.Create<PointerEventArgs>(this, _ => CancelLongPress(lpEv)));

        // Trackpad pinch on a desktop browser arrives as wheel+ctrlKey, never as two pointers. There is no
        // begin/end for it, so the factor accumulates across the whole session (an absolute zoom level),
        // which is the same 1.0-is-unchanged value the handler expects.
        if (magEv is not null)
        {
            b.AddAttribute(_seq++, "onwheel", EventCallback.Factory.Create<WheelEventArgs>(this, e =>
            {
                if (!e.CtrlKey) return;
                var scale = Math.Clamp(_wheelScale.GetValueOrDefault(magEv, 1) * Math.Exp(-e.DeltaY / 100.0), 0.05, 50);
                _wheelScale[magEv] = scale;
                _bridge.Emit(magEv, Inv(scale));
            }));
            b.AddEventPreventDefaultAttribute(_seq++, "onwheel", true);
        }
    }

    /// <summary>Distance between the first two live pointers of a pinch.</summary>
    static double Spread(Dictionary<long, (double X, double Y)> pts)
    {
        var a = pts.Values.ElementAt(0);
        var b = pts.Values.ElementAt(1);
        return Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }

    bool Pinching(string? magEv) => magEv is not null && _pinchPoints.TryGetValue(magEv, out var p) && p.Count >= 2;

    void EndPinchPointer(string? magEv, long pointerId)
    {
        if (magEv is null || !_pinchPoints.TryGetValue(magEv, out var pts)) return;
        pts.Remove(pointerId);
        if (pts.Count < 2) _pinchStart.Remove(magEv);
    }

    void StartLongPress(string ev, double seconds)
    {
        CancelLongPress(ev);
        var cts = new CancellationTokenSource();
        _longPress[ev] = cts;
        _ = Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            _ = InvokeAsync(() => { if (_longPress.Remove(ev)) _bridge.Emit(ev, null); });
        }, TaskScheduler.Default);
    }

    void CancelLongPress(string ev)
    {
        if (_longPress.Remove(ev, out var cts)) cts.Cancel();
    }

    static double Amount(Dictionary<string, object?>? m, string key, double fallback)
        => m is not null && m.TryGetValue(key, out var v) && v is double d ? d : fallback;

    static string Inv(double v) => v.ToString(CultureInfo.InvariantCulture);

    void EndSwipe(string ev, string? dir, PointerEventArgs e)
    {
        if (!_swipeStart.Remove(ev, out var start)) return;
        var dx = e.ClientX - start.X;
        var dy = e.ClientY - start.Y;
        var matched = Math.Abs(dx) > Math.Abs(dy)
            ? (dx < 0 ? dir == "left" : dir == "right")
            : (dy < 0 ? dir == "up" : dir == "down");
        if (matched && (Math.Abs(dx) > 40 || Math.Abs(dy) > 40)) _bridge.Emit(ev, null);
    }

    static string TitleOf(WebNode n) =>
        n.Modifiers.FirstOrDefault(m => m["type"] as string == "navigationTitle")?.GetValueOrDefault("value") as string ?? "";
}
