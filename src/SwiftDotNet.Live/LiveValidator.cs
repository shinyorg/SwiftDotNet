namespace SwiftDotNet;

/// <summary>Documented ceilings for the system surfaces. Every one of these is enforced by someone else.</summary>
public static class LiveBudget
{
    /// <summary>
    /// APNs caps a Live Activity update payload at 4 KB, and the whole <c>ContentState</c> rides inside it.
    /// Exceeding it does not throw — the update is rejected and the activity keeps showing stale content.
    /// </summary>
    public const int ActivityHardBytes = 4096;

    /// <summary>Where the validator starts warning, leaving room for the ActivityKit envelope around our tree.</summary>
    public const int ActivityWarnBytes = 2048;

    /// <summary>
    /// A notification crosses Binder, and the transaction budget is about 1 MB shared with everything else
    /// in flight. Oversized notifications are dropped by the system with a <c>Bad notification posted</c>
    /// log line and no exception, so we warn far below the real cliff.
    /// </summary>
    public const int NotificationWarnBytes = 256 * 1024;

    /// <summary>Widget payloads live in shared storage with no transport limit; this is a sanity ceiling.</summary>
    public const int WidgetWarnBytes = 512 * 1024;

    /// <summary>Largest bitmap we will ship inline, in pixels. ~400×256 at 3× density.</summary>
    public const int BitmapMaxPixels = 1200 * 768;

    /// <summary>
    /// A <c>RemoteViews</c> tree is rebuilt by reflection in SystemUI and deep nesting parcels badly.
    /// Not a hard limit — a practical one.
    /// </summary>
    public const int MaxDepth = 10;

    /// <summary>Android shows at most three notification action buttons.</summary>
    public const int MaxNotificationActions = 3;
}

/// <summary>How much a <see cref="LiveDiagnostic"/> matters.</summary>
public enum LiveSeverity
{
    /// <summary>Behaviour differs by platform but the surface still renders. Worth knowing at the call site.</summary>
    Info,
    /// <summary>Renders today, but is near a ceiling or silently degraded on one platform.</summary>
    Warning,
    /// <summary>Will not render correctly. <see cref="LiveValidator.Assert"/> throws on these.</summary>
    Error,
}

/// <summary>One validation finding, carrying the node it came from so the message can point at real code.</summary>
/// <param name="Code">Stable id, e.g. <c>SDNL001</c>. Greppable, and the eventual analyzer will reuse it.</param>
/// <param name="Severity">How much it matters.</param>
/// <param name="Message">What is wrong and what to do instead.</param>
/// <param name="NodeId">The offending node's structural id, when there is one.</param>
public readonly record struct LiveDiagnostic(string Code, LiveSeverity Severity, string Message, string? NodeId = null)
{
    public override string ToString() =>
        $"{Code} {Severity}: {Message}" + (NodeId is null ? "" : $" (node {NodeId})");
}

/// <summary>
/// Checks a live tree against the target that will render it, **before** it leaves the process.
///
/// This exists because every constraint on these surfaces fails *silently*. An oversized activity payload
/// is rejected by APNs with no error visible to the app. A <c>Button</c> in an Apple widget compiles,
/// ships, and does nothing. A bitmap with no accessibility label is invisible to VoiceOver. An oversized
/// notification is dropped by the system with a log line nobody reads. None of these surface as an
/// exception at the call site, and all of them are discovered on someone else's device.
///
/// So the validator is not a nicety — it is the substitute for the compiler errors these platforms
/// decline to give us. Drivers call <see cref="Assert"/> on every publish.
/// </summary>
public static class LiveValidator
{
    /// <summary>Validates and throws on the first <see cref="LiveSeverity.Error"/>. What the drivers call.</summary>
    public static void Assert(LivePayload payload, LiveTarget target)
    {
        var diagnostics = Validate(payload, target);
        foreach (var d in diagnostics)
        {
            if (d.Severity == LiveSeverity.Error)
                throw new InvalidOperationException(d.ToString());
        }
    }

    /// <summary>Validates and returns every finding, worst first.</summary>
    public static IReadOnlyList<LiveDiagnostic> Validate(LivePayload payload, LiveTarget target)
    {
        var found = new List<LiveDiagnostic>();

        CheckBudget(payload, target, found);
        Walk(payload.Root, target, 0, found);
        CheckFamily(payload.Root, target, found);
        CheckActionCount(payload, target, found);

        found.Sort((a, b) => b.Severity.CompareTo(a.Severity));
        return found;
    }

    static void CheckBudget(LivePayload payload, LiveTarget target, List<LiveDiagnostic> found)
    {
        var bytes = payload.Bytes;

        if (target.Surface == LiveSurface.Activity && target.Platform == LivePlatform.Apple)
        {
            if (bytes > LiveBudget.ActivityHardBytes)
                found.Add(new("SDNL001", LiveSeverity.Error,
                    $"Live Activity payload is {bytes} bytes; APNs caps it at {LiveBudget.ActivityHardBytes}. " +
                    "The update would be rejected with no error. Shorten the tree, or move detail behind a tap."));
            else if (bytes > LiveBudget.ActivityWarnBytes)
                found.Add(new("SDNL002", LiveSeverity.Warning,
                    $"Live Activity payload is {bytes} bytes, past the {LiveBudget.ActivityWarnBytes}-byte " +
                    $"guideline. The hard ceiling is {LiveBudget.ActivityHardBytes} including the ActivityKit envelope."));
        }
        else if (target.Surface == LiveSurface.Notification && bytes > LiveBudget.NotificationWarnBytes)
        {
            found.Add(new("SDNL002", LiveSeverity.Warning,
                $"Notification payload is {bytes} bytes. Notifications cross Binder against a ~1 MB " +
                "transaction budget and oversized ones are dropped by the system, not returned as an error."));
        }
        else if (target.Surface == LiveSurface.Widget && bytes > LiveBudget.WidgetWarnBytes)
        {
            found.Add(new("SDNL002", LiveSeverity.Warning,
                $"Widget payload is {bytes} bytes. There is no transport limit here, but this is written " +
                "to shared storage on every publish."));
        }
    }

