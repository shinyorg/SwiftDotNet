using System.Globalization;
using SwiftDotNet;

namespace SwiftDotNet.Graphics;

/// <summary>
/// Overlay pass for <see cref="VisualNode"/> — Sheet, Alert, Menu popovers and pushed NavigationStack
/// destinations. Because a self-drawn canvas has no OS modal layer, these are painted full-window on top
/// of the base scene by the bridge (post-order, so an outer Alert lands above an inner Sheet), and
/// hit-tested before the base scene. Presented state is prop-bound (Sheet/Alert) or engine-local (Menu, nav push).
/// </summary>
sealed partial class VisualNode
{
    Rect _menuRect, _sheetPanel, _alertBox, _navBack, _swatchRect;

    /// <summary>
    /// Laid-out button rects for the presented Alert / ActionSheet, index-aligned with the parsed
    /// <c>buttons</c> prop. Written by the paint pass and read by the hit-test, so a tap always lands on
    /// the button that was actually drawn — the same paint-defines-geometry contract the swatch grid uses.
    /// </summary>
    readonly List<Rect> _dialogButtons = new();

    /// <summary>The lift shared by every floating popover (Menu, ColorPicker swatches).</summary>
    static readonly Shadow PopoverShadow = new(0, 4, 8, new Color(0, 0, 0, 60));

    internal bool HasActiveOverlay => Type switch
    {
        "Sheet" => Bool("presented") && Children.Count > 1,
        "Alert" or "ActionSheet" => Bool("presented"),
        "Menu" => MenuOpen,
        "ColorPicker" => MenuOpen,
        "NavigationStack" => PushedContent is not null,
        _ => false,
    };

    internal void PaintOverlay(ICanvas canvas, Rect window, bool dark)
    {
        switch (Type)
        {
            case "Sheet": PaintSheet(canvas, window, dark); break;
            case "Alert": PaintAlert(canvas, window, dark); break;
            case "ActionSheet": PaintActionSheet(canvas, window, dark); break;
            case "Menu": PaintMenu(canvas, window, dark); break;
            case "ColorPicker": PaintSwatches(canvas, window, dark); break;
            case "NavigationStack": PaintPushed(canvas, window, dark); break;
        }
    }

    internal bool HitTestOverlay(Point p, Rect window)
    {
        switch (Type)
        {
            case "Sheet":
                if (!_sheetPanel.Contains(p)) { _bridge.Emit(Id, "false"); return true; } // scrim dismiss
                Children[1].HitTest(p);
                return true;
            case "Alert" or "ActionSheet":
                // Index-aligned with the parsed buttons, so the payload IS the chosen button's index;
                // anything outside the card is a choice-free dismissal.
                for (var i = 0; i < _dialogButtons.Count; i++)
                    if (_dialogButtons[i].Contains(p)) { _bridge.Emit(Id, i.ToString(CultureInfo.InvariantCulture)); return true; }
                if (!_alertBox.Contains(p)) _bridge.Emit(Id, "false");
                return true;
            case "Menu":
                if (_menuRect.Contains(p))
                {
                    var idx = (int)((p.Y - _menuRect.Top - 6) / 40);
                    if (idx >= 0 && idx < Children.Count) { MenuOpen = false; _bridge.Emit(Children[idx].Id, null); }
                }
                else MenuOpen = false; // tap outside closes
                return true;
            case "ColorPicker":
                if (_swatchRect.Contains(p))
                {
                    var col = (int)((p.X - _swatchRect.Left - SwatchPad) / SwatchStride);
                    var row = (int)((p.Y - _swatchRect.Top - SwatchPad) / SwatchStride);
                    var idx = row * SwatchColumns + col;
                    if (col >= 0 && col < SwatchColumns && idx >= 0 && idx < Palette.Length)
                    {
                        MenuOpen = false;
                        _bridge.Emit(Id, Palette[idx]);
                    }
                }
                else MenuOpen = false; // tap outside closes
                return true;
            case "NavigationStack":
                if (!Frame.Contains(p)) return false; // outside the pushed region (e.g. the tab bar) → not ours
                if (_navBack.Contains(p)) { PushedContent = null; return true; }
                PushedContent?.HitTest(p);
                return true;
        }
        return false;
    }

