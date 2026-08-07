namespace SwiftDotNet.Graphics;

/// <summary>
/// A point in the engine's coordinate space (device-independent pixels, origin top-left).
/// </summary>
/// <remarks>
/// Member names deliberately mirror <c>SKPoint</c>. The engine used to be written directly against
/// SkiaSharp's value types, and matching the shape kept that move a pure rename rather than a rewrite —
/// which is what let the existing test suite validate the extraction.
/// </remarks>
public readonly record struct Point(float X, float Y)
{
    public static readonly Point Empty = new(0, 0);

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A width/height pair. Mirrors <c>SKSize</c>.</summary>
public readonly record struct Size(float Width, float Height)
{
    public static readonly Size Empty = new(0, 0);

    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>
/// An axis-aligned rectangle stored as edges (not origin+size), mirroring <c>SKRect</c> — the engine's
/// layout code reads <see cref="Left"/>/<see cref="Right"/> far more often than it reads a size, and
/// edge storage keeps <see cref="MidX"/>/<see cref="MidY"/> free.
/// </summary>
public readonly record struct Rect(float Left, float Top, float Right, float Bottom)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public float Width => Right - Left;
    public float Height => Bottom - Top;
    public float MidX => Left + Width / 2f;
    public float MidY => Top + Height / 2f;
    public Size Size => new(Width, Height);
    public Point Location => new(Left, Top);

    /// <summary>Builds a rect from a top-left corner and a size.</summary>
    public static Rect Create(float x, float y, float width, float height) => new(x, y, x + width, y + height);

    /// <summary>Builds a rect from a top-left corner and a size.</summary>
    public static Rect Create(Point origin, Size size) =>
        new(origin.X, origin.Y, origin.X + size.Width, origin.Y + size.Height);

    /// <summary>Grows (or, with negative values, shrinks) the rect on every side.</summary>
    public static Rect Inflate(Rect r, float dx, float dy) =>
        new(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

    /// <summary>
    /// Half-open containment: the left/top edges are inside, the right/bottom edges are not. This is what
    /// makes adjacent rows hit-test unambiguously — a point on a shared boundary belongs to exactly one.
    /// </summary>
    public bool Contains(Point p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    public bool Contains(float x, float y) => x >= Left && x < Right && y >= Top && y < Bottom;

    /// <summary>True when the two rects share any area. Used for viewport culling in scrolled lists.</summary>
    public bool IntersectsWith(Rect other) =>
        Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;

    /// <summary>The overlapping area of two rects, or <see cref="Empty"/> when they do not intersect.</summary>
    public Rect Intersect(Rect other)
    {
        var l = Math.Max(Left, other.Left);
        var t = Math.Max(Top, other.Top);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : new Rect(l, t, r, b);
    }

    /// <summary>The rect shifted by an offset, keeping its size.</summary>
    public Rect Offset(float dx, float dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    public override string ToString() => $"[{Left}, {Top} → {Right}, {Bottom}]";
}
