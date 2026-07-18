using System.Globalization;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>The paint pass for <see cref="SkiaNode"/> — decorations, per-type content, and controls.</summary>
sealed partial class SkiaNode
{
    static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Paint(SKCanvas canvas, bool dark)
    {
        var count = canvas.Save();
        ApplyScale(canvas);

        var opacity = Opacity();
        SKPaint? layerPaint = null;
        if (opacity < 1)
        {
            layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)(opacity * 255)) };
            canvas.SaveLayer(layerPaint);
        }

        PaintDecorations(canvas, dark);
        PaintContent(canvas, dark);
        PaintChildren(canvas, dark);

        canvas.RestoreToCount(count);
        layerPaint?.Dispose();
    }

    void ApplyScale(SKCanvas canvas)
    {
        if (Scale() is not { } s || (Math.Abs(s.x - 1) < 0.0001 && Math.Abs(s.y - 1) < 0.0001)) return;
        var ax = s.anchor is "leading" or "topLeading" or "bottomLeading" ? Frame.Left
            : s.anchor is "trailing" or "topTrailing" or "bottomTrailing" ? Frame.Right : Frame.MidX;
        var ay = s.anchor is "top" or "topLeading" or "topTrailing" ? Frame.Top
            : s.anchor is "bottom" or "bottomLeading" or "bottomTrailing" ? Frame.Bottom : Frame.MidY;
        canvas.Translate(ax, ay);
        canvas.Scale((float)s.x, (float)s.y);
        canvas.Translate(-ax, -ay);
    }

    void PaintChildren(SKCanvas canvas, bool dark)
    {
        switch (Type)
        {
            case "TabView":
                if (_tabIndex < Children.Count) Children[_tabIndex].Paint(canvas, dark);
                PaintTabBar(canvas, dark);
                return;
            case "DisclosureGroup":
                if (Bool("expanded")) foreach (var c in Children) c.Paint(canvas, dark);
                return;
            case "NavigationLink":
                if (Children.Count > 0) Children[0].Paint(canvas, dark);
                return;
            case "NavigationStack":
                if (Children.Count > 0) Children[0].Paint(canvas, dark); // pushed destination is drawn as an overlay
                return;
            case "Sheet" or "Alert":
                if (Children.Count > 0) Children[0].Paint(canvas, dark); // presented content is drawn as an overlay
                return;
            case "Picker" or "Menu":
                return; // option / action children are shown inline (Picker) or in a popover (Menu), not laid out
            case "DatePicker" or "ColorPicker" or "Slider" or "Stepper" or "Toggle"
                or "TextField" or "SecureField" or "TextEditor":
                return; // self-drawn leaf controls have no paintable children
            case "ScrollView" or "List" or "Form":
            {
                var clip = canvas.Save();
                canvas.ClipRect(Frame);
                foreach (var c in Children) c.Paint(canvas, dark);
                canvas.RestoreToCount(clip);
                PaintScrollbar(canvas, dark);
                return;
            }
            default:
                foreach (var c in Children) c.Paint(canvas, dark);
                return;
        }
    }

    // ---- decorations (background / shadow / border) --------------------------

    void PaintDecorations(SKCanvas canvas, bool dark)
    {
        var radius = CornerRadius();

        if (BackgroundColor(dark) is { } bg)
        {
            using var paint = new SKPaint { Color = bg, IsAntialias = true, Style = SKPaintStyle.Fill };
            if (Shadow() is { } sh)
                paint.ImageFilter = SKImageFilter.CreateDropShadow(
                    (float)sh.x, (float)sh.y, (float)sh.radius, (float)sh.radius, sh.color);
            canvas.DrawRoundRect(Frame, radius, radius, paint);
        }

        // Form/List/Section rows sit on a grouped surface.
        if (Type is "Section" && HasProp("header"))
        {
            // header handled in PaintContent; the section body area uses a subtle surface.
        }

        if (Border(dark) is { } b)
        {
            using var stroke = new SKPaint
            {
                Color = b.color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = (float)b.width,
            };
            canvas.DrawRoundRect(Frame, radius, radius, stroke);
        }
    }

    // ---- content -------------------------------------------------------------

    void PaintContent(SKCanvas canvas, bool dark)
    {
        switch (Type)
        {
            case "Text":
                DrawBlock(canvas, _wrapLines ?? new List<string> { Str("text") }, _content, Font(), ForegroundColor(dark), AlignToken());
                break;
            case "Link":
                DrawBlock(canvas, new() { Str("title") }, _content, Font(), SkiaTheme.Color("blue", dark), AlignToken());
                break;
            case "Button":
                PaintButton(canvas, dark);
                break;
            case "Image":
                SkiaText.DrawLine(canvas, SkiaTheme.Icon(Str("system")), _content.Left, Baseline(_content, IconFont(22)), IconFont(22), ForegroundColor(dark));
                break;
            case "Label":
                SkiaText.DrawLine(canvas, SkiaTheme.Icon(Str("systemImage")) + "  " + Str("title"), _content.Left, Baseline(_content, Font()), Font(), ForegroundColor(dark));
                break;
            case "Divider":
                using (var line = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 1 })
                    canvas.DrawLine(_content.Left, _content.MidY, _content.Right, _content.MidY, line);
                break;
            case "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle":
                PaintShape(canvas, dark);
                break;
            case "Section":
                if (HasProp("header"))
                    SkiaText.DrawLine(canvas, Str("header").ToUpperInvariant(), _content.Left, _content.Top + 16,
                        SkiaTheme.MakeFont("caption"), new SKColor(0x8E, 0x8E, 0x93));
                break;
            case "TextField" or "SecureField":
                PaintTextField(canvas, dark);
                break;
            case "TextEditor":
                PaintTextEditor(canvas, dark);
                break;
            case "Toggle":
                PaintToggle(canvas, dark);
                break;
            case "Slider":
                PaintSlider(canvas, dark);
                break;
            case "Stepper":
                PaintStepper(canvas, dark);
                break;
            case "Picker":
                PaintPicker(canvas, dark);
                break;
            case "DatePicker":
                PaintRowValue(canvas, dark, Str("label"), FormatDate(Num("value") ?? 0));
                break;
            case "ColorPicker":
                PaintColorPicker(canvas, dark);
                break;
            case "Menu":
                PaintRowValue(canvas, dark, Str("label"), "▾");
                break;
            case "DisclosureGroup":
                PaintDisclosureHeader(canvas, dark);
                break;
            case "ProgressView":
                PaintProgress(canvas, dark);
                break;
            case "Gauge":
                PaintGauge(canvas, dark);
                break;
            case "WebView":
                PaintWebView(canvas, dark);
                break;
            case "NavigationStack":
                PaintNavBar(canvas, dark, Children.Count > 0 ? Children[0].NavTitle() : "", back: false);
                break;
            default:
                if (_custom is { } r) r.Paint(RenderCtx(), canvas, _content);
                else if (!IsBuiltIn(Type))
                    DrawBlock(canvas, new() { "⚠️ " + Type }, _content, Font(), ForegroundColor(dark), null);
                break;
        }
    }

    // ---- primitives ----------------------------------------------------------

    void PaintButton(SKCanvas canvas, bool dark)
    {
        using var chrome = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 10, 10, chrome);
        var color = IsDisabled ? new SKColor(0x8E, 0x8E, 0x93) : (ForegroundColorOptional(dark) ?? SkiaTheme.Accent);
        DrawCentered(canvas, Str("title"), Frame, Font(), color);
    }

    void PaintShape(SKCanvas canvas, bool dark)
    {
        var fill = ForegroundColorOptional(dark) ?? SkiaTheme.Accent;
        var box = _content; // inset by any padding
        using var paint = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        switch (Type)
        {
            case "Rectangle": canvas.DrawRect(box, paint); break;
            case "RoundedRectangle":
                var r = (float)(Num("cornerRadius") ?? 8);
                canvas.DrawRoundRect(box, r, r, paint);
                break;
            case "Capsule":
                var cr = Math.Min(box.Width, box.Height) / 2;
                canvas.DrawRoundRect(box, cr, cr, paint);
                break;
            case "Circle":
                var d = Math.Min(box.Width, box.Height);
                canvas.DrawOval(new SKRect(box.MidX - d / 2, box.MidY - d / 2, box.MidX + d / 2, box.MidY + d / 2), paint);
                break;
        }
    }

    // ---- controls ------------------------------------------------------------

    void PaintTextField(SKCanvas canvas, bool dark)
    {
        using var box = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        var text = Str("text");
        var showPlaceholder = text.Length == 0;
        var display = Type == "SecureField" && !showPlaceholder ? new string('•', text.Length) : (showPlaceholder ? Str("placeholder") : text);
        var color = showPlaceholder ? new SKColor(0x8E, 0x8E, 0x93) : (dark ? SKColors.White : SKColors.Black);
        SkiaText.DrawLine(canvas, display, _content.Left + 10, Baseline(_content, Font()), Font(), color);
        if (_bridge.FocusedId == Id) DrawCaret(canvas, _content.Left + 10 + SkiaText.Measure(display, Font()), dark);
    }

    void PaintTextEditor(SKCanvas canvas, bool dark)
    {
        using var box = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        var font = Font();
        var lines = SkiaText.Wrap(Str("text"), font, _content.Width - 20);
        var y = _content.Top + 8 - font.Metrics.Ascent;
        var lh = font.Metrics.Descent - font.Metrics.Ascent;
        foreach (var line in lines) { SkiaText.DrawLine(canvas, line, _content.Left + 10, y, font, dark ? SKColors.White : SKColors.Black); y += lh; }
        if (_bridge.FocusedId == Id) DrawCaret(canvas, _content.Left + 10 + SkiaText.Measure(lines.Count > 0 ? lines[^1] : "", font), dark);
    }

    void DrawCaret(SKCanvas canvas, float x, bool dark)
    {
        using var p = new SKPaint { Color = SkiaTheme.Accent, StrokeWidth = 2 };
        canvas.DrawLine(x + 1, _content.Top + 8, x + 1, _content.Top + 8 + Font().Size + 4, p);
    }

    void PaintToggle(SKCanvas canvas, bool dark)
    {
        SkiaText.DrawLine(canvas, Str("label"), _content.Left, Baseline(_content, Font()), Font(), dark ? SKColors.White : SKColors.Black);
        var on = Bool("value");
        var w = 50f; var h = 30f;
        var track = new SKRect(_content.Right - w, _content.MidY - h / 2, _content.Right, _content.MidY + h / 2);
        using var tp = new SKPaint { Color = on ? new SKColor(0x34, 0xC7, 0x59) : new SKColor(0x78, 0x78, 0x80, 0x66), IsAntialias = true };
        canvas.DrawRoundRect(track, h / 2, h / 2, tp);
        using var knob = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var kx = on ? track.Right - h / 2 : track.Left + h / 2;
        canvas.DrawCircle(kx, track.MidY, h / 2 - 2, knob);
    }

    void PaintSlider(SKCanvas canvas, bool dark)
    {
        var min = Num("min") ?? 0; var max = Num("max") ?? 1; var val = Num("value") ?? 0;
        var t = (float)Math.Clamp((val - min) / Math.Max(0.0001, max - min), 0, 1);
        var left = _content.Left + 10; var right = _content.Right - 10; var y = _content.MidY;
        using var track = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 4, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(left, y, right, y, track);
        var knobX = left + t * (right - left);
        using var fill = new SKPaint { Color = SkiaTheme.Accent, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(left, y, knobX, y, fill);
        using var knob = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var knobEdge = new SKPaint { Color = SkiaTheme.Separator(dark), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawCircle(knobX, y, 11, knob);
        canvas.DrawCircle(knobX, y, 11, knobEdge);
    }

    void PaintStepper(SKCanvas canvas, bool dark)
    {
        SkiaText.DrawLine(canvas, Str("label") + " " + ((int)(Num("value") ?? 0)), _content.Left, Baseline(_content, Font()), Font(), dark ? SKColors.White : SKColors.Black);
        DrawPillButton(canvas, dark, new SKRect(_content.Right - 76, _content.MidY - 15, _content.Right - 44, _content.MidY + 15), "−");
        DrawPillButton(canvas, dark, new SKRect(_content.Right - 32, _content.MidY - 15, _content.Right, _content.MidY + 15), "+");
    }

    void DrawPillButton(SKCanvas canvas, bool dark, SKRect r, string glyph)
    {
        using var p = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(r, 6, 6, p);
        DrawCentered(canvas, glyph, r, SkiaTheme.MakeFont("headline"), dark ? SKColors.White : SKColors.Black);
    }

    void PaintPicker(SKCanvas canvas, bool dark)
    {
        var sel = (int)(Num("selection") ?? 0);
        var value = sel >= 0 && sel < Children.Count ? Children[sel].Str("text") : "";
        PaintRowValue(canvas, dark, Str("label"), value + "  ▾");
    }

    void PaintColorPicker(SKCanvas canvas, bool dark)
    {
        SkiaText.DrawLine(canvas, Str("label"), _content.Left, Baseline(_content, Font()), Font(), dark ? SKColors.White : SKColors.Black);
        var swatch = new SKRect(_content.Right - 34, _content.MidY - 13, _content.Right, _content.MidY + 13);
        using var p = new SKPaint { Color = SkiaTheme.Color(Str("value"), dark), IsAntialias = true };
        canvas.DrawRoundRect(swatch, 6, 6, p);
        using var edge = new SKPaint { Color = SkiaTheme.Separator(dark), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(swatch, 6, 6, edge);
    }

    void PaintRowValue(SKCanvas canvas, bool dark, string label, string value)
    {
        SkiaText.DrawLine(canvas, label, _content.Left, Baseline(_content, Font()), Font(), dark ? SKColors.White : SKColors.Black);
        var w = SkiaText.Measure(value, Font());
        SkiaText.DrawLine(canvas, value, _content.Right - w, Baseline(_content, Font()), Font(), new SKColor(0x8E, 0x8E, 0x93));
    }

    void PaintDisclosureHeader(SKCanvas canvas, bool dark)
    {
        var chevron = Bool("expanded") ? "▾" : "▸";
        SkiaText.DrawLine(canvas, chevron + "  " + Str("label"), _content.Left, _content.Top + 24, Font(), dark ? SKColors.White : SKColors.Black);
    }

    void PaintProgress(SKCanvas canvas, bool dark)
    {
        var top = _content.Top;
        if (HasProp("label")) { SkiaText.DrawLine(canvas, Str("label"), _content.Left, top + 14, SkiaTheme.MakeFont("caption"), new SKColor(0x8E, 0x8E, 0x93)); top += 24; }
        var y = top + 3;
        var bar = new SKRect(_content.Left, y - 3, _content.Right, y + 3);
        using var bg = new SKPaint { Color = SkiaTheme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(bar, 3, 3, bg);
        var frac = (float)Math.Clamp(Num("value") ?? 0.3, 0, 1);
        using var fg = new SKPaint { Color = SkiaTheme.Accent, IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(bar.Left, bar.Top, bar.Left + bar.Width * frac, bar.Bottom), 3, 3, fg);
    }

    void PaintGauge(SKCanvas canvas, bool dark)
    {
        var top = _content.Top;
        if (HasProp("label")) { SkiaText.DrawLine(canvas, Str("label"), _content.Left, top + 14, SkiaTheme.MakeFont("caption"), new SKColor(0x8E, 0x8E, 0x93)); top += 22; }
        var min = Num("min") ?? 0; var max = Num("max") ?? 1; var val = Num("value") ?? 0;
        var frac = (float)Math.Clamp((val - min) / Math.Max(0.0001, max - min), 0, 1);
        var y = top + 6;
        var bar = new SKRect(_content.Left, y - 5, _content.Right, y + 5);
        using var bg = new SKPaint { Color = SkiaTheme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(bar, 5, 5, bg);
        using var fg = new SKPaint { Color = new SKColor(0x34, 0xC7, 0x59), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(bar.Left, bar.Top, bar.Left + bar.Width * frac, bar.Bottom), 5, 5, fg);
    }

    void PaintWebView(SKCanvas canvas, bool dark)
    {
        using var box = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        using var edge = new SKPaint { Color = SkiaTheme.Separator(dark), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, edge);
        var label = HasProp("url") ? "🌐  " + Str("url") : "🌐  HTML content";
        DrawCentered(canvas, label + "\n(native WebView — not drawable on a canvas)", Frame, SkiaTheme.MakeFont("caption"), new SKColor(0x8E, 0x8E, 0x93));
    }

    // ---- tab bar / scrollbar -------------------------------------------------

    void PaintTabBar(SKCanvas canvas, bool dark)
    {
        if (Paged)
        {
            // page dots
            var n = Children.Count;
            var cy = _content.Bottom - 14;
            var spacing = 16f;
            var startX = _content.MidX - (n - 1) * spacing / 2;
            for (var i = 0; i < n; i++)
            {
                using var p = new SKPaint { Color = i == _tabIndex ? SkiaTheme.Accent : SkiaTheme.Separator(dark), IsAntialias = true };
                canvas.DrawCircle(startX + i * spacing, cy, 4, p);
            }
            return;
        }

        var barTop = _content.Bottom - TabBarHeight;
        using var barBg = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(new SKRect(_content.Left, barTop, _content.Right, _content.Bottom), barBg);
        using var sep = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(_content.Left, barTop, _content.Right, barTop, sep);

        var n2 = Children.Count;
        var cellW = _content.Width / Math.Max(1, n2);
        var iconFont = IconFont(20);
        var titleFont = SkiaTheme.MakeFont("caption");
        for (var i = 0; i < n2; i++)
        {
            var cx = _content.Left + cellW * (i + 0.5f);
            var selected = i == _tabIndex;
            var color = selected ? SkiaTheme.Accent : new SKColor(0x8E, 0x8E, 0x93);
            var icon = SkiaTheme.Icon(Children[i].Str("systemImage"));
            var iw = SkiaText.Measure(icon, iconFont);
            SkiaText.DrawLine(canvas, icon, cx - iw / 2, barTop + 24, iconFont, color);
            var title = Children[i].Str("title");
            var tw = SkiaText.Measure(title, titleFont);
            SkiaText.DrawLine(canvas, title, cx - tw / 2, barTop + 44, titleFont, color);
        }
    }

    void PaintScrollbar(SKCanvas canvas, bool dark)
    {
        if (ScrollMax <= 0) return;
        var trackH = _content.Height;
        var thumbH = Math.Max(30, trackH * (_content.Height / (_naturalHeight)));
        var t = ScrollOffset / ScrollMax;
        var y = _content.Top + t * (trackH - thumbH);
        using var p = new SKPaint { Color = new SKColor(0x8E, 0x8E, 0x93, 0x99), IsAntialias = true };
        canvas.DrawRoundRect(new SKRect(_content.Right - 4, y, _content.Right - 1, y + thumbH), 2, 2, p);
    }

    // ---- text helpers --------------------------------------------------------

    void DrawBlock(SKCanvas canvas, List<string> lines, SKRect rect, SKFont font, SKColor color, string? align)
    {
        var m = font.Metrics;
        var lh = m.Descent - m.Ascent;
        var y = rect.Top - m.Ascent;
        foreach (var line in lines)
        {
            var w = SkiaText.Measure(line, font);
            var x = align is "center" ? rect.MidX - w / 2 : align is "trailing" ? rect.Right - w : rect.Left;
            SkiaText.DrawLine(canvas, line, x, y, font, color);
            y += lh;
        }
    }

    static void DrawCentered(SKCanvas canvas, string text, SKRect rect, SKFont font, SKColor color)
    {
        var lines = text.Split('\n');
        var m = font.Metrics;
        var lh = m.Descent - m.Ascent;
        var y = rect.MidY - lh * lines.Length / 2 - m.Ascent;
        foreach (var line in lines)
        {
            var w = SkiaText.Measure(line, font);
            SkiaText.DrawLine(canvas, line, rect.MidX - w / 2, y, font, color);
            y += lh;
        }
    }

    void PaintNavBar(SKCanvas canvas, bool dark, string title, bool back)
    {
        var bar = new SKRect(Frame.Left, Frame.Top, Frame.Right, Frame.Top + NavBarHeight);
        using var bg = new SKPaint { Color = SkiaTheme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(bar, bg);
        using var sep = new SKPaint { Color = SkiaTheme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(bar.Left, bar.Bottom, bar.Right, bar.Bottom, sep);
        if (back)
            SkiaText.DrawLine(canvas, "‹ Back", bar.Left + 12, Baseline(bar, SkiaTheme.MakeFont("body")), SkiaTheme.MakeFont("body"), SkiaTheme.Accent);
        DrawCentered(canvas, title, bar, SkiaTheme.MakeFont("headline"), dark ? SKColors.White : SKColors.Black);
    }

    static float Baseline(SKRect rect, SKFont font)
    {
        var m = font.Metrics;
        return rect.MidY - (m.Ascent + m.Descent) / 2;
    }

    static string FormatDate(double unixSeconds)
        => Epoch.AddSeconds(unixSeconds).ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