    /// <summary>
    /// The overlay's content subtree, if a point falls inside the region that subtree owns.
    ///
    /// An overlay's content is NOT part of <see cref="Children"/> — a pushed nav destination and a Sheet's
    /// body are separate trees, arranged during <see cref="PaintOverlay"/>. Tap routing goes through
    /// <see cref="HitTestOverlay"/>, but the continuous/deferred gestures (drag, pinch, long-press, swipe)
    /// resolve their target by walking the node tree, so without this they only ever see the base scene —
    /// which made every gesture dead on a pushed page or inside a Sheet, including the Controls library's
    /// overlay-presented Dialog / FloatingPanel / ImageViewer.
    /// </summary>
    internal VisualNode? OverlayContentAt(Point p) => Type switch
    {
        "Sheet" when _sheetPanel.Contains(p) && Children.Count > 1 => Children[1],
        "NavigationStack" when Frame.Contains(p) && !_navBack.Contains(p) => PushedContent,
        // Alert and Menu are engine-drawn chrome with no gesture-bearing content subtree.
        _ => null,
    };

    /// <summary>
    /// The content subtrees this node presents, for the overlay walk. These hang off the node rather than
    /// living in <see cref="Children"/>, so anything *they* present needs collecting separately.
    /// </summary>
    internal IEnumerable<VisualNode> OverlayContentRoots()
    {
        if (Type == "NavigationStack" && PushedContent is { } pushed) yield return pushed;
        if (Type == "Sheet" && Bool("presented") && Children.Count > 1) yield return Children[1];
    }

    static void Scrim(ICanvas canvas, Rect window)
    {
        var s = new Paint { Color = new Color(0, 0, 0, 110) };
        canvas.DrawRect(window, s);
    }

