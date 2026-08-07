using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// A property a <see cref="KeyframeTimeline"/> can drive. Every backend animates these natively (or
/// documents the gap) — deliberately a small, numeric set so one wire encoding covers all of them.
/// </summary>
public enum Prop
{
    /// <summary>Absolute opacity, 0–1.</summary>
    Opacity,
    /// <summary>Uniform scale multiplier (drives both axes); 1 = unscaled.</summary>
    Scale,
    /// <summary>Horizontal scale multiplier; 1 = unscaled.</summary>
    ScaleX,
    /// <summary>Vertical scale multiplier; 1 = unscaled.</summary>
    ScaleY,
    /// <summary>Rotation in degrees, clockwise.</summary>
    Rotation,
    /// <summary>Horizontal translation in points; does not affect layout.</summary>
    OffsetX,
    /// <summary>Vertical translation in points; does not affect layout.</summary>
    OffsetY,
    /// <summary>Absolute width in points.</summary>
    Width,
    /// <summary>Absolute height in points.</summary>
    Height,
}

/// <summary>
/// One stop on a <see cref="Prop"/>'s track. <paramref name="Time"/> is a fraction of the timeline's
/// duration (0–1), <paramref name="Value"/> is the property's absolute value there, and
/// <paramref name="Curve"/> is how the value <em>arrives</em> at this stop from the previous one —
/// matching SwiftUI's <c>KeyframeTrack</c> and CSS's per-stop <c>animation-timing-function</c>.
/// </summary>
public readonly record struct Keyframe(double Time, double Value, AnimationCurve? Curve = null);

/// <summary>Builds one property's track — a sorted list of <see cref="Keyframe"/> stops.</summary>
public sealed class KeyframeTrackBuilder
{
    internal readonly List<Keyframe> Stops = new();

    /// <summary>
    /// Adds a stop at <paramref name="time"/> (0–1, a fraction of the timeline duration) holding
    /// <paramref name="value"/>. <paramref name="curve"/> overrides the timeline's default curve for the
    /// segment leading <em>into</em> this stop; the first stop's curve is unused.
    /// </summary>
    public KeyframeTrackBuilder At(double time, double value, AnimationSpec? curve = null)
    {
        Stops.Add(new Keyframe(Math.Clamp(time, 0, 1), value, curve?.Curve));
        return this;
    }
}

/// <summary>
/// A multi-track keyframe animation: independent per-property timelines that share one duration and
/// clock. Mirrors SwiftUI's <c>KeyframeAnimator</c> / Compose's <c>keyframes</c> spec / CSS
/// <c>@keyframes</c>. Build one through <see cref="ViewModifiers.Keyframes{T}"/>.
/// </summary>
public sealed class KeyframeTimeline
{
    readonly List<(Prop Property, List<Keyframe> Stops)> _tracks = new();
    internal double TotalDuration = 1;
    internal double StartDelay;
    internal AnimationCurve DefaultCurve = AnimationCurve.EaseInOut;
    internal int? Repeat;
    internal bool AutoReverse;
    internal string Trigger = "";

    /// <summary>
    /// Adds (or extends) the track for <paramref name="property"/>. Tracks are independent: each has its
    /// own stops and its own per-segment curves, so opacity can ease while scale springs.
    /// </summary>
    public KeyframeTimeline Track(Prop property, Action<KeyframeTrackBuilder> build)
    {
        var b = new KeyframeTrackBuilder();
        build(b);
        if (b.Stops.Count == 0) return this;
        // Stops are sorted (not required to be declared in order) and a duplicate time keeps the later
        // declaration — a discontinuous jump, which is a legitimate keyframe effect.
        var stops = b.Stops.OrderBy(s => s.Time).ToList();
        var existing = _tracks.FindIndex(t => t.Property == property);
        if (existing >= 0) _tracks[existing] = (property, stops);
        else _tracks.Add((property, stops));
        return this;
    }

    /// <summary>Total length of one cycle, in seconds (default 1). Stop times are fractions of this.</summary>
    public KeyframeTimeline Duration(double seconds)
    {
        TotalDuration = Math.Max(0.001, seconds);
        return this;
    }

    /// <summary>Waits <paramref name="seconds"/> before the first cycle starts.</summary>
    public KeyframeTimeline Delay(double seconds)
    {
        StartDelay = Math.Max(0, seconds);
        return this;
    }

    /// <summary>The interpolation curve for segments whose stop didn't specify one (default ease-in-out).</summary>
    public KeyframeTimeline Curve(AnimationSpec spec)
    {
        DefaultCurve = spec.Curve;
        return this;
    }

    /// <summary>
    /// Loops the timeline: <paramref name="count"/> defaults to <c>-1</c> (forever);
    /// <paramref name="autoreverse"/> plays each alternate cycle backwards. Without this the timeline
    /// plays once — on appear, or each time the <c>on:</c> trigger changes.
    /// </summary>
    public KeyframeTimeline Repeating(int count = -1, bool autoreverse = false)
    {
        Repeat = count;
        AutoReverse = autoreverse;
        return this;
    }

