using System.Buffers;
using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SwiftDotNet.Watch;

/// <summary>Reflection-free writer for the loosely typed arguments of native service calls.</summary>
static class JsonValueWriter
{
    public static void Write(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
            case null: w.WriteNullValue(); break;
            case string s: w.WriteStringValue(s); break;
            case bool b: w.WriteBooleanValue(b); break;
            case int i: w.WriteNumberValue(i); break;
            case long l: w.WriteNumberValue(l); break;
            case float f: w.WriteNumberValue(f); break;
            case double d: w.WriteNumberValue(d); break;
            case decimal m: w.WriteNumberValue(m); break;
            case DateTime dt: w.WriteStringValue(dt.ToString("O", CultureInfo.InvariantCulture)); break;
            case DateTimeOffset dto: w.WriteStringValue(dto.ToString("O", CultureInfo.InvariantCulture)); break;
            case Enum e: w.WriteStringValue(e.ToString()); break;
            case JsonElement je: je.WriteTo(w); break;
            case IEnumerable<KeyValuePair<string, object?>> dict:
                w.WriteStartObject();
                foreach (var (k, v) in dict)
                {
                    w.WritePropertyName(k);
                    Write(w, v);
                }
                w.WriteEndObject();
                break;
            case IEnumerable seq:
                w.WriteStartArray();
                foreach (var item in seq)
                    Write(w, item);
                w.WriteEndArray();
                break;
            default:
                w.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    public static string ToJson(object? value)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var w = new Utf8JsonWriter(buffer))
            Write(w, value);
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
