using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using Java.Lang;
using AColor = Android.Graphics.Color;
using AString = Java.Lang.String;
// Android.OS.Build collides with this class's own Build(Node); the platform type is always AndroidBuild here.
using AndroidBuild = Android.OS.Build;

namespace SwiftDotNet;

/// <summary>
/// Turns a live <see cref="Node"/> tree into a <see cref="RemoteViews"/> recipe.
///
/// <para><b>Why this is C# and not Kotlin.</b> A <c>RemoteViews</c> is not a view — it is a layout id plus
/// a queue of reflective setter calls, parcelled across Binder and replayed by SystemUI in *its* process.
/// Building one requires no toolkit runtime at all, and the code that builds it (an
/// <c>AppWidgetProvider</c>, or the notification post) runs in our own process. So unlike Compose, there
/// is nothing here that C# cannot express, and shipping a Kotlin interpreter would only move the
/// vocabulary out of the language the developer is writing in.</para>
///
/// <para><b>What SystemUI will actually accept</b> shapes almost every decision below. Only whitelisted
/// view classes can be inflated — which is why a divider and a spacer are empty <c>TextView</c>s rather
/// than a bare <c>View</c> or a <c>Space</c>, neither of which is inflatable. Only
/// <c>@RemotableViewMethod</c>-annotated setters can be called — which is why opacity is applied through
/// <c>setImageAlpha</c> on images and is a documented no-op elsewhere, rather than the <c>setAlpha</c>
/// that would throw an <c>ActionException</c> at inflate time. And runtime sizing did not exist before
/// API 31, which is the floor for <see cref="LiveRenderMode.Native"/>.</para>
/// </summary>
public sealed class RemoteViewsInterpreter
{
    readonly Context _context;
    readonly string _package;
    readonly DisplayMetrics? _metrics;
    readonly bool _dark;

    /// <summary>Actions collected while walking, so the caller can attach the right <see cref="PendingIntent"/>s.</summary>
    readonly List<string> _actionNodeIds = new();

    public RemoteViewsInterpreter(Context context)
    {
        _context = context;
        _package = context.PackageName ?? "";
        _metrics = context.Resources?.DisplayMetrics;
        _dark = (context.Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
    }

    /// <summary>
    /// The surface id this tree belongs to. Used to key the <see cref="PendingIntent"/>s so a tap can be
    /// routed back to the right <see cref="LiveActionRouter"/> entry.
    /// </summary>
    public string Kind { get; set; } = "";

    /// <summary>Node ids that got a click handler, in walk order.</summary>
    public IReadOnlyList<string> ActionNodeIds => _actionNodeIds;

    /// <summary>
    /// Supplies the <see cref="PendingIntent"/> for a tappable node, or null to leave it inert.
    ///
    /// The click has to be attached *while* the child recipe is being built, not afterwards, and the
    /// reason is the shared <c>sdn_root</c> id: every primitive layout names its root the same thing, so
    /// a setter always addresses the recipe it is called on. Applied to the child before
    /// <c>addView</c>, it hits that child; applied to the parent afterwards, it would hit the parent's
    /// own root instead. That single fact is what lets a whole tree be composed from one shared id.
    /// </summary>
    public Func<string, PendingIntent?>? ClickIntentFor { get; set; }

    /// <summary>Builds the recipe for a whole tree.</summary>
    public RemoteViews Build(Node root)
    {
        _actionNodeIds.Clear();
        return Visit(root);
    }

    RemoteViews Visit(Node node)
    {
        var rv = Create(node);
        ApplyProps(node, rv);
        ApplyModifiers(node, rv);

        if (node.Children.Count > 0 && IsContainer(node.Type))
        {
            var spacingPx = node.Props.TryGetValue("spacing", out var sp) && sp is double s
                ? LiveTokens.Px(s, _metrics) : 0;

            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = Visit(node.Children[i]);

                // LinearLayout has no runtime "spacing"; the nearest honest equivalent is a margin on
                // every child but the last, and setViewLayoutMargin is API 31+. Below that, spacing is a
                // documented no-op rather than a fake gap built out of padding (which would also inset
                // the child's own background).
                if (spacingPx > 0 && i < node.Children.Count - 1
                    && AndroidBuild.VERSION.SdkInt >= BuildVersionCodes.S)
                {
                    var edge = node.Type == "LHStack" ? EndMargin : BottomMargin;
                    child.SetViewLayoutMargin(Resource.Id.sdn_root, edge, spacingPx, (int)ComplexUnitType.Px);
                }

                rv.AddView(Resource.Id.sdn_root, child);
            }
        }
        else if (node.Type == "LLink" && node.Children.Count > 0)
        {
            // A link is a pass-through wrapper: its child is the content, and the click lands on the
            // whole thing. Modelling it as a container would add a redundant FrameLayout to every parcel.
            rv.AddView(Resource.Id.sdn_root, Visit(node.Children[0]));
        }

        return rv;
    }

