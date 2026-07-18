namespace SwiftDotNet;

/// <summary>Cross-axis alignment for a <see cref="VStack"/> (mirrors SwiftUI's <c>HorizontalAlignment</c>).</summary>
public enum HorizontalAlignment { Leading, Center, Trailing }

/// <summary>Cross-axis alignment for an <see cref="HStack"/> (mirrors SwiftUI's <c>VerticalAlignment</c>).</summary>
public enum VerticalAlignment { Top, Center, Bottom }

/// <summary>2-D alignment for a <see cref="ZStack"/>, a frame, or <c>.Align(...)</c> (mirrors SwiftUI's <c>Alignment</c>).</summary>
public enum Alignment
{
    TopLeading, Top, TopTrailing,
    Leading, Center, Trailing,
    BottomLeading, Bottom, BottomTrailing,
}

/// <summary>Edges for per-edge padding (mirrors SwiftUI's <c>Edge.Set</c>).</summary>
[Flags]
public enum Edge
{
    Top = 1,
    Leading = 2,
    Bottom = 4,
    Trailing = 8,
    Horizontal = Leading | Trailing,
    Vertical = Top | Bottom,
    All = Top | Leading | Bottom | Trailing,
}

internal static class AlignmentTokens
{
    public static string Token(this HorizontalAlignment a) => a switch
    {
        HorizontalAlignment.Leading => "leading",
        HorizontalAlignment.Trailing => "trailing",
        _ => "center",
    };

    public static string Token(this VerticalAlignment a) => a switch
    {
        VerticalAlignment.Top => "top",
        VerticalAlignment.Bottom => "bottom",
        _ => "center",
    };

    public static string Token(this Alignment a) => a switch
    {
        Alignment.TopLeading => "topLeading",
        Alignment.Top => "top",
        Alignment.TopTrailing => "topTrailing",
        Alignment.Leading => "leading",
        Alignment.Trailing => "trailing",
        Alignment.BottomLeading => "bottomLeading",
        Alignment.Bottom => "bottom",
        Alignment.BottomTrailing => "bottomTrailing",
        _ => "center",
    };
}
