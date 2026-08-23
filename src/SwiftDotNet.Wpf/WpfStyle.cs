using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
// Core declares `Color`, `Brush`, `Font` and `GradientStop` in this same namespace (SwiftDotNet). A simple name binds to
// the enclosing namespace's member before any using-imported one, so the WPF types are reached through
// these distinctly-named aliases (a same-name alias would itself collide with the namespace member —
// CS0576 — which is why they are renamed rather than plain).
using WpfBrush = System.Windows.Media.Brush;
using WpfColor = System.Windows.Media.Color;
using WpfGradientStop = System.Windows.Media.GradientStop;

namespace SwiftDotNet;

/// <summary>Maps SwiftDotNet tokens to WPF colors / fonts / brushes. The WPF twin of <c>WinStyle</c>.</summary>
static class WpfStyle
{
    public static WpfColor? Color(string? token)
    {
        if (token is null || token == "primary") return null;
        if (token.StartsWith('#')) return FromHex(token);
        return token switch
        {
            "secondary" => FromRgb(0x8E, 0x8E, 0x93),
            "red" => FromRgb(0xFF, 0x3B, 0x30),
            "green" => FromRgb(0x34, 0xC7, 0x59),
            "blue" => FromRgb(0x00, 0x7A, 0xFF),
            "accentColor" => FromRgb(0x7C, 0x4D, 0xFF),
            _ => null,
        };
    }

    public static SolidColorBrush? Brush(string? token) => Color(token) is { } c ? new SolidColorBrush(c) : null;

    /// <summary>
    /// F5: parse a Brush wire string ("linear:&lt;deg&gt;:&lt;c&gt;@&lt;loc&gt;;…" / "radial:&lt;c&gt;@&lt;loc&gt;;…")
    /// into a WPF brush. Unlike WinUI, WPF has had a real <see cref="RadialGradientBrush"/> forever, so
    /// the radial form is not approximated.
    /// </summary>
    public static WpfBrush? Gradient(string spec)
    {
        var firstColon = spec.IndexOf(':');
        if (firstColon < 0) return null;
        var kind = spec[..firstColon];
        var rest = spec[(firstColon + 1)..];
        if (kind == "linear")
        {
            var secondColon = rest.IndexOf(':');
            if (secondColon < 0) return null;
            var angle = double.TryParse(rest[..secondColon], NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 90;
            var stops = Stops(rest[(secondColon + 1)..]);
            if (stops is null) return null;
            var rad = angle * Math.PI / 180.0;
            // Relative-to-bounding-box is WPF's default mapping mode, so these are 0..1 fractions of the
            // painted area — the same convention the WinUI backend uses.
            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0.5 - Math.Cos(rad) * 0.5, 0.5 - Math.Sin(rad) * 0.5),
                EndPoint = new Point(0.5 + Math.Cos(rad) * 0.5, 0.5 + Math.Sin(rad) * 0.5),
            };
            foreach (var s in stops) brush.GradientStops.Add(s);
            return brush;
        }
        if (kind == "radial")
        {
            var stops = Stops(rest);
            if (stops is null) return null;
            var brush = new RadialGradientBrush();
            foreach (var s in stops) brush.GradientStops.Add(s);
            return brush;
        }
        return null;
    }

    static List<WpfGradientStop>? Stops(string spec)
    {
        var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var list = new List<WpfGradientStop>();
        foreach (var part in parts)
        {
            var at = part.LastIndexOf('@');
            if (at < 0) return null;
            var color = Color(part[..at]) ?? WpfColor.FromArgb(0xFF, 0, 0, 0);
            var loc = double.TryParse(part[(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
            list.Add(new WpfGradientStop(color, loc));
        }
        return list;
    }

    static WpfColor FromRgb(byte r, byte g, byte b) => WpfColor.FromArgb(0xFF, r, g, b);

    static WpfColor? FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, null, out var v))
            return null;
        return WpfColor.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static (double size, FontWeight weight)? Font(string? token) => token switch
    {
        "largeTitle" => (30, FontWeights.Bold),
        "title" => (24, FontWeights.Normal),
        "headline" => (17, FontWeights.SemiBold),
        "body" => (16, FontWeights.Normal),
        "caption" => (12, FontWeights.Normal),
        _ => null,
    };

    /// <summary>The <see cref="AnimationCurve"/> a wire curve token names, defaulting to ease-in-out.</summary>
    public static AnimationCurve CurveFor(string? token) => token switch
    {
        "linear" => AnimationCurve.Linear,
        "easeIn" => AnimationCurve.EaseIn,
        "easeOut" => AnimationCurve.EaseOut,
        "spring" => AnimationCurve.Spring,
        _ => AnimationCurve.EaseInOut,
    };

    /// <summary>
    /// A curve as the cubic control points of a <see cref="KeySpline"/>. Spring gets an overshooting
    /// spline (control y &gt; 1), the closest a bezier gets to the engine's decaying settle.
    /// </summary>
    public static KeySpline SplineFor(AnimationCurve curve) => curve switch
    {
        AnimationCurve.EaseIn => new KeySpline(0.42, 0, 1, 1),
        AnimationCurve.EaseOut => new KeySpline(0, 0, 0.58, 1),
        AnimationCurve.Spring => new KeySpline(0.34, 1.56, 0.64, 1),
        _ => new KeySpline(0.42, 0, 0.58, 1),
    };

    public static string Emoji(string name) => name switch
    {
        "star.fill" or "star" => "⭐",
        "heart.fill" or "heart" => "❤️",
        "bell" or "bell.fill" => "🔔",
        "checkmark" => "✅",
        "slider.horizontal.3" => "🎚️",
        "square.grid.2x2" => "▦",
        "rectangle.stack" => "🗂️",
        "list.bullet" => "☰",
        "arrow.forward.circle" => "➡️",
        _ => "•",
    };
}