    RemoteViews Create(Node node) => new(_package, LayoutFor(node));

    int LayoutFor(Node node) => node.Type switch
    {
        "LVStack" => Resource.Layout.sdn_vstack,
        "LHStack" => Resource.Layout.sdn_hstack,
        "LZStack" or "LLink" => Resource.Layout.sdn_zstack,
        "LText" or "LDate" => Resource.Layout.sdn_text,
        "LTimer" => Resource.Layout.sdn_timer,
        "LImage" => Resource.Layout.sdn_image,
        "LBitmap" => Resource.Layout.sdn_bitmap,
        "LButton" => Resource.Layout.sdn_button,
        "LDivider" => Resource.Layout.sdn_divider,
        "LSpacer" => Resource.Layout.sdn_spacer,
        // A gauge has no Android analog at all, so it degrades to the progress bar it most resembles.
        // Declared behaviour, reported by LiveValidator as SDNL011 rather than discovered on device.
        "LProgress" or "LGauge" => node.Props.ContainsKey("indeterminate")
            ? Resource.Layout.sdn_spinner
            : Resource.Layout.sdn_progress,
        "LShape" => Resource.Layout.sdn_text,   // an empty TextView carrying a background drawable
        _ => Resource.Layout.sdn_text,
    };

    static bool IsContainer(string type) => type is "LVStack" or "LHStack" or "LZStack";

    void ApplyProps(Node node, RemoteViews rv)
    {
        var Id = Resource.Id.sdn_root;

        switch (node.Type)
        {
            case "LText":
                rv.SetTextViewText(Id, new AString(Str(node, "text")));
                break;

            case "LDate":
                // Formatted here rather than by the host: RemoteViews has no date-formatting setter, and
                // a Chronometer only counts. The freshness argument for LiveDate is therefore Apple-only,
                // and the Android side re-formats on every publish.
                rv.SetTextViewText(Id, new AString(FormatDate(node)));
                break;

            case "LTimer":
                ApplyTimer(node, rv);
                break;

            case "LImage":
                var resId = _context.Resources?.GetIdentifier(Str(node, "name"), "drawable", _package) ?? 0;
                if (resId != 0) rv.SetImageViewResource(Id, resId);
                else rv.SetViewVisibility(Id, ViewStates.Gone);   // a missing drawable must not leave a gap
                break;

            case "LBitmap":
                ApplyBitmap(node, rv);
                break;

            case "LButton":
                rv.SetTextViewText(Id, new AString(Str(node, "title")));
                _actionNodeIds.Add(node.Id);
                if (ClickIntentFor?.Invoke(node.Id) is { } buttonIntent)
                    rv.SetOnClickPendingIntent(Id, buttonIntent);
                break;

            case "LLink":
                if (ClickIntentFor?.Invoke(node.Id) is { } linkIntent)
                    rv.SetOnClickPendingIntent(Id, linkIntent);
                break;

            case "LProgress":
                rv.SetProgressBar(Id, 100,
                    node.Props.TryGetValue("value", out var v) && v is double d ? (int)(d * 100) : 0,
                    node.Props.ContainsKey("indeterminate"));
                break;

            case "LGauge":
                var min = Num(node, "min", 0);
                var max = Num(node, "max", 1);
                var val = Num(node, "value", 0);
                var fraction = max > min ? (val - min) / (max - min) : 0;
                rv.SetProgressBar(Id, 100, (int)(System.Math.Clamp(fraction, 0, 1) * 100), false);
                break;

            case "LShape":
                rv.SetTextViewText(Id, new AString(""));
                break;
        }

        // Container gravity. LinearLayout.setGravity and FrameLayout.setForegroundGravity are both
        // @RemotableViewMethod, so this is one of the few layout properties settable below API 31.
        if (IsContainer(node.Type) && node.Props.TryGetValue("alignment", out var a) && a is string alignment)
            rv.SetInt(Id, "setGravity", (int)GravityFor(alignment, node.Type));
    }

