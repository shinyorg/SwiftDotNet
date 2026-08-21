using System.Globalization;

namespace SwiftDotNet;

/// <summary>A run of text. Maps to SwiftUI <c>Text</c> and to a <c>TextView</c> in a <c>RemoteViews</c> tree.</summary>
public sealed class LiveText : LiveView
{
    readonly string _text;

    public LiveText(string text) => _text = text;

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LText", path);
        n.Props["text"] = _text;
        return n;
    }
}

/// <summary>
/// A **self-ticking** clock. This is the single most valuable node on these surfaces and has no analog
/// in the main DSL: it counts up or down *without any update from us*, so a running timer costs zero
/// against the iOS activity-update budget and zero <c>notify()</c> calls on Android.
///
/// SwiftUI renders it as <c>Text(timerInterval:countsDown:)</c>; Android as a <c>Chronometer</c> with
/// <c>setChronometerCountDown</c> (API 24+). Anything that shows a countdown — a delivery ETA, a rest
/// timer, a ride arrival — should use this rather than publishing a new tree every second.
/// </summary>
public sealed class LiveTimer : LiveView
{
    readonly DateTimeOffset _target;
    readonly bool _countsDown;

    /// <param name="target">The instant counted to (when counting down) or from (when counting up).</param>
    /// <param name="countsDown">True for a countdown to <paramref name="target"/>; false to count elapsed time since it.</param>
    public LiveTimer(DateTimeOffset target, bool countsDown = true)
    {
        _target = target;
        _countsDown = countsDown;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LTimer", path);
        // Seconds since the epoch as a double — the one numeric encoding every interpreter already parses.
        n.Props["target"] = _target.ToUnixTimeMilliseconds() / 1000.0;
        n.Props["countsDown"] = _countsDown;
        return n;
    }
}

/// <summary>
/// A date rendered by the *host*, in the host's locale and time zone, rather than pre-formatted by us.
/// Worth preferring over <c>LiveText(date.ToString())</c> because a surface can outlive the process that
/// published it — a widget published at 09:00 is still on screen at 17:00, and a baked string goes stale.
/// </summary>
public sealed class LiveDate : LiveView
{
    readonly DateTimeOffset _date;
    readonly LiveDateStyle _style;

    public LiveDate(DateTimeOffset date, LiveDateStyle style = LiveDateStyle.Time)
    {
        _date = date;
        _style = style;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LDate", path);
        n.Props["date"] = _date.ToUnixTimeMilliseconds() / 1000.0;
        n.Props["style"] = _style switch
        {
            LiveDateStyle.Date => "date",
            LiveDateStyle.Relative => "relative",
            LiveDateStyle.Offset => "offset",
            _ => "time",
        };
        return n;
    }
}

/// <summary>How a <see cref="LiveDate"/> is rendered by the host.</summary>
public enum LiveDateStyle
{
    /// <summary>Clock time, e.g. "4:30 PM".</summary>
    Time,
    /// <summary>Calendar date, e.g. "June 3, 2026".</summary>
    Date,
    /// <summary>Relative, e.g. "2 hours ago". Android approximates with <c>DateUtils</c>.</summary>
    Relative,
    /// <summary>Signed offset from now, e.g. "+2 hours". Apple only; Android falls back to relative.</summary>
    Offset,
}

/// <summary>
/// A named platform symbol — an SF Symbol on Apple, a drawable name on Android. Deliberately *not* an
/// arbitrary image path: a widget extension has its own bundle and cannot see the app's asset catalogue,
/// so a file path that works in the app renders as nothing on the lock screen. For arbitrary pixels use
/// <see cref="LiveBitmap"/>, which carries the data with the tree.
/// </summary>
public sealed class LiveImage : LiveView
{
    readonly string _name;

    public LiveImage(string name) => _name = name;

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LImage", path);
        n.Props["name"] = _name;
        return n;
    }
}

/// <summary>
/// Raw pixels carried inline with the tree, as base64 PNG. The escape hatch for anything the vocabulary
/// cannot express — a chart, a route line, a branded badge — and the way
/// <see cref="LiveRenderMode.Bitmap"/> ships a whole tree rendered by the Skia engine.
///
/// Costly: it inflates the payload, and on iOS an activity payload is capped at
/// <see cref="LiveBudget.ActivityHardBytes"/>. <see cref="LiveValidator"/> enforces that.
/// </summary>
public sealed class LiveBitmap : LiveView
{
    readonly string _base64Png;
    readonly int _width;
    readonly int _height;

    public LiveBitmap(byte[] png, int width, int height)
    {
        _base64Png = Convert.ToBase64String(png);
        _width = width;
        _height = height;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LBitmap", path);
        n.Props["png"] = _base64Png;
        n.Props["w"] = (double)_width;
        n.Props["h"] = (double)_height;
        return n;
    }
}

/// <summary>Shared base for the three stacks.</summary>
public abstract class LiveStack : LiveView
{
    readonly string _type;
    readonly LiveView[] _children;
    double? _spacing;
    LiveAlignment _alignment = LiveAlignment.Center;

    private protected LiveStack(string type, LiveView[] children)
    {
        _type = type;
        _children = children;
    }

    /// <summary>Points between children.</summary>
    public LiveStack Spacing(double points)
    {
        _spacing = points;
        return this;
    }

