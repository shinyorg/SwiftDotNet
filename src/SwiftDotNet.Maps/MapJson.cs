using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// Hand-rolled JSON for the map payloads carried as string props (the Phase-1 "JSON-string prop"
/// shortcut — no wire-model change). Zero reflection → trim/AOT-safe, matching <c>NodeJson</c>'s style.
/// The per-backend renderers parse these strings with their platform's JSON reader.
/// </summary>
internal static class MapJson
{
    public static string Camera(MapCamera c)
    {
        var sb = new StringBuilder(48);
        sb.Append("{\"lat\":").Append(Num(c.Center.Latitude))
          .Append(",\"lng\":").Append(Num(c.Center.Longitude))
          .Append(",\"zoom\":").Append(Num(c.Zoom)).Append('}');
        return sb.ToString();
    }

    public static string Pins(IReadOnlyList<MapPin> pins)
    {
        var sb = new StringBuilder(64);
        sb.Append('[');
        for (var i = 0; i < pins.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var p = pins[i];
            sb.Append("{\"lat\":").Append(Num(p.Coordinate.Latitude))
              .Append(",\"lng\":").Append(Num(p.Coordinate.Longitude))
              .Append(",\"draggable\":").Append(p.Draggable ? "true" : "false");
            if (p.Title is { } t) { sb.Append(",\"title\":"); Str(sb, t); }
            if (p.Tint is { } tint) { sb.Append(",\"tint\":"); Str(sb, tint.Value); }
            // A stable id lets renderers correlate taps/drags; default to the index when unset.
            sb.Append(",\"id\":"); Str(sb, p.Id ?? i.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }
        sb.Append(']');
        return sb.ToString();
    }

    public static string Polylines(IReadOnlyList<MapPolyline> lines)
    {
        var sb = new StringBuilder(96);
        sb.Append('[');
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var l = lines[i];
            sb.Append("{\"width\":").Append(Num(l.Width));
            if (l.Color is { } c) { sb.Append(",\"color\":"); Str(sb, c.Value); }
            sb.Append(",\"points\":[");
            for (var j = 0; j < l.Points.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append("{\"lat\":").Append(Num(l.Points[j].Latitude))
                  .Append(",\"lng\":").Append(Num(l.Points[j].Longitude)).Append('}');
            }
            sb.Append("]}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    static string Num(double v) => v.ToString(CultureInfo.InvariantCulture);

    static void Str(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (ch < 0x20) sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(ch);
                    break;
            }
        }
        sb.Append('"');
    }
}
