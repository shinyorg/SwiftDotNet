using System.Globalization;

namespace SwiftDotNet;

/// <summary>How one grid track (a <see cref="Grid"/> column or row) is sized. Mirrors SwiftUI's
/// <c>GridItem</c> sizing cases, with WPF/MAUI's star weights spelled out.</summary>
public enum GridTrackKind
{
    /// <summary>Size to the largest child in the track.</summary>
    Auto,
    /// <summary>An exact number of points.</summary>
    Fixed,
    /// <summary>A weighted share of whatever is left after Fixed/Auto tracks are satisfied.</summary>
    Star,
    /// <summary>Content-sized, then clamped into <c>[min, max]</c>.</summary>
    Flexible,
}

/// <summary>
/// One column or row definition for a <see cref="Grid"/>. Build them with the factories —
/// <see cref="Auto"/>, <see cref="Fixed"/>, <see cref="Star"/>, <see cref="Flexible"/> — and pass the
/// set to <c>Grid.Columns(...)</c> / <c>Grid.Rows(...)</c>.
/// </summary>
public readonly struct GridTrack
{
    public GridTrackKind Kind { get; }
    /// <summary>Points for <see cref="GridTrackKind.Fixed"/>, weight for <see cref="GridTrackKind.Star"/>,
    /// lower bound for <see cref="GridTrackKind.Flexible"/>; unused for Auto.</summary>
    public double Value { get; }
    /// <summary>Upper bound for <see cref="GridTrackKind.Flexible"/>; <c>null</c> means unbounded.</summary>
    public double? Max { get; }

    GridTrack(GridTrackKind kind, double value, double? max) { Kind = kind; Value = value; Max = max; }

    /// <summary>A track sized to its largest child.</summary>
    public static GridTrack Auto => new(GridTrackKind.Auto, 0, null);

    /// <summary>A track of exactly <paramref name="points"/>.</summary>
    public static GridTrack Fixed(double points) => new(GridTrackKind.Fixed, Math.Max(0, points), null);

    /// <summary>A track taking <paramref name="weight"/> shares of the leftover space (WPF/MAUI <c>*</c>).</summary>
    public static GridTrack Star(double weight = 1) => new(GridTrackKind.Star, Math.Max(0, weight), null);

    /// <summary>A content-sized track clamped into <c>[min, max]</c> (SwiftUI's <c>GridItem(.flexible(minimum:maximum:))</c>).</summary>
    public static GridTrack Flexible(double min = 0, double? max = null)
        => new(GridTrackKind.Flexible, Math.Max(0, min), max);

    /// <summary>
    /// The compact wire token for this track: <c>auto</c>, <c>fixed:80</c>, <c>star:1.5</c>, or
    /// <c>flex:40:120</c> (<c>inf</c> for an unbounded maximum). Tracks ride as one comma-joined string
    /// so the hand-rolled <see cref="NodeJson"/> writer stays free of nested arrays.
    /// </summary>
    internal string Token() => Kind switch
    {
        GridTrackKind.Fixed => "fixed:" + N(Value),
        GridTrackKind.Star => "star:" + N(Value),
        GridTrackKind.Flexible => "flex:" + N(Value) + ":" + (Max is { } m ? N(m) : "inf"),
        _ => "auto",
    };

    static string N(double d) => d.ToString(CultureInfo.InvariantCulture);

    internal static string Join(GridTrack[] tracks)
    {
        var parts = new string[tracks.Length];
        for (var i = 0; i < tracks.Length; i++) parts[i] = tracks[i].Token();
        return string.Join(",", parts);
    }
}

/// <summary>
/// Which parts of an <see cref="AbsoluteLayout"/> child's <c>.LayoutBounds(...)</c> are fractions of the
/// layout's own size rather than points — mirrors MAUI's <c>AbsoluteLayoutFlags</c>. A proportional
/// coordinate of <c>0.5</c> is "half way across"; a proportional size of <c>0.5</c> is "half as wide".
/// </summary>
[Flags]
public enum LayoutFlags
{
    None = 0,
    XProportional = 1,
    YProportional = 2,
    WidthProportional = 4,
    HeightProportional = 8,
    PositionProportional = XProportional | YProportional,
    SizeProportional = WidthProportional | HeightProportional,
    All = PositionProportional | SizeProportional,
}

internal static class LayoutFlagTokens
{
    /// <summary>The wire token: the subset of <c>xywh</c> that is proportional ("" when nothing is).</summary>
    public static string Token(this LayoutFlags f)
    {
        Span<char> buf = stackalloc char[4];
        var n = 0;
        if ((f & LayoutFlags.XProportional) != 0) buf[n++] = 'x';
        if ((f & LayoutFlags.YProportional) != 0) buf[n++] = 'y';
        if ((f & LayoutFlags.WidthProportional) != 0) buf[n++] = 'w';
        if ((f & LayoutFlags.HeightProportional) != 0) buf[n++] = 'h';
        return n == 0 ? "" : new string(buf[..n]);
    }
}

/// <summary>
/// Shared interpretation of the <c>layoutBounds</c> wire modifier, so every backend resolves an
/// <see cref="AbsoluteLayout"/> child to the same rect.
/// </summary>
public static class AbsoluteLayoutBounds
{
    /// <summary>Reads the <c>flags</c> token (a subset of <c>xywh</c>) back into <see cref="LayoutFlags"/>.</summary>
    public static LayoutFlags Parse(string? token)
    {
        var flags = LayoutFlags.None;
        if (string.IsNullOrEmpty(token)) return flags;
        foreach (var c in token)
            flags |= c switch
            {
                'x' => LayoutFlags.XProportional,
                'y' => LayoutFlags.YProportional,
                'w' => LayoutFlags.WidthProportional,
                'h' => LayoutFlags.HeightProportional,
                _ => LayoutFlags.None,
            };
        return flags;
    }

    /// <summary>
    /// Resolves a child's declared bounds against the layout's own size.
    ///
    /// A proportional <em>size</em> is a straight fraction of the host. A proportional <em>position</em>
    /// is an anchor across the free space — <c>x: 0</c> is flush left, <c>1</c> flush right, <c>0.5</c>
    /// centered — which is MAUI's rule, and the only one where <c>1</c> stays on screen. A null
    /// width/height means the child sizes itself, so its measured size is used instead.
    /// </summary>
    public static (double X, double Y, double Width, double Height) Resolve(
        double x, double y, double? width, double? height, LayoutFlags flags,
        double hostWidth, double hostHeight, double naturalWidth, double naturalHeight)
    {
        var w = width is { } dw
            ? ((flags & LayoutFlags.WidthProportional) != 0 ? dw * hostWidth : dw)
            : naturalWidth;
        var h = height is { } dh
            ? ((flags & LayoutFlags.HeightProportional) != 0 ? dh * hostHeight : dh)
            : naturalHeight;

        var px = (flags & LayoutFlags.XProportional) != 0 ? (hostWidth - w) * x : x;
        var py = (flags & LayoutFlags.YProportional) != 0 ? (hostHeight - h) * y : y;
        return (px, py, Math.Max(0, w), Math.Max(0, h));
    }
}
