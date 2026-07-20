using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Overlay pass for <see cref="SkiaNode"/> — Sheet, Alert, Menu popovers and pushed NavigationStack
/// destinations. Because a self-drawn canvas has no OS modal layer, these are painted full-window on top
/// of the base scene by the bridge (post-order, so an outer Alert lands above an inner Sheet), and
/// hit-tested before the base scene. Presented state is prop-bound (Sheet/Alert) or engine-local (Menu, nav push).
/// </summary>
sealed partial class SkiaNode
{
    SKRect _menuRect, _sheetPanel, _alertBox, _alertOk, _navBack;

    internal bool HasActiveOverlay => Type switch
    {
        "Sheet" => Bool("presented") && Children.Count > 1,
        "Alert" => Bool("presented"),
        "Menu" => MenuOpen,
        "NavigationStack" => PushedContent is not null,
        _ => false,
    };

    internal void PaintOverlay(SKCanvas canvas, SKRect window, bool dark)
    {
        switch (Type)
        {
            case "Sheet": PaintSheet(canvas, window, dark); break;
            case "Alert": PaintAlert(canvas, window, dark); break;
            case "Menu": PaintMenu(canvas, window, dark); break;
            case "NavigationStack": PaintPushed(canvas, window, dark); break;
        }
    }