    void ApplyTimer(Node node, RemoteViews rv)
    {
        var Id = Resource.Id.sdn_root;

        // Chronometer counts against SystemClock.elapsedRealtime, not wall time, so the target has to be
        // rebased. This is the whole reason LiveTimer exists: once set, it ticks by itself and a running
        // countdown costs zero notify() calls and zero payload.
        var targetMs = (long)(Num(node, "target", 0) * 1000);
        var deltaMs = targetMs - Java.Lang.JavaSystem.CurrentTimeMillis();
        rv.SetChronometer(Id, SystemClock.ElapsedRealtime() + deltaMs, null, started: true);

        var countsDown = node.Props.TryGetValue("countsDown", out var cd) && cd is bool b && b;
        if (AndroidBuild.VERSION.SdkInt >= BuildVersionCodes.N)
            rv.SetChronometerCountDown(Id, countsDown);
    }

    void ApplyBitmap(Node node, RemoteViews rv)
    {
        var b64 = Str(node, "png");
        if (b64.Length == 0) return;

        var bytes = System.Convert.FromBase64String(b64);
        var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
        if (bitmap is not null) rv.SetImageViewBitmap(Resource.Id.sdn_root, bitmap);
    }

    void ApplyModifiers(Node node, RemoteViews rv)
    {
        var Id = Resource.Id.sdn_root;
        var isText = node.Type is "LText" or "LDate" or "LTimer" or "LButton" or "LShape";

        foreach (var mod in node.Modifiers)
        {
            if (!mod.TryGetValue("type", out var raw) || raw is not string type) continue;

            switch (type)
            {
                case "font" when isText:
                    var token = ModStr(mod, "value");
                    rv.SetTextViewTextSize(Id, (int)ComplexUnitType.Sp, LiveTokens.FontSizeSp(token));
                    if (LiveTokens.IsBold(token)) SetBold(rv, Id);
                    break;

                case "bold" when isText:
                    if (mod.TryGetValue("value", out var bv) && bv is bool bold && bold) SetBold(rv, Id);
                    break;

                case "foregroundColor" when isText:
                    rv.SetTextColor(Id, LiveTokens.Resolve(ModStr(mod, "value"), _dark));
                    break;

                case "background":
                    // A RemoteViews tree cannot build a GradientDrawable at runtime, so a gradient
                    // collapses to its first stop. Reported as SDNL010; documented, never silent.
                    var color = mod.TryGetValue("gradient", out var g) && g is string grad
                        ? FirstStopOf(grad)
                        : ModStr(mod, "value");
                    rv.SetInt(Id, "setBackgroundColor", LiveTokens.Resolve(color, _dark).ToArgb());
                    break;

                case "tint":
                    if (AndroidBuild.VERSION.SdkInt >= BuildVersionCodes.S)
                    {
                        var tint = ColorStateList.ValueOf(LiveTokens.Resolve(ModStr(mod, "value"), _dark));
                        // setProgressTintList / setImageTintList are both @RemotableViewMethod from 31.
                        rv.SetColorStateList(Id, node.Type is "LProgress" or "LGauge"
                            ? "setProgressTintList" : "setImageTintList", tint);
                    }
                    break;

                case "padding":
                    rv.SetViewPadding(Id,
                        LiveTokens.Px(ModNum(mod, "leading"), _metrics),
                        LiveTokens.Px(ModNum(mod, "top"), _metrics),
                        LiveTokens.Px(ModNum(mod, "trailing"), _metrics),
                        LiveTokens.Px(ModNum(mod, "bottom"), _metrics));
                    break;

                case "frame" when AndroidBuild.VERSION.SdkInt >= BuildVersionCodes.S:
                    if (mod.TryGetValue("width", out var w) && w is double dw)
                        rv.SetViewLayoutWidth(Id, (float)dw, (int)ComplexUnitType.Dip);
                    if (mod.TryGetValue("height", out var h) && h is double dh)
                        rv.SetViewLayoutHeight(Id, (float)dh, (int)ComplexUnitType.Dip);
                    break;

                case "lineLimit" when isText:
                    rv.SetInt(Id, "setMaxLines", (int)ModNum(mod, "value"));
                    break;

                case "opacity":
                    // View.setAlpha is NOT @RemotableViewMethod — calling it would throw ActionException
                    // when SystemUI replays the recipe. ImageView.setImageAlpha is, so opacity works on
                    // images and is a no-op elsewhere.
                    if (node.Type is "LImage" or "LBitmap")
                        rv.SetInt(Id, "setImageAlpha", (int)(ModNum(mod, "value") * 255));
                    break;

                case "a11yLabel":
                    rv.SetContentDescription(Id, new AString(ModStr(mod, "value")));
                    break;

                case "tapUrl":
                    if (ClickIntentFor?.Invoke(node.Id) is { } tapIntent)
                        rv.SetOnClickPendingIntent(Id, tapIntent);
                    break;

                // cornerRadius is absent on purpose: rounding needs a drawable, and a RemoteViews recipe
                // cannot construct one remotely. LiveRenderMode.Bitmap is the answer when it matters.
            }
        }
    }

