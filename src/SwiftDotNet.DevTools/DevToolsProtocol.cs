using System.Text;

namespace SwiftDotNet;

/// <summary>
/// The framing for the dev-tools socket. One frame is an ASCII header line followed by exactly
/// <c>length</c> bytes of payload:
///
/// <code>
/// SDN1 patch 512\n
/// {"op":"replace",...}
/// </code>
///
/// Length-prefixed rather than newline-delimited because the same socket carries both the patch JSON
/// (text, and guaranteed newline-free — <c>NodeJson</c> emits compact JSON) and PNG frames from the
/// preview host (binary, full of newline bytes). One framing for both means one reader on the IDE side.
///
/// The header is deliberately ASCII and human-readable: this is a debugging channel, and being able to
/// point <c>nc</c> at it and see what is going on is worth more than a few bytes.
/// </summary>
public static class DevToolsProtocol
{
    /// <summary>Protocol marker. Bump when the frame layout changes incompatibly.</summary>
    public const string Magic = "SDN1";

    /// <summary>Frames the app sends to the IDE.</summary>
    public static class ServerFrames
    {
        /// <summary>First frame on every connection: <c>backend=skia;pid=1234;protocol=1</c>.</summary>
        public const string Hello = "hello";
        /// <summary>A render patch, exactly as it was handed to the bridge.</summary>
        public const string Patch = "patch";
        /// <summary>A host→C# event: <c>nodeId\tvalue</c>.</summary>
        public const string Event = "event";
        /// <summary>A rendered preview frame (PNG bytes).</summary>
        public const string Frame = "frame";
        /// <summary>Free text for the IDE's log view.</summary>
        public const string Log = "log";
    }

    /// <summary>Frames the IDE sends to the app.</summary>
    public static class ClientFrames
    {
        public const string Ping = "ping";
        /// <summary>Preview input: <c>tap x y</c>, <c>scroll x y dy</c>, <c>text …</c>, <c>key name</c>.</summary>
        public const string Input = "input";
        /// <summary>Preview surface size: <c>width height</c>.</summary>
        public const string Resize = "resize";
        /// <summary>Preview colour scheme: <c>light</c> or <c>dark</c>.</summary>
        public const string Theme = "theme";
        /// <summary>Re-load the view assembly from disk.</summary>
        public const string Reload = "reload";
    }

    /// <summary>A single frame. <see cref="Payload"/> is UTF-8 text for every type except <c>frame</c>.</summary>
    public readonly struct Frame(string type, byte[] payload)
    {
        public string Type { get; } = type;
        public byte[] Payload { get; } = payload;

        public string Text => Encoding.UTF8.GetString(Payload);

        public override string ToString() => $"{Type}[{Payload.Length}]";
    }

    public static void Write(Stream stream, string type, ReadOnlySpan<byte> payload)
    {
        var header = Encoding.ASCII.GetBytes($"{Magic} {type} {payload.Length}\n");
        // One Write per frame would interleave two frames from different threads mid-header, so the
        // header and payload go out as a single buffer and callers only have to lock around this call.
        var buffer = new byte[header.Length + payload.Length];
        header.CopyTo(buffer, 0);
        payload.CopyTo(buffer.AsSpan(header.Length));
        stream.Write(buffer, 0, buffer.Length);
        stream.Flush();
    }

    public static void Write(Stream stream, string type, string payload)
        => Write(stream, type, Encoding.UTF8.GetBytes(payload));

    /// <summary>
    /// Reads one frame, blocking until it is complete. Returns null at end of stream.
    /// </summary>
    /// <exception cref="InvalidDataException">The header was malformed — the stream is unusable.</exception>
    public static Frame? Read(Stream stream)
    {
        var header = ReadHeaderLine(stream);
        if (header is null)
            return null;

        var parts = header.Split(' ');
        if (parts.Length != 3 || parts[0] != Magic)
            throw new InvalidDataException($"Bad dev-tools frame header: '{header}'");
        if (!int.TryParse(parts[2], out var length) || length < 0)
            throw new InvalidDataException($"Bad dev-tools frame length: '{header}'");

        var payload = new byte[length];
        var read = 0;
        while (read < length)
        {
            // A TCP read returns what has arrived, not what was asked for; a large PNG frame reliably
            // arrives in pieces, so this loop is load-bearing rather than defensive.
            var n = stream.Read(payload, read, length - read);
            if (n <= 0)
                return null;
            read += n;
        }

        return new Frame(parts[1], payload);
    }

    static string? ReadHeaderLine(Stream stream)
    {
        var sb = new StringBuilder(48);
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
                return sb.Length == 0 ? null : throw new InvalidDataException("Truncated dev-tools header");
            if (b == '\n')
                return sb.ToString();
            sb.Append((char)b);

            if (sb.Length > 256)
                throw new InvalidDataException("Dev-tools header line too long");
        }
    }
}
