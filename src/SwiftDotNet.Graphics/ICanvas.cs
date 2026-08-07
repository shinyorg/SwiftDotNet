namespace SwiftDotNet.Graphics;

/// <summary>
/// The rasterizer seam. Everything the engine's paint pass can ask for, and nothing else.
/// </summary>
/// <remarks>
/// <para>The vocabulary is deliberately small and closed — rounded rects, ovals, circles, lines, images and
/// text, under a save/restore transform stack with rectangular clipping. That is the complete set of
/// primitives the DSL's node types actually draw, which is why a from-scratch GPU backend is tractable:
/// an SDF quad shader covers the first five, a glyph atlas covers text.</para>
///
/// <para>Notably absent: arbitrary paths. Adding a general path primitive would make every future backend
/// owe a full vector rasterizer. If a node type ever needs one, it belongs behind a capability check
/// rather than in this interface.</para>
///
/// <para>Implementations are used from the UI thread only and must not block — remote image loading is
/// handled by <see cref="ImageLoader"/>, which returns null until bytes land.</para>
/// </remarks>
public interface ICanvas
{
    /// <summary>Fills the entire surface, discarding what was there. Called once per frame, first.</summary>
    void Clear(Color color);

    /// <summary>Pushes the transform/clip state, returning a depth token for <see cref="RestoreToCount"/>.</summary>
    int Save();

    /// <summary>Pops back to a depth previously returned by <see cref="Save"/>.</summary>
    void RestoreToCount(int count);

    /// <summary>
    /// Begins an offscreen layer composited at <paramref name="opacity"/>. Needed for group opacity: fading
    /// a container must fade the composite, not each child independently, or overlapping children show
    /// through one another.
    /// </summary>
    void SaveLayer(float opacity);

    void Translate(float dx, float dy);
    void Scale(float sx, float sy);

    /// <summary>Rotates about an arbitrary pivot, in degrees clockwise.</summary>
    void RotateDegrees(float degrees, float pivotX, float pivotY);

    /// <summary>Intersects the clip with a rect. Rect-only by design — see the remarks on this interface.</summary>
    void ClipRect(Rect rect);

    void DrawRect(Rect rect, in Paint paint);
    void DrawRoundRect(Rect rect, float radiusX, float radiusY, in Paint paint);
    void DrawOval(Rect rect, in Paint paint);
    void DrawCircle(float centerX, float centerY, float radius, in Paint paint);
    void DrawLine(float x0, float y0, float x1, float y1, in Paint paint);

    /// <summary>Draws a decoded image stretched to <paramref name="dest"/>, with linear filtering.</summary>
    void DrawImage(IImage image, Rect dest);

    /// <summary>
    /// Draws a single line of text with its baseline at <paramref name="baselineY"/>. The implementation
    /// owns shaping and per-run font fallback, and must produce the same advance width that
    /// <see cref="IFontProvider.Measure"/> reported for the same string.
    /// </summary>
    void DrawText(string text, float x, float baselineY, Font font, Color color);
}
