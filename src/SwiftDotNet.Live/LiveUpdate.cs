using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// An **Android 16 Live Update** — the structural analog of an iOS Live Activity: a promoted ongoing
/// notification that gets a status-bar chip and lock-screen prominence, driven by
/// <c>Notification.ProgressStyle</c> with <c>requestPromotedOngoing(true)</c> (API 36).
///
/// This is a **data model, not a view tree, and that is the whole point.** A Live Update is *templated*:
/// you supply segments, points, a tracker icon and text, and the system draws it. There is no
/// <c>RemoteViews</c> to hand over and no way to influence the layout. Modelling it as a
/// <see cref="LiveView"/> would reintroduce exactly the silent-drop failure this vocabulary exists to
/// prevent — a developer would write a stack with three labels and see one line of system text.
///
/// So an app that wants the best of both declares a <see cref="LiveActivity{TState}"/> *and* a
/// <see cref="LiveUpdate"/>. On API 36+ the Live Update wins; below it, the activity's
/// <see cref="LiveSlot.LockScreen"/> tree renders as a custom-content notification.
/// </summary>
public sealed class LiveUpdate
{
    readonly List<LiveUpdateSegment> _segments = new();
    readonly List<LiveUpdatePoint> _points = new();

    /// <summary>The headline, e.g. "Arriving at 4:32 PM".</summary>
    public string Title { get; set; } = "";

    /// <summary>Supporting line beneath the title.</summary>
    public string? Text { get; set; }

    /// <summary>Overall completion, 0–1.</summary>
    public double Progress { get; set; }

    /// <summary>A drawable name for the marker that rides along the progress track.</summary>
    public string? TrackerIcon { get; set; }

    /// <summary>Whether the progress track is indeterminate; ignores <see cref="Progress"/>.</summary>
    public bool Indeterminate { get; set; }

    /// <summary>Coloured stretches of the track — a walk leg, then a train leg, then a walk leg.</summary>
    public IReadOnlyList<LiveUpdateSegment> Segments => _segments;

    /// <summary>Milestones drawn on the track — a transfer, a stop, a checkpoint.</summary>
    public IReadOnlyList<LiveUpdatePoint> Points => _points;

    /// <summary>Adds a track segment of <paramref name="length"/> (a 0–1 fraction of the whole track).</summary>
    public LiveUpdate Segment(double length, SwiftColor color)
    {
        _segments.Add(new LiveUpdateSegment(length, color));
        return this;
    }

    /// <summary>Adds a milestone at <paramref name="at"/> (a 0–1 position along the track).</summary>
    public LiveUpdate Point(double at, SwiftColor color)
    {
        _points.Add(new LiveUpdatePoint(at, color));
        return this;
    }

    /// <summary>
    /// The flat wire form, in the same colon/semicolon grammar <see cref="Brush"/> uses — cheap for the
    /// Android driver to parse and free of a JSON dependency on either side.
    /// <code>
    ///   title|text|progress|indeterminate|trackerIcon|seg,color;seg,color|pt,color;pt,color
    /// </code>
    /// </summary>
    public string Serialize()
    {
        var sb = new StringBuilder(128);
        sb.Append(Escape(Title)).Append('|')
          .Append(Escape(Text ?? "")).Append('|')
          .Append(Progress.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
          .Append(Indeterminate ? "1" : "0").Append('|')
          .Append(Escape(TrackerIcon ?? "")).Append('|');

        for (var i = 0; i < _segments.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(_segments[i].Length.ToString("0.###", CultureInfo.InvariantCulture))
              .Append(',').Append(_segments[i].Color.Value);
        }
        sb.Append('|');

        for (var i = 0; i < _points.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(_points[i].At.ToString("0.###", CultureInfo.InvariantCulture))
              .Append(',').Append(_points[i].Color.Value);
        }

        return sb.ToString();
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("|", "\\p");
}

/// <summary>A coloured stretch of a <see cref="LiveUpdate"/> track. <paramref name="Length"/> is a 0–1 fraction.</summary>
public readonly record struct LiveUpdateSegment(double Length, SwiftColor Color);

/// <summary>A milestone on a <see cref="LiveUpdate"/> track. <paramref name="At"/> is a 0–1 position.</summary>
public readonly record struct LiveUpdatePoint(double At, SwiftColor Color);
