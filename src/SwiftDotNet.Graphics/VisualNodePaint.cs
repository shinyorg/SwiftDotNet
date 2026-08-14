using System.Globalization;
using SwiftDotNet;

namespace SwiftDotNet.Graphics;

/// <summary>The paint pass for <see cref="VisualNode"/> — decorations, per-type content, and controls.</summary>
sealed partial class VisualNode
{
    static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Draw(ICanvas canvas, bool dark)
    {
        var count = canvas.Save();
        ApplyScale(canvas);
        ApplyOffset(canvas);
        ApplyRotation(canvas);

        // Group opacity composites the whole subtree once. Fading each child independently instead would
        // let overlapping siblings show through one another.
        var opacity = Opacity();
        if (opacity < 1) canvas.SaveLayer((float)opacity);

        PaintDecorations(canvas, dark);
        PaintContent(canvas, dark);
        PaintChildren(canvas, dark);

        canvas.RestoreToCount(count);
    }

    void ApplyScale(ICanvas canvas)
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

    void ApplyOffset(ICanvas canvas)
    {
        if (Offset() is { } o) canvas.Translate((float)o.x, (float)o.y);
    }

    // Aspect-preserving placement of a source image into a destination rect (fit = contain, fill = cover).
    static Rect FitRect(int srcW, int srcH, Rect dst, bool fill)
    {
        if (srcW <= 0 || srcH <= 0) return dst;
        var scale = fill
            ? Math.Max(dst.Width / srcW, dst.Height / srcH)
            : Math.Min(dst.Width / srcW, dst.Height / srcH);
        var w = srcW * scale;
        var h = srcH * scale;
        var x = dst.MidX - w / 2f;
        var y = dst.MidY - h / 2f;
        return new Rect(x, y, x + w, y + h);
    }

    void ApplyRotation(ICanvas canvas)
    {
        if (Rotation() is not { } r || Math.Abs(r.degrees) < 0.0001) return;
        var ax = r.anchor is "leading" or "topLeading" or "bottomLeading" ? Frame.Left
            : r.anchor is "trailing" or "topTrailing" or "bottomTrailing" ? Frame.Right : Frame.MidX;
        var ay = r.anchor is "top" or "topLeading" or "topTrailing" ? Frame.Top
            : r.anchor is "bottom" or "bottomLeading" or "bottomTrailing" ? Frame.Bottom : Frame.MidY;
        canvas.RotateDegrees((float)r.degrees, ax, ay);
    }