    internal bool HitTestOverlay(SKPoint p, SKRect window)
    {
        switch (Type)
        {
            case "Sheet":
                if (!_sheetPanel.Contains(p)) { _bridge.Emit(Id, "false"); return true; } // scrim dismiss
                Children[1].HitTest(p);
                return true;
            case "Alert":
                if (_alertOk.Contains(p) || !_alertBox.Contains(p)) { _bridge.Emit(Id, "false"); return true; }
                return true;
            case "Menu":
                if (_menuRect.Contains(p))
                {
                    var idx = (int)((p.Y - _menuRect.Top - 6) / 40);
                    if (idx >= 0 && idx < Children.Count) { MenuOpen = false; _bridge.Emit(Children[idx].Id, null); }
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
    internal SkiaNode? OverlayContentAt(SKPoint p) => Type switch
    {
        "Sheet" when _sheetPanel.Contains(p) && Children.Count > 1 => Children[1],
        "NavigationStack" when Frame.Contains(p) && !_navBack.Contains(p) => PushedContent,
        // Alert and Menu are engine-drawn chrome with no gesture-bearing content subtree.
        _ => null,
    };

    static void Scrim(SKCanvas canvas, SKRect window)
    {
        using var s = new SKPaint { Color = new SKColor(0, 0, 0, 110) };
        canvas.DrawRect(window, s);
    }

    void PaintSheet(SKCanvas canvas, SKRect window, bool dark)
    {
        Scrim(canvas, window);
        _sheetPanel = new SKRect(window.Left, window.Top + 90, window.Right, window.Bottom);
        using var panel = new SKPaint { Color = SkiaTheme.Background(dark), IsAntialias = true };
        canvas.DrawRoundRect(_sheetPanel, 18, 18, panel);
        // grabber
        using var grab = new SKPaint { Color = SkiaTheme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(window.MidX - 18, _sheetPanel.Top + 8, window.MidX + 18, _sheetPanel.Top + 12), 2, 2, grab);

        var inner = new SKRect(_sheetPanel.Left + 8, _sheetPanel.Top + 20, _sheetPanel.Right - 8, _sheetPanel.Bottom);
        var content = Children[1];
        content.Measure(new SKSize(inner.Width, inner.Height));
        content.Arrange(inner);
        content.Paint(canvas, dark);
    }

    void PaintAlert(SKCanvas canvas, SKRect window, bool dark)
    {
        Scrim(canvas, window);
        var w = 300f;
        var titleFont = SkiaTheme.MakeFont("headline");
        var msgFont = SkiaTheme.MakeFont("body");
        var msgLines = SkiaText.Wrap(Str("message"), msgFont, w - 40);
        var lh = msgFont.Metrics.Descent - msgFont.Metrics.Ascent;
        var h = 28 + 24 + msgLines.Count * lh + 20 + 44;
        _alertBox = new SKRect(window.MidX - w / 2, window.MidY - h / 2, window.MidX + w / 2, window.MidY + h / 2);
        using var box = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(_alertBox, 14, 14, box);

        var y = _alertBox.Top + 20 - titleFont.Metrics.Ascent;
        DrawBlock(canvas, new() { Str("title") }, new SKRect(_alertBox.Left, _alertBox.Top + 20, _alertBox.Right, _alertBox.Top + 44), titleFont, dark ? SKColors.White : SKColors.Black, "center");
        var my = _alertBox.Top + 52;
        foreach (var line in msgLines)
        {
            DrawBlock(canvas, new() { line }, new SKRect(_alertBox.Left, my, _alertBox.Right, my + lh), msgFont, new SKColor(0x8E, 0x8E, 0x93), "center");
            my += lh;
        }
        _alertOk = new SKRect(_alertBox.Left, _alertBox.Bottom - 44, _alertBox.Right, _alertBox.Bottom);
        using var sep = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(_alertOk.Left, _alertOk.Top, _alertOk.Right, _alertOk.Top, sep);
        DrawCentered(canvas, "OK", _alertOk, SkiaTheme.MakeFont("headline"), SkiaTheme.Accent);
    }

    void PaintMenu(SKCanvas canvas, SKRect window, bool dark)
    {
        var w = 220f;
        var h = Children.Count * 40 + 12;
        var top = Math.Min(_content.Bottom + 4, window.Bottom - h - 8);
        _menuRect = new SKRect(Math.Max(8, _content.Right - w), top, _content.Right, top + h);
        using var shadow = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true, ImageFilter = SKImageFilter.CreateDropShadow(0, 4, 8, 8, new SKColor(0, 0, 0, 60)) };
        canvas.DrawRoundRect(_menuRect, 12, 12, shadow);
        var font = SkiaTheme.MakeFont("body");
        for (var i = 0; i < Children.Count; i++)
        {
            var ry = _menuRect.Top + 6 + i * 40;
            if (i > 0) using (var sep = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 0.5f })
                canvas.DrawLine(_menuRect.Left + 12, ry, _menuRect.Right - 12, ry, sep);
            SkiaText.DrawLine(canvas, Children[i].Str("title"), _menuRect.Left + 14, ry + 26, font, dark ? SKColors.White : SKColors.Black);
        }
    }

    void PaintPushed(SKCanvas canvas, SKRect window, bool dark)
    {
        // A nav push lives INSIDE its tab, so it covers the NavigationStack's own frame — not the whole
        // window — leaving the tab bar visible and tappable.
        var region = Frame;
        using var bg = new SKPaint { Color = SkiaTheme.Background(dark) };
        canvas.DrawRect(region, bg);
        var bar = new SKRect(region.Left, region.Top, region.Right, region.Top + NavBarHeight);
        _navBack = new SKRect(bar.Left, bar.Top, bar.Left + 80, bar.Bottom);
        using var barBg = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(bar, barBg);
        using var sep = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(bar.Left, bar.Bottom, bar.Right, bar.Bottom, sep);
        SkiaText.DrawLine(canvas, "‹ Back", bar.Left + 12, Baseline(bar, SkiaTheme.MakeFont("body")), SkiaTheme.MakeFont("body"), SkiaTheme.Accent);
        DrawCentered(canvas, PushedTitle, bar, SkiaTheme.MakeFont("headline"), dark ? SKColors.White : SKColors.Black);

        var contentRect = new SKRect(region.Left, bar.Bottom, region.Right, region.Bottom);
        PushedContent!.Measure(new SKSize(contentRect.Width, contentRect.Height));
        PushedContent.Arrange(contentRect);
        PushedContent.Paint(canvas, dark);
    }
}