    /// <summary>
    /// Replays the (non-repeating) timeline whenever <paramref name="value"/> changes, mirroring the
    /// <c>on:</c> argument of <see cref="ViewModifiers.Animation{T}"/>. Ignored for a repeating timeline,
    /// which runs on its own clock.
    /// </summary>
    public KeyframeTimeline On(object? value)
    {
        Trigger = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return this;
    }

    internal string EncodeTracks() => KeyframeWire.Encode(_tracks);

    internal bool IsEmpty => _tracks.Count == 0;
}

/// <summary>
/// The <c>keyframes</c> wire encoding, shared by every C# backend (and mirrored by the Swift and Kotlin
/// bridges). The patch protocol carries only flat scalars — see
/// <see cref="NodeJson"/> — so a whole multi-track timeline is packed into one string, the same way a
/// <see cref="Brush"/> is:
/// <code>
/// opacity:0,1;0.5,0.3,easeOut;1,1|scale:0,1;0.6,1.2,spring;1,1
/// </code>
/// tracks separated by <c>|</c>, <c>property:stop;stop;…</c>, each stop <c>time,value[,curve]</c>.
/// </summary>
public static class KeyframeWire
{
    /// <summary>The wire token for a <see cref="Prop"/>.</summary>
    public static string Token(this Prop p) => p switch
    {
        Prop.Opacity => "opacity",
        Prop.Scale => "scale",
        Prop.ScaleX => "scaleX",
        Prop.ScaleY => "scaleY",
        Prop.Rotation => "rotation",
        Prop.OffsetX => "offsetX",
        Prop.OffsetY => "offsetY",
        Prop.Width => "width",
        _ => "height",
    };