    void PaintSheet(ICanvas canvas, Rect window, bool dark)
    {
        Scrim(canvas, window);
        _sheetPanel = new Rect(window.Left, window.Top + 90, window.Right, window.Bottom);
        var panel = new Paint { Color = Theme.Background(dark), IsAntialias = true };
        canvas.DrawRoundRect(_sheetPanel, 18, 18, panel);
        // grabber
        var grab = new Paint { Color = Theme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(new Rect(window.MidX - 18, _sheetPanel.Top + 8, window.MidX + 18, _sheetPanel.Top + 12), 2, 2, grab);

        var inner = new Rect(_sheetPanel.Left + 8, _sheetPanel.Top + 20, _sheetPanel.Right - 8, _sheetPanel.Bottom);
        var content = Children[1];
        content.Measure(new Size(inner.Width, inner.Height));
        content.Arrange(inner);
        content.Draw(canvas, dark);
    }

    const float DialogButtonHeight = 44;

    /// <summary>The tint a dialog button's role reads as — matching the platform alerts' convention.</summary>
    static Color RoleColor(DialogRole role) =>
        role == DialogRole.Destructive ? new Color(0xFF, 0x3B, 0x30) : Theme.Accent;

    /// <summary>
    /// A centered alert card: title, wrapped message, then the buttons. Two buttons sit side by side
    /// (the iOS convention); one, or three or more, stack vertically — a row of four would be unreadable
    /// at 300pt wide.
    /// </summary>
    void PaintAlert(ICanvas canvas, Rect window, bool dark)
    {
        Scrim(canvas, window);
        var w = 300f;
        var titleFont = Theme.MakeFont("headline", Fonts);
        var msgFont = Theme.MakeFont("body", Fonts);
        var msgLines = TextLayout.Wrap(Str("message"), msgFont, w - 40, Fonts);
        var lh = msgFont.Metrics.Descent - msgFont.Metrics.Ascent;

        var buttons = DialogButtons.Parse(Str("buttons"));
        var sideBySide = buttons.Count == 2;
        var buttonBlock = (sideBySide ? 1 : buttons.Count) * DialogButtonHeight;
        var h = 28 + 24 + msgLines.Count * lh + 20 + buttonBlock;

        _alertBox = new Rect(window.MidX - w / 2, window.MidY - h / 2, window.MidX + w / 2, window.MidY + h / 2);
        var box = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(_alertBox, 14, 14, box);

        DrawBlock(canvas, new() { Str("title") }, new Rect(_alertBox.Left, _alertBox.Top + 20, _alertBox.Right, _alertBox.Top + 44), titleFont, dark ? Colors.White : Colors.Black, "center");
        var my = _alertBox.Top + 52;
        foreach (var line in msgLines)
        {
            DrawBlock(canvas, new() { line }, new Rect(_alertBox.Left, my, _alertBox.Right, my + lh), msgFont, new Color(0x8E, 0x8E, 0x93), "center");
            my += lh;
        }

        _dialogButtons.Clear();
        var sep = new Paint { Color = Theme.Separator(dark), StrokeWidth = 1 };
        var font = Theme.MakeFont("headline", Fonts);
        var top = _alertBox.Bottom - buttonBlock;
        canvas.DrawLine(_alertBox.Left, top, _alertBox.Right, top, sep);

        for (var i = 0; i < buttons.Count; i++)
        {
            var rect = sideBySide
                ? new Rect(i == 0 ? _alertBox.Left : _alertBox.MidX, top,
                           i == 0 ? _alertBox.MidX : _alertBox.Right, _alertBox.Bottom)
                : new Rect(_alertBox.Left, top + i * DialogButtonHeight, _alertBox.Right, top + (i + 1) * DialogButtonHeight);
            _dialogButtons.Add(rect);
            if (i > 0)
            {
                if (sideBySide) canvas.DrawLine(rect.Left, rect.Top, rect.Left, rect.Bottom, sep);
                else canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, sep);
            }
            DrawCentered(canvas, buttons[i].Label, rect, font, RoleColor(buttons[i].Role));
        }
    }

    /// <summary>
    /// A bottom-anchored choice list — the iOS action sheet shape. Cancel-role buttons are pulled out
    /// into their own detached card below the options, which is what makes an action sheet read as
    /// "pick one of these, or back out" rather than as a long undifferentiated list.
    /// </summary>
    void PaintActionSheet(ICanvas canvas, Rect window, bool dark)
    {
        Scrim(canvas, window);
        var buttons = DialogButtons.Parse(Str("buttons"));
        var cancel = DialogButtons.CancelIndex(buttons);

        var titleFont = Theme.MakeFont("caption", Fonts);
        var font = Theme.MakeFont("body", Fonts);
        var title = Str("title");
        var message = Str("message");
        const float pad = 10, gap = 8;
        var w = Math.Min(360f, window.Width - 2 * pad);

        var headerLines = new List<string>();
        if (title.Length > 0) headerLines.AddRange(TextLayout.Wrap(title, titleFont, w - 32, Fonts));
        if (message.Length > 0) headerLines.AddRange(TextLayout.Wrap(message, titleFont, w - 32, Fonts));
        var headerLh = titleFont.Metrics.Descent - titleFont.Metrics.Ascent;
        var headerH = headerLines.Count == 0 ? 0 : headerLines.Count * headerLh + 16;

        var optionCount = buttons.Count - (cancel >= 0 ? 1 : 0);
        var optionsH = headerH + optionCount * DialogButtonHeight;
        var cancelH = cancel >= 0 ? DialogButtonHeight + gap : 0;
        var totalH = optionsH + cancelH;

        var left = window.MidX - w / 2;
        var bottom = window.Bottom - pad;
        var optionsTop = bottom - totalH;
        var optionsRect = new Rect(left, optionsTop, left + w, optionsTop + optionsH);
        // The whole stack counts as "inside" for hit-testing, so a tap on the gap between the two cards
        // isn't read as a scrim dismissal.
        _alertBox = new Rect(left, optionsTop, left + w, bottom);

        var surface = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(optionsRect, 14, 14, surface);

        var hy = optionsRect.Top + 8;
        foreach (var line in headerLines)
        {
            DrawBlock(canvas, new() { line }, new Rect(optionsRect.Left, hy, optionsRect.Right, hy + headerLh), titleFont, new Color(0x8E, 0x8E, 0x93), "center");
            hy += headerLh;
        }

        _dialogButtons.Clear();
        for (var i = 0; i < buttons.Count; i++) _dialogButtons.Add(default);

        var sep = new Paint { Color = Theme.Separator(dark), StrokeWidth = 1 };
        var y = optionsRect.Top + headerH;
        var drawn = 0;
        for (var i = 0; i < buttons.Count; i++)
        {
            if (i == cancel) continue;
            var rect = new Rect(optionsRect.Left, y, optionsRect.Right, y + DialogButtonHeight);
            _dialogButtons[i] = rect;
            if (drawn > 0 || headerH > 0) canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Top, sep);
            DrawCentered(canvas, buttons[i].Label, rect, font, RoleColor(buttons[i].Role));
            y += DialogButtonHeight;
            drawn++;
        }

