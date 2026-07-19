using System.Globalization;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>Parses a <see cref="Brush"/> wire string into an <see cref="SKShader"/> for the self-drawing backend (F5).</summary>
static class SkiaGradient
{
    public static SKShader? Shader(string spec, SKRect frame, bool dark)
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
            if (!Stops(rest[(secondColon + 1)..], dark, out var colors, out var positions)) return null;
            // Angle: 0° = left→right, 90° = top→bottom. Project the frame's half-diagonal onto that axis.
            var rad = angle * Math.PI / 180.0;
            var dx = (float)Math.Cos(rad) * frame.Width / 2f;
            var dy = (float)Math.Sin(rad) * frame.Height / 2f;
            var mid = new SKPoint(frame.MidX, frame.MidY);
            var start = new SKPoint(mid.X - dx, mid.Y - dy);
            var end = new SKPoint(mid.X + dx, mid.Y + dy);
            return SKShader.CreateLinearGradient(start, end, colors, positions, SKShaderTileMode.Clamp);
        }

        if (kind == "radial")
        {
            if (!Stops(rest, dark, out var colors, out var positions)) return null;
            var center = new SKPoint(frame.MidX, frame.MidY);
            var r = Math.Max(frame.Width, frame.Height) / 2f;
            return SKShader.CreateRadialGradient(center, r, colors, positions, SKShaderTileMode.Clamp);
        }

        return null;
    }

    static bool Stops(string spec, bool dark, out SKColor[] colors, out float[] positions)
    {
        var parts = spec.Split(';', StringSplitOptions.RemoveEmptyEntries);
        colors = new SKColor[parts.Length];
        positions = new float[parts.Length];
        if (parts.Length == 0) return false;
        for (var i = 0; i < parts.Length; i++)
        {
            var at = parts[i].LastIndexOf('@');
            if (at < 0) return false;
            colors[i] = SkiaTheme.Color(parts[i][..at], dark);
            positions[i] = float.TryParse(parts[i][(at + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var l) ? l : 0;
        }
        return true;
    }
}
