namespace SwiftDotNet;

/// <summary>
/// The app↔surface channel: how a running app hands state *to* a system-rendered surface, and how that
/// surface hands actions back.
///
/// **This is a mailbox, not a socket, and the API is shaped to say so.** On Apple the app and its widget
/// extension are almost never alive at the same time — the user taps a widget, the extension launches,
/// renders, and dies, while the app may have been suspended for hours. There is no live IPC between them.
/// What exists is durable shared storage (an App Group container) plus a "please look at it" nudge in each
/// direction (<c>WidgetCenter.reloadTimelines</c> outbound, a deep link or an appended action record
/// inbound). Every method here is therefore asynchronous and eventually-consistent.
///
/// On Android the same shape degenerates almost to a direct call: an <c>AppWidgetProvider</c> is a
/// <c>BroadcastReceiver</c> in our own process, so publishing is an <c>updateAppWidget</c> and an action
/// arrives live. That asymmetry is real and the API does not pretend otherwise — it is designed against
/// the *Apple* constraint, because an API shaped by Android's freedom could not be honoured on Apple.
///
/// Both the Live Activity and widget drivers use this. A Live Activity carries its own state inside the
/// ActivityKit payload, but it still needs the same mailbox to answer "which surfaces are live" and "what
/// did the user tap while we were suspended".
/// </summary>
public interface ISurfaceChannel
{
    /// <summary>
    /// Writes a snapshot to shared storage and nudges the host to re-render. Durable: it survives both
    /// processes dying, which is the normal case rather than the exception.
    /// </summary>
    Task PublishAsync(SurfaceSnapshot snapshot, CancellationToken ct = default);

    /// <summary>Removes a published surface's state and asks the host to stop showing it.</summary>
    Task WithdrawAsync(string kind, CancellationToken ct = default);

    /// <summary>Reads back the most recently published snapshot for a kind, or null.</summary>
    Task<SurfaceSnapshot?> ReadAsync(string kind, CancellationToken ct = default);

    /// <summary>
    /// What the user has actually installed. Publishing into the void is the normal state of affairs for
    /// widgets — most apps have none placed — so this exists to let callers skip the work entirely.
    /// </summary>
    Task<IReadOnlyList<SurfacePlacement>> GetPlacementsAsync(CancellationToken ct = default);

    /// <summary>
    /// Takes every action queued since the last drain, and clears the queue. Called on app foreground:
    /// on Apple these accumulated while the app was suspended, and on Android the queue is usually empty
    /// because the handler already ran in-process.
    /// </summary>
    Task<IReadOnlyList<SurfaceAction>> DrainActionsAsync(CancellationToken ct = default);

    /// <summary>Appends an action to the queue. Called by the platform's intent/receiver plumbing.</summary>
    Task PostActionAsync(SurfaceAction action, CancellationToken ct = default);
}

/// <summary>
/// One published surface state: a serialized tree per variant, plus the metadata a host needs to pick
/// the right one and know when it goes stale.
/// </summary>
public sealed record SurfaceSnapshot
{
    /// <summary>Stable id for this surface, e.g. <c>"delivery"</c>. Widgets and activities share one id space.</summary>
    public required string Kind { get; init; }

    /// <summary>Which system surface this is destined for.</summary>
    public required LiveSurface Surface { get; init; }

    /// <summary>
    /// The serialized trees, keyed by variant. A Live Activity's keys are slot names
    /// (<c>lockScreen</c>, <c>compactLeading</c>, …); a widget's are
    /// <c>{family}@{unix-seconds}</c>, one per family per timeline entry.
    /// </summary>
    public required IReadOnlyDictionary<string, string> Trees { get; init; }

    /// <summary>When this was published, as unix seconds.</summary>
    public required double PublishedAt { get; init; }

    /// <summary>
    /// When the host should come back for more, as unix seconds, or null for "no opinion".
    /// Apple maps it to <c>TimelineReloadPolicy.after(_:)</c>; Android to a one-shot WorkManager request.
    /// </summary>
    public double? RefreshAfter { get; init; }

    /// <summary>Total UTF-8 size of every tree in this snapshot.</summary>
    public int Bytes
    {
        get
        {
            var total = 0;
            foreach (var t in Trees.Values) total += LiveWire.ByteCount(t);
            return total;
        }
    }
}

/// <summary>An installed surface instance — one placed widget, or one running activity.</summary>
/// <param name="Kind">The surface id it was published under.</param>
/// <param name="Family">The shape the host placed, when it has one.</param>
/// <param name="NativeId">The platform's own handle (an <c>appWidgetId</c>, or an ActivityKit activity id).</param>
public readonly record struct SurfacePlacement(string Kind, WidgetFamily? Family, string NativeId);

/// <summary>
/// Something the user did on a surface, queued for the app to handle. Timestamped because it may be
/// drained long after the fact — on Apple, potentially after an app relaunch.
/// </summary>
/// <param name="Kind">Which surface it came from.</param>
/// <param name="NodeId">The structural id of the tapped node, matching <see cref="LivePayload.Actions"/>.</param>
/// <param name="Value">Optional payload; null for a plain button.</param>
/// <param name="At">When it happened, as unix seconds.</param>
public readonly record struct SurfaceAction(string Kind, string NodeId, string? Value, double At)
{
    /// <summary>
    /// The on-disk form. Deliberately a delimited line rather than JSON: the Swift side appends to this
    /// same file from a widget extension under memory pressure, and a one-line append with no parser is
    /// the cheapest correct thing on both sides. <c>|</c> and newlines are escaped.
    /// </summary>
    public string ToLine() =>
        $"{At.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}|{Escape(Kind)}|{Escape(NodeId)}|{Escape(Value ?? "")}";

    /// <summary>Parses <see cref="ToLine"/>. Returns false rather than throwing — a corrupt mailbox line
    /// must not take down app startup.</summary>
    public static bool TryParse(string line, out SurfaceAction action)
    {
        action = default;
        var parts = line.Split('|');
        if (parts.Length != 4) return false;
        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var at)) return false;

        var value = Unescape(parts[3]);
        action = new SurfaceAction(Unescape(parts[1]), Unescape(parts[2]), value.Length == 0 ? null : value, at);
        return true;
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("|", "\\p").Replace("\n", "\\n");

    static string Unescape(string s)
    {
        if (s.IndexOf('\\') < 0) return s;
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                switch (s[++i])
                {
                    case 'p': sb.Append('|'); break;
                    case 'n': sb.Append('\n'); break;
                    default: sb.Append(s[i]); break;
                }
            }
            else sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