        if (cancel < 0) return;
        var cancelRect = new Rect(left, bottom - DialogButtonHeight, left + w, bottom);
        canvas.DrawRoundRect(cancelRect, 14, 14, surface);
        _dialogButtons[cancel] = cancelRect;
        DrawCentered(canvas, buttons[cancel].Label, cancelRect, Theme.MakeFont("headline", Fonts), Theme.Accent);
    }

    void PaintMenu(ICanvas canvas, Rect window, bool dark)
    {
        var w = 220f;
        var h = Children.Count * 40 + 12;
        var top = Math.Min(_content.Bottom + 4, window.Bottom - h - 8);
        _menuRect = new Rect(Math.Max(8, _content.Right - w), top, _content.Right, top + h);
        canvas.DrawRoundRect(_menuRect, 12, 12,
            new Paint { Color = Theme.Surface(dark), IsAntialias = true, Shadow = PopoverShadow });
        var font = Theme.MakeFont("body", Fonts);
        for (var i = 0; i < Children.Count; i++)
        {
            var ry = _menuRect.Top + 6 + i * 40;
            if (i > 0)
                canvas.DrawLine(_menuRect.Left + 12, ry, _menuRect.Right - 12, ry,
                    Paint.Hairline(Theme.Separator(dark), 0.5f));
            canvas.DrawText(Children[i].Str("title"), _menuRect.Left + 14, ry + 26, font, dark ? Colors.White : Colors.Black);
        }
    }

    // Swatch popover geometry, shared by paint and hit-test so a tap always lands on what was drawn.
    internal const int SwatchColumns = 4;
    const float SwatchSize = 44, SwatchGap = 8, SwatchPad = 12;
    const float SwatchStride = SwatchSize + SwatchGap;

    /// <summary>
    /// The built-in ColorPicker's popover: the palette as a grid of tappable swatches, with the current
    /// one ringed. It used to advance blindly to the next palette entry on each tap, which is technically
    /// "working" and reads as broken — you cannot choose a colour, only cycle past it.
    /// </summary>
    void PaintSwatches(ICanvas canvas, Rect window, bool dark)
    {
        var rows = (Palette.Length + SwatchColumns - 1) / SwatchColumns;
        var w = SwatchColumns * SwatchStride - SwatchGap + SwatchPad * 2;
        var h = rows * SwatchStride - SwatchGap + SwatchPad * 2;
        var top = Math.Min(_content.Bottom + 4, window.Bottom - h - 8);
        var left = Math.Max(8, Math.Min(_content.Right - w, window.Right - w - 8));
        _swatchRect = new Rect(left, top, left + w, top + h);

        canvas.DrawRoundRect(_swatchRect, 12, 12,
            new Paint { Color = Theme.Surface(dark), IsAntialias = true, Shadow = PopoverShadow });

        var current = Str("value");
        for (var i = 0; i < Palette.Length; i++)
        {
            var cx = _swatchRect.Left + SwatchPad + i % SwatchColumns * SwatchStride;
            var cy = _swatchRect.Top + SwatchPad + i / SwatchColumns * SwatchStride;
            var cell = new Rect(cx, cy, cx + SwatchSize, cy + SwatchSize);

            var fill = new Paint { Color = Theme.Color(Palette[i], dark), IsAntialias = true };
            canvas.DrawRoundRect(cell, 10, 10, fill);

            if (!string.Equals(Palette[i], current, StringComparison.OrdinalIgnoreCase)) continue;
            var ring = new Paint
            {
                Color = dark ? Colors.White : Colors.Black,
                IsAntialias = true,
                Style = PaintStyle.Stroke,
                StrokeWidth = 3,
            };
            canvas.DrawRoundRect(Rect.Inflate(cell, 3, 3), 13, 13, ring);
        }
    }

    /// <summary>
    /// Centre of the <paramref name="index"/>th button on the presented Alert / ActionSheet. For
    /// tests/tooling — like the swatch grid, the dialog is engine-drawn chrome with no node to hit-test
    /// against, so its geometry is only knowable after a paint pass.
    /// </summary>
    internal bool TryGetDialogButtonCenter(int index, out Point center)
    {
        center = default;
        if (Type is not ("Alert" or "ActionSheet") || !Bool("presented")) return false;
        if (index < 0 || index >= _dialogButtons.Count) return false;
        var rect = _dialogButtons[index];
        center = new Point(rect.MidX, rect.MidY);
        return true;
    }

    /// <summary>Centre of a laid-out swatch in the open popover. For tests/tooling.</summary>
    internal bool TryGetSwatchCenter(int index, out Point center)
    {
        center = default;
        if (Type != "ColorPicker" || !MenuOpen || index < 0 || index >= Palette.Length) return false;
        center = new Point(
            _swatchRect.Left + SwatchPad + index % SwatchColumns * SwatchStride + SwatchSize / 2,
            _swatchRect.Top + SwatchPad + index / SwatchColumns * SwatchStride + SwatchSize / 2);
        return true;
    }

    void PaintPushed(ICanvas canvas, Rect window, bool dark)
    {
        // A nav push lives INSIDE its tab, so it covers the NavigationStack's own frame — not the whole
        // window — leaving the tab bar visible and tappable.
        var region = Frame;
        var bg = new Paint { Color = Theme.Background(dark) };
        canvas.DrawRect(region, bg);
        var bar = new Rect(region.Left, region.Top, region.Right, region.Top + NavBarHeight);
        _navBack = new Rect(bar.Left, bar.Top, bar.Left + 80, bar.Bottom);
        var barBg = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(bar, barBg);
        var sep = new Paint { Color = Theme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(bar.Left, bar.Bottom, bar.Right, bar.Bottom, sep);
        canvas.DrawText("‹ Back", bar.Left + 12, Baseline(bar, Theme.MakeFont("body", Fonts)), Theme.MakeFont("body", Fonts), Theme.Accent);
        DrawCentered(canvas, PushedTitle, bar, Theme.MakeFont("headline", Fonts), dark ? Colors.White : Colors.Black);

        var contentRect = new Rect(region.Left, bar.Bottom, region.Right, region.Bottom);
        PushedContent!.Measure(new Size(contentRect.Width, contentRect.Height));
        PushedContent.Arrange(contentRect);
        PushedContent.Draw(canvas, dark);
    }
}