    static void Walk(Node node, LiveTarget target, int depth, List<LiveDiagnostic> found)
    {
        if (depth == LiveBudget.MaxDepth && target.Platform == LivePlatform.Android)
            found.Add(new("SDNL008", LiveSeverity.Warning,
                $"Tree is nested more than {LiveBudget.MaxDepth} deep. SystemUI rebuilds a RemoteViews tree " +
                "by reflection; deep nesting parcels badly and inflates slowly.", node.Id));

        switch (node.Type)
        {
            case "LButton":
                // A plain AppIntent in a widget extension runs *in the extension*, where there is no .NET,
                // so the handler could never be reached. Live Activities escape this via LiveActivityIntent.
                if (target.Platform == LivePlatform.Apple && target.Surface == LiveSurface.Widget)
                    found.Add(new("SDNL003", LiveSeverity.Error,
                        "LiveButton is not reachable on an Apple widget: a widget's AppIntent runs inside the " +
                        "widget extension, which contains no .NET, so the handler could never run. Use LiveLink " +
                        "with a deep link instead. (Live Activities are fine — they use LiveActivityIntent, " +
                        "which runs in the app process.)", node.Id));
                break;

            case "LBitmap":
                CheckBitmap(node, found);
                break;

            case "LGauge":
                if (target.Platform == LivePlatform.Android)
                    found.Add(new("SDNL011", LiveSeverity.Info,
                        "Android has no gauge widget; LiveGauge renders as a LiveProgress bar.", node.Id));
                break;
        }

        foreach (var mod in node.Modifiers)
        {
            if (!mod.TryGetValue("type", out var raw) || raw is not string type) continue;

            if (type == "background" && mod.ContainsKey("gradient") && target.Platform == LivePlatform.Android)
                found.Add(new("SDNL010", LiveSeverity.Info,
                    "A RemoteViews tree cannot build a gradient drawable at runtime; the gradient renders as " +
                    "its first stop. Use LiveRenderMode.Bitmap if the gradient is load-bearing.", node.Id));

            if (type == "frame" && target.Platform == LivePlatform.Android && target.AndroidMinSdk < 31)
                found.Add(new("SDNL007", LiveSeverity.Warning,
                    $".Frame(…) needs setViewLayoutWidth/Height, which is API 31+. This app's minSdk is " +
                    $"{target.AndroidMinSdk}, so the modifier is a no-op below 31. Raise minSdk, or use " +
                    "LiveRenderMode.Bitmap.", node.Id));
        }

        foreach (var child in node.Children)
            Walk(child, target, depth + 1, found);
    }

    static void CheckBitmap(Node node, List<LiveDiagnostic> found)
    {
        var hasLabel = node.Modifiers.Any(m =>
            m.TryGetValue("type", out var t) && t is "a11yLabel");

        if (!hasLabel)
            found.Add(new("SDNL004", LiveSeverity.Error,
                "A LiveBitmap is one opaque image to VoiceOver and TalkBack, so .AccessibilityLabel(…) is " +
                "the only thing a screen-reader user gets. It is required.", node.Id));

        if (node.Props.TryGetValue("w", out var wv) && node.Props.TryGetValue("h", out var hv)
            && wv is double w && hv is double h && w * h > LiveBudget.BitmapMaxPixels)
        {
            found.Add(new("SDNL005", LiveSeverity.Error,
                $"Bitmap is {w:0}×{h:0} = {w * h:0} pixels, over the {LiveBudget.BitmapMaxPixels} ceiling. " +
                "It would inflate the payload past what the transport carries.", node.Id));
        }
    }

    static void CheckFamily(Node root, LiveTarget target, List<LiveDiagnostic> found)
    {
        if (target.Surface != LiveSurface.Widget || target.Family is not { } family) return;

        // The lock-screen accessories are not small widgets — they are a different medium. An inline
        // accessory is literally one line of text rendered beside the clock, and anything else is dropped.
        if (family == WidgetFamily.AccessoryInline)
        {
            var t = root.Type;
            if (t is not ("LText" or "LDate" or "LTimer" or "LHStack"))
                found.Add(new("SDNL006", LiveSeverity.Error,
                    "An AccessoryInline widget renders a single line of text beside the lock-screen clock. " +
                    $"'{LiveWire.ShortType(t)}' cannot appear there — use LiveText, LiveDate or LiveTimer.",
                    root.Id));
        }

        if (target.Platform == LivePlatform.Android && family
                is WidgetFamily.AccessoryCircular or WidgetFamily.AccessoryRectangular or WidgetFamily.AccessoryInline)
        {
            found.Add(new("SDNL012", LiveSeverity.Warning,
                $"{family} is an Apple lock-screen accessory; Android has no lock-screen widget host, so this " +
                "family is never requested there.", root.Id));
        }
    }

    static void CheckActionCount(LivePayload payload, LiveTarget target, List<LiveDiagnostic> found)
    {
        if (target.Surface == LiveSurface.Notification
            && payload.Actions.Count > LiveBudget.MaxNotificationActions)
        {
            found.Add(new("SDNL009", LiveSeverity.Warning,
                $"{payload.Actions.Count} actions declared; Android shows at most " +
                $"{LiveBudget.MaxNotificationActions} notification buttons. The rest are dropped by the system."));
        }
    }
}