    /// <summary>Cross-axis alignment (both axes for a <see cref="LiveZStack"/>).</summary>
    public LiveStack Alignment(LiveAlignment alignment)
    {
        _alignment = alignment;
        return this;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode(_type, path);
        if (_spacing is { } s) n.Props["spacing"] = s;
        n.Props["alignment"] = _alignment.ToString().ToLowerInvariant();
        for (var i = 0; i < _children.Length; i++)
            n.Children.Add(_children[i].ToNode(ctx, path + "." + i.ToString(CultureInfo.InvariantCulture)));
        return n;
    }
}

/// <summary>Vertical stack. SwiftUI <c>VStack</c> / a vertical <c>LinearLayout</c>.</summary>
public sealed class LiveVStack : LiveStack
{
    public LiveVStack(params LiveView[] children) : base("LVStack", children) { }
}

/// <summary>Horizontal stack. SwiftUI <c>HStack</c> / a horizontal <c>LinearLayout</c>.</summary>
public sealed class LiveHStack : LiveStack
{
    public LiveHStack(params LiveView[] children) : base("LHStack", children) { }
}

/// <summary>Overlay stack. SwiftUI <c>ZStack</c> / a <c>FrameLayout</c>.</summary>
public sealed class LiveZStack : LiveStack
{
    public LiveZStack(params LiveView[] children) : base("LZStack", children) { }
}

/// <summary>Alignment tokens shared by the stacks. A subset of the main DSL's, matching what both hosts honour.</summary>
public enum LiveAlignment { Leading, Center, Trailing, Top, Bottom }

/// <summary>Flexible space. SwiftUI <c>Spacer</c> / a zero-size weighted <c>View</c>.</summary>
public sealed class LiveSpacer : LiveView
{
    internal override Node Build(LiveContext ctx, string path) => ctx.NewNode("LSpacer", path);
}

/// <summary>A hairline rule. SwiftUI <c>Divider</c> / a 1px <c>View</c>.</summary>
public sealed class LiveDivider : LiveView
{
    internal override Node Build(LiveContext ctx, string path) => ctx.NewNode("LDivider", path);
}

/// <summary>
/// A determinate or indeterminate progress bar. SwiftUI <c>ProgressView</c> / a <c>ProgressBar</c>.
/// On Android 16 this is also what a <see cref="LiveUpdate"/> promotes into the status-bar chip.
/// </summary>
public sealed class LiveProgress : LiveView
{
    readonly double? _fraction;

    /// <param name="fraction">0–1, or null for an indeterminate spinner.</param>
    public LiveProgress(double? fraction = null) => _fraction = fraction;

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LProgress", path);
        if (_fraction is { } f) n.Props["value"] = Math.Clamp(f, 0, 1);
        else n.Props["indeterminate"] = true;
        return n;
    }
}

/// <summary>
/// A circular/linear gauge. SwiftUI <c>Gauge</c>; Android has no gauge widget, so it degrades to a
/// labelled <see cref="LiveProgress"/> — an explicit, documented fallback rather than a silent drop.
/// </summary>
public sealed class LiveGauge : LiveView
{
    readonly double _value;
    readonly double _min;
    readonly double _max;

    public LiveGauge(double value, double min = 0, double max = 1)
    {
        _value = value;
        _min = min;
        _max = max;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LGauge", path);
        n.Props["value"] = _value;
        n.Props["min"] = _min;
        n.Props["max"] = _max;
        return n;
    }
}

/// <summary>A filled shape — the vocabulary's only drawing primitive. Use <c>.Background</c> for the fill.</summary>
public sealed class LiveShape : LiveView
{
    readonly string _shape;
    readonly double _radius;

    LiveShape(string shape, double radius = 0)
    {
        _shape = shape;
        _radius = radius;
    }

    public static LiveShape Rectangle() => new("rect");
    public static LiveShape RoundedRectangle(double cornerRadius) => new("rounded", cornerRadius);
    public static LiveShape Capsule() => new("capsule");
    public static LiveShape Circle() => new("circle");

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LShape", path);
        n.Props["shape"] = _shape;
        if (_radius > 0) n.Props["radius"] = _radius;
        return n;
    }
}

/// <summary>
/// A tappable button. The handler runs **in the app's process** on both platforms, but by very different
/// routes: on Apple through a <c>LiveActivityIntent</c> (a plain <c>AppIntent</c> would run in the widget
/// extension, where there is no .NET); on Android through a <c>PendingIntent</c> to our own receiver,
/// which is our process to begin with.
///
/// Buttons are not available on an Apple *widget* — there they must be a <see cref="LiveLink"/>.
/// <see cref="LiveValidator"/> enforces the difference rather than letting it fail silently on device.
/// </summary>
public sealed class LiveButton : LiveView
{
    readonly string _title;
    readonly Action<string?> _action;

    public LiveButton(string title, Action action)
        : this(title, _ => action()) { }

    public LiveButton(string title, Action<string?> action)
    {
        _title = title;
        _action = action;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LButton", path);
        n.Props["title"] = _title;
        ctx.Register(path, _action);
        return n;
    }
}

/// <summary>
/// A deep link into the app. The only interaction a *widget* gets on Apple, and the cheapest one
/// everywhere: no intent, no receiver, just a URL the host opens.
/// </summary>
public sealed class LiveLink : LiveView
{
    readonly string _url;
    readonly LiveView _content;

    public LiveLink(string url, LiveView content)
    {
        _url = url;
        _content = content;
    }

    internal override Node Build(LiveContext ctx, string path)
    {
        var n = ctx.NewNode("LLink", path);
        n.Props["url"] = _url;
        n.Children.Add(_content.ToNode(ctx, path + ".0"));
        return n;
    }
}