    static void SetBold(RemoteViews rv, int id)
    {
        // There is no remotable setTypeface(style); the annotated overload takes a family name, and
        // "sans-serif-medium" is the closest weight bump reachable from a RemoteViews recipe.
        rv.SetString(id, "setFontFamily", "sans-serif-medium");
    }

    static GravityFlags GravityFor(string alignment, string type) => alignment switch
    {
        "leading" => type == "LVStack" ? GravityFlags.Start : GravityFlags.CenterVertical | GravityFlags.Start,
        "trailing" => type == "LVStack" ? GravityFlags.End : GravityFlags.CenterVertical | GravityFlags.End,
        "top" => GravityFlags.Top,
        "bottom" => GravityFlags.Bottom,
        _ => GravityFlags.Center,
    };

    /// <summary>Pulls the first color out of a <see cref="Brush"/> wire string — the Android fallback for a gradient.</summary>
    static string FirstStopOf(string brushWire)
    {
        // linear:<angle>:<color>@<loc>;…   |   radial:<color>@<loc>;…
        var parts = brushWire.Split(':');
        var stops = parts.Length > 0 ? parts[^1] : "";
        var first = stops.Split(';')[0];
        var at = first.IndexOf('@');
        return at > 0 ? first.Substring(0, at) : first;
    }

    string FormatDate(Node node)
    {
        var seconds = Num(node, "date", 0);
        var when = DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).ToLocalTime();

        return Str(node, "style") switch
        {
            "date" => when.ToString("d"),
            "relative" or "offset" => Relative(when),
            _ => when.ToString("t"),
        };
    }

    static string Relative(DateTimeOffset when)
    {
        var delta = when - DateTimeOffset.Now;
        var abs = delta.Duration();
        var unit = abs.TotalDays >= 1 ? $"{(int)abs.TotalDays}d"
            : abs.TotalHours >= 1 ? $"{(int)abs.TotalHours}h"
            : $"{System.Math.Max(1, (int)abs.TotalMinutes)}m";
        return delta < TimeSpan.Zero ? unit + " ago" : "in " + unit;
    }

    static string Str(Node node, string key) =>
        node.Props.TryGetValue(key, out var v) && v is string s ? s : "";

    static double Num(Node node, string key, double fallback) =>
        node.Props.TryGetValue(key, out var v) && v is double d ? d : fallback;

    static string ModStr(Dictionary<string, object> mod, string key) =>
        mod.TryGetValue(key, out var v) && v is string s ? s : "";

    static double ModNum(Dictionary<string, object> mod, string key) =>
        mod.TryGetValue(key, out var v) && v is double d ? d : 0;

    // RemoteViews.setViewLayoutMargin edge constants (MARGIN_BOTTOM / MARGIN_END). Hard-coded because the
    // binding exposes them as a plain int parameter rather than an enum.
    const int BottomMargin = 3;
    const int EndMargin = 1;
}