    void PaintChildren(ICanvas canvas, bool dark)
    {
        switch (Type)
        {
            case "TabView":
                if (_tabIndex < Children.Count) Children[_tabIndex].Draw(canvas, dark);
                PaintTabBar(canvas, dark);
                return;
            case "DisclosureGroup":
                if (Bool("expanded")) foreach (var c in Children) c.Draw(canvas, dark);
                return;
            case "NavigationLink":
                if (Children.Count > 0) Children[0].Draw(canvas, dark);
                return;
            case "NavigationStack":
                if (Children.Count > 0) Children[0].Draw(canvas, dark); // pushed destination is drawn as an overlay
                return;
            case "Sheet" or "Alert" or "ActionSheet":
                if (Children.Count > 0) Children[0].Draw(canvas, dark); // presented content is drawn as an overlay
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
                // Viewport culling: rows arranged fully above/below the visible window are skipped, so a
                // long scrolled list only pays paint cost for the handful of rows actually on screen.
                foreach (var c in Children)
                {
                    if (!c.Frame.IntersectsWith(Frame)) continue;
                    if (c.Props.GetValueOrDefault("selected") as bool? == true)
                    {
                        var hl = new Paint { Color = Theme.Color("accentColor", dark).WithAlpha(40), IsAntialias = true };
                        canvas.DrawRect(new Rect(Frame.Left, c.Frame.Top, Frame.Right, c.Frame.Bottom), hl);
                    }
                    c.Draw(canvas, dark);
                }
                canvas.RestoreToCount(clip);
                PaintScrollbar(canvas, dark);
                return;
            }
            default:
                foreach (var c in Children) c.Draw(canvas, dark);
                return;
        }
    }

    // ---- decorations (background / shadow / border) --------------------------

    void PaintDecorations(ICanvas canvas, bool dark)
    {
        var radius = CornerRadius();

        // A gradient brush wins over a flat colour. Either way the shadow rides on the same paint, so it is
        // cast by the filled shape itself rather than drawn as a separate rect behind it.
        Paint? background =
            BackgroundGradient(dark) is { } gradient ? Paint.Fill(gradient) :
            BackgroundColor(dark) is { } bg ? Paint.Fill(bg) :
            null;

        if (background is { } fill)
            canvas.DrawRoundRect(Frame, radius, radius, fill.With(DropShadow()));

        // F6 material: real backdrop blur needs a surface snapshot the engine doesn't keep, so paint a
        // translucent tint (documented fallback). Sits above the background, below content.
        if (Mod("material") is { } mat)
        {
            var tint = (mat.GetValueOrDefault("value") as string) switch
            { "ultraThin" => 0.55, "thin" => 0.65, "thick" => 0.85, _ => 0.75 };
            var matDark = (mat.GetValueOrDefault("dark") as string) == "true";
            var baseColor = matDark ? new Color(20, 20, 22) : new Color(255, 255, 255);
            canvas.DrawRoundRect(Frame, radius, radius, Paint.Fill(baseColor.WithAlpha((byte)(tint * 255))));
        }

        // Form/List/Section rows sit on a grouped surface.
        if (Type is "Section" && HasProp("header"))
        {
            // header handled in PaintContent; the section body area uses a subtle surface.
        }

        if (Border(dark) is { } b)
            canvas.DrawRoundRect(Frame, radius, radius, Paint.Stroke(b.color, (float)b.width));
    }

    // ---- content -------------------------------------------------------------

    void PaintContent(ICanvas canvas, bool dark)
    {
        switch (Type)
        {
            case "Text":
                DrawBlock(canvas, _wrapLines ?? new List<string> { Str("text") }, _content, Font(), ForegroundColor(dark), AlignToken());
                break;
            case "Link":
                DrawBlock(canvas, new() { Str("title") }, _content, Font(), Theme.Color("blue", dark), AlignToken());
                break;
            case "Button":
                PaintButton(canvas, dark);
                break;
            case "Image":
                if (RasterImage() is { } img)
                {
                    var dst = FitRect(img.Width, img.Height, _content, Str("contentMode") == "fill");
                    var save = canvas.Save();
                    canvas.ClipRect(_content);
                    canvas.DrawImage(img, dst);
                    canvas.RestoreToCount(save);
                }
                else if (Str("system").Length > 0)
                    // Only fall back to a glyph when the app actually asked for an SF Symbol. A raster-only
                    // image that failed/hasn't loaded draws nothing, so callers can supply their own placeholder.
                    canvas.DrawText(Theme.Icon(Str("system")), _content.Left, Baseline(_content, IconFont(22)), IconFont(22), ForegroundColor(dark));
                break;
            case "Label":
                canvas.DrawText(Theme.Icon(Str("systemImage")) + "  " + Str("title"), _content.Left, Baseline(_content, Font()), Font(), ForegroundColor(dark));
                break;
            case "Divider":
                canvas.DrawLine(_content.Left, _content.MidY, _content.Right, _content.MidY,
                    Paint.Hairline(Theme.Separator(dark)));
                break;
            case "Rectangle" or "Circle" or "Capsule" or "RoundedRectangle":
                PaintShape(canvas, dark);
                break;
            case "Section":
                if (HasProp("header"))
                    canvas.DrawText(Str("header").ToUpperInvariant(), _content.Left, _content.Top + 16,
                        Theme.MakeFont("caption", Fonts), new Color(0x8E, 0x8E, 0x93));
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

    void PaintButton(ICanvas canvas, bool dark)
    {
        var chrome = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 10, 10, chrome);
        var color = IsDisabled ? new Color(0x8E, 0x8E, 0x93) : (ForegroundColorOptional(dark) ?? Theme.Accent);
        DrawCentered(canvas, Str("title"), Frame, Font(), color);
    }

    void PaintShape(ICanvas canvas, bool dark)
    {
        var fill = ForegroundColorOptional(dark) ?? Theme.Accent;
        var box = _content; // inset by any padding
        var paint = new Paint { Color = fill, IsAntialias = true, Style = PaintStyle.Fill };
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
                canvas.DrawOval(new Rect(box.MidX - d / 2, box.MidY - d / 2, box.MidX + d / 2, box.MidY + d / 2), paint);
                break;
        }
    }

    // ---- controls ------------------------------------------------------------

    void PaintTextField(ICanvas canvas, bool dark)
    {
        var box = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        var text = Str("text");
        var showPlaceholder = text.Length == 0;
        var display = Type == "SecureField" && !showPlaceholder ? new string('•', text.Length) : (showPlaceholder ? Str("placeholder") : text);
        var color = showPlaceholder ? new Color(0x8E, 0x8E, 0x93) : (dark ? Colors.White : Colors.Black);
        canvas.DrawText(display, _content.Left + 10, Baseline(_content, Font()), Font(), color);
        if (_bridge.FocusedId == Id) DrawCaret(canvas, _content.Left + 10 + Fonts.Measure(display, Font()), dark);
    }

    void PaintTextEditor(ICanvas canvas, bool dark)
    {
        var box = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        var font = Font();
        var lines = TextLayout.Wrap(Str("text"), font, _content.Width - 20, Fonts);
        var y = _content.Top + 8 - font.Metrics.Ascent;
        var lh = font.Metrics.Descent - font.Metrics.Ascent;
        foreach (var line in lines) { canvas.DrawText(line, _content.Left + 10, y, font, dark ? Colors.White : Colors.Black); y += lh; }
        if (_bridge.FocusedId == Id) DrawCaret(canvas, _content.Left + 10 + Fonts.Measure(lines.Count > 0 ? lines[^1] : "", font), dark);
    }

    void DrawCaret(ICanvas canvas, float x, bool dark)
    {
        var p = new Paint { Color = Theme.Accent, StrokeWidth = 2 };
        canvas.DrawLine(x + 1, _content.Top + 8, x + 1, _content.Top + 8 + Font().Size + 4, p);
    }

    void PaintToggle(ICanvas canvas, bool dark)
    {
        canvas.DrawText(Str("label"), _content.Left, Baseline(_content, Font()), Font(), dark ? Colors.White : Colors.Black);
        var on = Bool("value");
        var w = 50f; var h = 30f;
        var track = new Rect(_content.Right - w, _content.MidY - h / 2, _content.Right, _content.MidY + h / 2);
        var tp = new Paint { Color = on ? new Color(0x34, 0xC7, 0x59) : new Color(0x78, 0x78, 0x80, 0x66), IsAntialias = true };
        canvas.DrawRoundRect(track, h / 2, h / 2, tp);
        var knob = new Paint { Color = Colors.White, IsAntialias = true };
        var kx = on ? track.Right - h / 2 : track.Left + h / 2;
        canvas.DrawCircle(kx, track.MidY, h / 2 - 2, knob);
    }

    void PaintSlider(ICanvas canvas, bool dark)
    {
        var min = Num("min") ?? 0; var max = Num("max") ?? 1; var val = Num("value") ?? 0;
        var t = (float)Math.Clamp((val - min) / Math.Max(0.0001, max - min), 0, 1);
        var left = _content.Left + 10; var right = _content.Right - 10; var y = _content.MidY;
        var track = new Paint { Color = Theme.Separator(dark), StrokeWidth = 4, StrokeCap = StrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(left, y, right, y, track);
        var knobX = left + t * (right - left);
        var fill = new Paint { Color = Theme.Accent, StrokeWidth = 4, StrokeCap = StrokeCap.Round, IsAntialias = true };
        canvas.DrawLine(left, y, knobX, y, fill);
        var knob = new Paint { Color = Colors.White, IsAntialias = true };
        var knobEdge = new Paint { Color = Theme.Separator(dark), IsAntialias = true, Style = PaintStyle.Stroke, StrokeWidth = 1 };
        canvas.DrawCircle(knobX, y, 11, knob);
        canvas.DrawCircle(knobX, y, 11, knobEdge);
    }

    void PaintStepper(ICanvas canvas, bool dark)
    {
        canvas.DrawText(Str("label") + " " + ((int)(Num("value") ?? 0)), _content.Left, Baseline(_content, Font()), Font(), dark ? Colors.White : Colors.Black);
        DrawPillButton(canvas, dark, new Rect(_content.Right - 76, _content.MidY - 15, _content.Right - 44, _content.MidY + 15), "−");
        DrawPillButton(canvas, dark, new Rect(_content.Right - 32, _content.MidY - 15, _content.Right, _content.MidY + 15), "+");
    }

    void DrawPillButton(ICanvas canvas, bool dark, Rect r, string glyph)
    {
        var p = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(r, 6, 6, p);
        DrawCentered(canvas, glyph, r, Theme.MakeFont("headline", Fonts), dark ? Colors.White : Colors.Black);
    }

    void PaintPicker(ICanvas canvas, bool dark)
    {
        var sel = (int)(Num("selection") ?? 0);
        var value = sel >= 0 && sel < Children.Count ? Children[sel].Str("text") : "";
        PaintRowValue(canvas, dark, Str("label"), value + "  ▾");
    }

    void PaintColorPicker(ICanvas canvas, bool dark)
    {
        canvas.DrawText(Str("label"), _content.Left, Baseline(_content, Font()), Font(), dark ? Colors.White : Colors.Black);
        var swatch = new Rect(_content.Right - 34, _content.MidY - 13, _content.Right, _content.MidY + 13);
        var p = new Paint { Color = Theme.Color(Str("value"), dark), IsAntialias = true };
        canvas.DrawRoundRect(swatch, 6, 6, p);
        var edge = new Paint { Color = Theme.Separator(dark), Style = PaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(swatch, 6, 6, edge);
    }

    void PaintRowValue(ICanvas canvas, bool dark, string label, string value)
    {
        canvas.DrawText(label, _content.Left, Baseline(_content, Font()), Font(), dark ? Colors.White : Colors.Black);
        var w = Fonts.Measure(value, Font());
        canvas.DrawText(value, _content.Right - w, Baseline(_content, Font()), Font(), new Color(0x8E, 0x8E, 0x93));
    }

    void PaintDisclosureHeader(ICanvas canvas, bool dark)
    {
        var chevron = Bool("expanded") ? "▾" : "▸";
        canvas.DrawText(chevron + "  " + Str("label"), _content.Left, _content.Top + 24, Font(), dark ? Colors.White : Colors.Black);
    }

    void PaintProgress(ICanvas canvas, bool dark)
    {
        var top = _content.Top;
        if (HasProp("label")) { canvas.DrawText(Str("label"), _content.Left, top + 14, Theme.MakeFont("caption", Fonts), new Color(0x8E, 0x8E, 0x93)); top += 24; }
        var y = top + 3;
        var bar = new Rect(_content.Left, y - 3, _content.Right, y + 3);
        var bg = new Paint { Color = Theme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(bar, 3, 3, bg);
        var frac = (float)Math.Clamp(Num("value") ?? 0.3, 0, 1);
        var fg = new Paint { Color = Theme.Accent, IsAntialias = true };
        canvas.DrawRoundRect(new Rect(bar.Left, bar.Top, bar.Left + bar.Width * frac, bar.Bottom), 3, 3, fg);
    }

    void PaintGauge(ICanvas canvas, bool dark)
    {
        var top = _content.Top;
        if (HasProp("label")) { canvas.DrawText(Str("label"), _content.Left, top + 14, Theme.MakeFont("caption", Fonts), new Color(0x8E, 0x8E, 0x93)); top += 22; }
        var min = Num("min") ?? 0; var max = Num("max") ?? 1; var val = Num("value") ?? 0;
        var frac = (float)Math.Clamp((val - min) / Math.Max(0.0001, max - min), 0, 1);
        var y = top + 6;
        var bar = new Rect(_content.Left, y - 5, _content.Right, y + 5);
        var bg = new Paint { Color = Theme.Separator(dark), IsAntialias = true };
        canvas.DrawRoundRect(bar, 5, 5, bg);
        var fg = new Paint { Color = new Color(0x34, 0xC7, 0x59), IsAntialias = true };
        canvas.DrawRoundRect(new Rect(bar.Left, bar.Top, bar.Left + bar.Width * frac, bar.Bottom), 5, 5, fg);
    }

    void PaintWebView(ICanvas canvas, bool dark)
    {
        var box = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, box);
        var edge = new Paint { Color = Theme.Separator(dark), Style = PaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        canvas.DrawRoundRect(Frame, 8, 8, edge);
        var label = HasProp("url") ? "🌐  " + Str("url") : "🌐  HTML content";
        DrawCentered(canvas, label + "\n(native WebView — not drawable on a canvas)", Frame, Theme.MakeFont("caption", Fonts), new Color(0x8E, 0x8E, 0x93));
    }

    // ---- tab bar / scrollbar -------------------------------------------------

    void PaintTabBar(ICanvas canvas, bool dark)
    {
        if (Paged)
        {
            // page dots (hidden when .HidePageIndicator() sets pageIndicator=false)
            if (HasProp("pageIndicator") && !Bool("pageIndicator")) return;
            var n = Children.Count;
            var cy = _content.Bottom - 14;
            var spacing = 16f;
            var startX = _content.MidX - (n - 1) * spacing / 2;
            for (var i = 0; i < n; i++)
            {
                var p = new Paint { Color = i == _tabIndex ? Theme.Accent : Theme.Separator(dark), IsAntialias = true };
                canvas.DrawCircle(startX + i * spacing, cy, 4, p);
            }
            return;
        }

        var barTop = _content.Bottom - TabBarHeight;
        var barBg = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(new Rect(_content.Left, barTop, _content.Right, _content.Bottom), barBg);
        var sep = new Paint { Color = Theme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(_content.Left, barTop, _content.Right, barTop, sep);

        var n2 = Children.Count;
        var cellW = _content.Width / Math.Max(1, n2);
        var iconFont = IconFont(20);
        var titleFont = Theme.MakeFont("caption", Fonts);
        for (var i = 0; i < n2; i++)
        {
            var cx = _content.Left + cellW * (i + 0.5f);
            var selected = i == _tabIndex;
            var color = selected ? Theme.Accent : new Color(0x8E, 0x8E, 0x93);
            var icon = Theme.Icon(Children[i].Str("systemImage"));
            var iw = Fonts.Measure(icon, iconFont);
            canvas.DrawText(icon, cx - iw / 2, barTop + 24, iconFont, color);
            var title = Children[i].Str("title");
            var tw = Fonts.Measure(title, titleFont);
            canvas.DrawText(title, cx - tw / 2, barTop + 44, titleFont, color);
        }
    }

    void PaintScrollbar(ICanvas canvas, bool dark)
    {
        if (ScrollMax <= 0) return;
        var trackH = _content.Height;
        var thumbH = Math.Max(30, trackH * (_content.Height / (_naturalHeight)));
        var t = ScrollOffset / ScrollMax;
        var y = _content.Top + t * (trackH - thumbH);
        var p = new Paint { Color = new Color(0x8E, 0x8E, 0x93, 0x99), IsAntialias = true };
        canvas.DrawRoundRect(new Rect(_content.Right - 4, y, _content.Right - 1, y + thumbH), 2, 2, p);
    }

    // ---- text helpers --------------------------------------------------------

    void DrawBlock(ICanvas canvas, List<string> lines, Rect rect, Font font, Color color, string? align)
    {
        var m = font.Metrics;
        var lh = m.Descent - m.Ascent;
        var y = rect.Top - m.Ascent;
        foreach (var line in lines)
        {
            var w = Fonts.Measure(line, font);
            var x = align is "center" ? rect.MidX - w / 2 : align is "trailing" ? rect.Right - w : rect.Left;
            canvas.DrawText(line, x, y, font, color);
            y += lh;
        }
    }

    void DrawCentered(ICanvas canvas, string text, Rect rect, Font font, Color color)
    {
        var lines = text.Split('\n');
        var m = font.Metrics;
        var lh = m.Descent - m.Ascent;
        var y = rect.MidY - lh * lines.Length / 2 - m.Ascent;
        foreach (var line in lines)
        {
            var w = Fonts.Measure(line, font);
            canvas.DrawText(line, rect.MidX - w / 2, y, font, color);
            y += lh;
        }
    }

    void PaintNavBar(ICanvas canvas, bool dark, string title, bool back)
    {
        var bar = new Rect(Frame.Left, Frame.Top, Frame.Right, Frame.Top + NavBarHeight);
        var bg = new Paint { Color = Theme.Surface(dark), IsAntialias = true };
        canvas.DrawRect(bar, bg);
        var sep = new Paint { Color = Theme.Separator(dark), StrokeWidth = 1 };
        canvas.DrawLine(bar.Left, bar.Bottom, bar.Right, bar.Bottom, sep);
        if (back)
            canvas.DrawText("‹ Back", bar.Left + 12, Baseline(bar, Theme.MakeFont("body", Fonts)), Theme.MakeFont("body", Fonts), Theme.Accent);
        DrawCentered(canvas, title, bar, Theme.MakeFont("headline", Fonts), dark ? Colors.White : Colors.Black);
    }

    static float Baseline(Rect rect, Font font)
    {
        var m = font.Metrics;
        return rect.MidY - (m.Ascent + m.Descent) / 2;
    }

    static string FormatDate(double unixSeconds)
        => Epoch.AddSeconds(unixSeconds).ToLocalTime().ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
}