    internal static string Encode(List<(Prop Property, List<Keyframe> Stops)> tracks)
    {
        var sb = new StringBuilder();
        foreach (var (property, stops) in tracks)
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(property.Token()).Append(':');
            for (var i = 0; i < stops.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(N(stops[i].Time)).Append(',').Append(N(stops[i].Value));
                if (stops[i].Curve is { } c) sb.Append(',').Append(c.Token());
            }
        }
        return sb.ToString();
    }

    static string N(double d) => d.ToString("0.#####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a wire string back into tracks. Malformed segments are skipped rather than thrown on — a
    /// bad stop must not take down a render.
    /// </summary>
    public static List<(string Property, List<Keyframe> Stops)> Parse(string? tracks)
    {
        var result = new List<(string, List<Keyframe>)>();
        if (string.IsNullOrEmpty(tracks)) return result;

        foreach (var trackSpec in tracks.Split('|'))
        {
            var colon = trackSpec.IndexOf(':');
            if (colon <= 0) continue;
            var property = trackSpec[..colon];
            var stops = new List<Keyframe>();
            foreach (var stopSpec in trackSpec[(colon + 1)..].Split(';'))
            {
                var parts = stopSpec.Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var t)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                stops.Add(new Keyframe(t, v, parts.Length > 2 ? CurveFor(parts[2]) : null));
            }
            if (stops.Count > 0) result.Add((property, stops));
        }
        return result;
    }

    static AnimationCurve? CurveFor(string token) => token switch
    {
        "linear" => AnimationCurve.Linear,
        "easeIn" => AnimationCurve.EaseIn,
        "easeOut" => AnimationCurve.EaseOut,
        "easeInOut" => AnimationCurve.EaseInOut,
        "spring" => AnimationCurve.Spring,
        _ => null,
    };

    /// <summary>
    /// The value of <paramref name="stops"/> at normalized <paramref name="phase"/> (0–1). Before the
    /// first stop the track holds its first value; after the last it holds its last — so a track that
    /// doesn't span the whole timeline simply clamps rather than snapping to some implied base.
    /// </summary>
    public static double Sample(List<Keyframe> stops, double phase, AnimationCurve fallbackCurve = AnimationCurve.EaseInOut)
    {
        if (stops.Count == 0) return 0;
        if (stops.Count == 1 || phase <= stops[0].Time) return stops[0].Value;
        if (phase >= stops[^1].Time) return stops[^1].Value;

        for (var i = 1; i < stops.Count; i++)
        {
            if (phase > stops[i].Time) continue;
            var a = stops[i - 1];
            var b = stops[i];
            var span = b.Time - a.Time;
            // Coincident stops are a deliberate hard cut: jump straight to the later value.
            var local = span <= 0 ? 1 : (phase - a.Time) / span;
            return a.Value + ((b.Value - a.Value) * Ease(b.Curve ?? fallbackCurve, local));
        }
        return stops[^1].Value;
    }

    /// <summary>
    /// The eased progress of <paramref name="t"/> (0–1) under <paramref name="curve"/>. This is the one
    /// definition of the curves for every backend that interpolates in C# (the Graphics engine, and the
    /// CSS backends when they need to bake a spring into discrete steps), so a keyframe animation reads
    /// identically on all of them.
    /// </summary>
    public static double Ease(AnimationCurve curve, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return curve switch
        {
            AnimationCurve.Linear => t,
            AnimationCurve.EaseIn => t * t,
            AnimationCurve.EaseOut => t * (2 - t),
            // A decaying settle — the same approximation the Graphics engine has always used for spring.
            AnimationCurve.Spring => 1 - (Math.Exp(-6 * t) * Math.Cos(t * Math.PI * 1.5)),
            _ => t < 0.5 ? 2 * t * t : 1 - (Math.Pow((-2 * t) + 2, 2) / 2),
        };
    }

    /// <summary>
    /// Flattens independent per-property tracks into a single list of stops where <em>every</em> property
    /// has a value — the shape a CSS <c>@keyframes</c> rule needs, since one rule can't give two properties
    /// different timing functions.
    /// <para>
    /// Stops land on the union of all tracks' declared times. Any interval a non-linear curve arrives over
    /// is subdivided into <paramref name="subdivisions"/> linear steps and sampled with
    /// <see cref="Sample"/> — so the browser interpolates linearly between points that already carry the
    /// eased shape, and the result matches what the Graphics engine draws for the same timeline.
    /// </para>
    /// </summary>
    public static List<(double Time, List<(string Property, double Value)> Values)> Bake(
        List<(string Property, List<Keyframe> Stops)> tracks,
        AnimationCurve fallback = AnimationCurve.EaseInOut,
        int subdivisions = 8)
    {
        var result = new List<(double, List<(string, double)>)>();
        if (tracks.Count == 0) return result;

        var times = new SortedSet<double> { 0, 1 };
        foreach (var (_, stops) in tracks)
            foreach (var s in stops)
                times.Add(s.Time);

        var anchors = times.ToList();
        var sampleAt = new List<double>();
        for (var i = 0; i < anchors.Count; i++)
        {
            sampleAt.Add(anchors[i]);
            if (i + 1 >= anchors.Count) break;
            if (!NeedsSubdivision(tracks, anchors[i], anchors[i + 1], fallback)) continue;
            var span = anchors[i + 1] - anchors[i];
            for (var k = 1; k < subdivisions; k++) sampleAt.Add(anchors[i] + (span * k / subdivisions));
        }

        foreach (var t in sampleAt)
        {
            var values = new List<(string, double)>(tracks.Count);
            foreach (var (property, stops) in tracks) values.Add((property, Sample(stops, t, fallback)));
            result.Add((t, values));
        }
        return result;
    }

    // An interval needs extra samples when the segment arriving anywhere inside it isn't linear — two
    // endpoints alone would straighten out the ease.
    static bool NeedsSubdivision(
        List<(string Property, List<Keyframe> Stops)> tracks, double from, double to, AnimationCurve fallback)
    {
        foreach (var (_, stops) in tracks)
            for (var i = 1; i < stops.Count; i++)
            {
                // The segment [stops[i-1], stops[i]] overlaps this interval, and it curves.
                if (stops[i].Time <= from || stops[i - 1].Time >= to) continue;
                if (Math.Abs(stops[i].Value - stops[i - 1].Value) < 1e-9) continue;   // flat: curve is moot
                if ((stops[i].Curve ?? fallback) != AnimationCurve.Linear) return true;
            }
        return false;
    }

    /// <summary>
    /// A stable, collision-resistant name for a timeline's generated <c>@keyframes</c> rule, derived from
    /// its wire string (FNV-1a — no reflection, no allocation beyond the name itself). Identical timelines
    /// on different nodes share one rule.
    /// </summary>
    public static string RuleName(string tracks)
    {
        uint hash = 2166136261;
        foreach (var c in tracks)
        {
            hash ^= c;
            hash *= 16777619;
        }
        return "sdn-kf-" + hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Advances a free-running timeline clock and returns the phase (0–1) to sample at.
    /// <paramref name="elapsed"/> is seconds since the timeline armed, including <paramref name="delay"/>.
    /// <paramref name="repeat"/> is null for play-once, -1 for forever, or a cycle count;
    /// <paramref name="autoreverse"/> plays alternate cycles backwards. <paramref name="finished"/> is true
    /// once a play-once or finite-count timeline has settled on its final value.
    /// </summary>
    public static double Phase(double elapsed, double duration, double delay, int? repeat, bool autoreverse, out bool finished)
    {
        finished = false;
        if (elapsed <= delay) return 0;
        var t = (elapsed - delay) / Math.Max(0.001, duration);

        if (repeat is null)
        {
            if (t >= 1) { finished = true; return 1; }
            return t;
        }

        var cycle = (int)Math.Floor(t);
        if (repeat >= 0 && cycle >= repeat)
        {
            finished = true;
            // A finite autoreversing run ends where it started; otherwise on the track's last value.
            return autoreverse && repeat % 2 == 0 ? 0 : 1;
        }
        var within = t - cycle;
        return autoreverse && cycle % 2 == 1 ? 1 - within : within;
    }
}
