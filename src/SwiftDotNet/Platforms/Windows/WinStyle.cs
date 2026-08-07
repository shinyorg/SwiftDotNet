using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

// Core declares a static `Color` class in this same namespace (SwiftDotNet), which shadows the
// using-imported Windows.UI.Color in type positions — hence the distinctly-named alias.
using WinColor = Windows.UI.Color;

namespace SwiftDotNet;

/// <summary>Maps SwiftDotNet tokens to WinUI colors/fonts/brushes.</summary>
static class WinStyle
{
    public static WinColor? Color(string? token)
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

    // F5: parse a Brush wire string ("linear:<deg>:<c>@<loc>;…" / "radial:<c>@<loc>;…") into a WinUI brush.
    // WinUI has no built-in radial gradient before the newer RadialGradientBrush; both map to a gradient brush.
    public static Microsoft.UI.Xaml.Media.Brush? Gradient(string spec)
    {
        var firstColon = spec.IndexOf(':');
        if (firstColon < 0) return null;
        var kind = spec[..firstColon];
        var rest = spec[(firstColon + 1)..];
        if (kind == "linear")
        {
            var secondColon = rest.IndexOf(':');
            if (secondColon < 0) return null;
            var angle = double.TryParse(rest[..secondColon], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var a) ? a : 90;
            var stops = Stops(rest[(secondColon + 1)..]);
            if (stops is null) return null;
            var rad = angle * Math.PI / 180.0;
            var brush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0.5 - Math.Cos(rad) * 0.5, 0.5 - Math.Sin(rad) * 0.5),
                EndPoint = new Windows.Foundation.Point(0.5 + Math.Cos(rad) * 0.5, 0.5 + Math.Sin(rad) * 0.5),
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

    static List<GradientStop>? Stops(string spec)
    {
        var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var list = new List<GradientStop>();
        foreach (var part in parts)
        {
            var at = part.LastIndexOf('@');
            if (at < 0) return null;
            var color = Color(part[..at]) ?? Windows.UI.Color.FromArgb(0xFF, 0, 0, 0);
            var loc = double.TryParse(part[(at + 1)..], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : 0;
            list.Add(new GradientStop { Color = color, Offset = loc });
        }
        return list;
    }

    static WinColor FromRgb(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    static WinColor? FromHex(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return null;
        return Windows.UI.Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
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
    /// A curve as the cubic control points of a WinUI <c>KeySpline</c> — the only easing a keyframed
    /// storyboard can express. Spring gets an overshooting spline (control y &gt; 1), the closest a
    /// bezier gets to the engine's decaying settle.
    /// </summary>
    public static KeySpline SplineFor(AnimationCurve curve) => curve switch
    {
        AnimationCurve.EaseIn => new KeySpline { ControlPoint1 = new(0.42, 0), ControlPoint2 = new(1, 1) },
        AnimationCurve.EaseOut => new KeySpline { ControlPoint1 = new(0, 0), ControlPoint2 = new(0.58, 1) },
        AnimationCurve.Spring => new KeySpline { ControlPoint1 = new(0.34, 1.56), ControlPoint2 = new(0.64, 1) },
        _ => new KeySpline { ControlPoint1 = new(0.42, 0), ControlPoint2 = new(0.58, 1) },
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
