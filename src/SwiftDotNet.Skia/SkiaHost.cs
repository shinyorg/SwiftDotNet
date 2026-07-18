using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Abstraction over a platform canvas surface. A real host (a windowed <c>SKCanvasView</c> per OS via
/// SkiaSharp.Views.*) implements this to feed the engine a canvas + size + density each frame and to
/// pump pointer/keyboard events in; <see cref="SkiaImageHost"/> is the headless render-to-image variant.
/// </summary>
public interface ISkiaHost
{
    /// <summary>True when the OS is in dark appearance (drives <see cref="SkiaTheme"/> resolution).</summary>
    bool Dark { get; }

    /// <summary>Request a repaint (the engine calls this after applying a patch).</summary>
    void Invalidate();
}

/// <summary>
/// A headless host that renders the current scene to an off-screen <see cref="SKSurface"/> and encodes a
/// PNG — the Skia analog of the other backends' "SDN_TEST" harness. Lets us verify layout/paint and drive
/// interaction (tap → emit → re-render) without opening a window, and produces screenshots for review.
/// </summary>
public sealed class SkiaImageHost
{
    readonly SkiaBridge _bridge;

    public SkiaImageHost(SkiaBridge bridge) => _bridge = bridge;

    public bool Dark { get; set; }

    /// <summary>Render the current scene at <paramref name="width"/>×<paramref name="height"/> and return PNG bytes.</summary>
    public byte[] RenderPng(int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        _bridge.Paint(surface.Canvas, new SKSize(width, height), Dark);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Render straight to a PNG file (convenience for the sample harness).</summary>
    public void RenderToFile(string path, int width, int height)
        => File.WriteAllBytes(path, RenderPng(width, height));

    /// <summary>
    /// Simulate a tap at (x,y). Layout must reflect the given size first, so call after a render of the
    /// same dimensions. Returns true if a control handled it.
    /// </summary>
    public bool Tap(float x, float y) => _bridge.DispatchPointer(new SKPoint(x, y));

    /// <summary>Scroll the scrollable under (x,y) by dy pixels (positive = content moves up). Layout must be current.</summary>
    public bool Scroll(float x, float y, float dy) => _bridge.Scroll(new SKPoint(x, y), dy);

    /// <summary>Type into the focused text control (focus one by tapping it first).</summary>
    public void Type(string text) => _bridge.InsertText(text);

    /// <summary>Delete the last character of the focused text control.</summary>
    public void Backspace() => _bridge.DeleteBackward();

    /// <summary>Fire a long-press at (x,y).</summary>
    public bool LongPress(float x, float y) => _bridge.LongPress(new SKPoint(x, y));

    /// <summary>Fire a directional swipe (left/right/up/down) at (x,y).</summary>
    public bool Swipe(float x, float y, string direction) => _bridge.Swipe(new SKPoint(x, y), direction);

    /// <summary>Advance implicit animations by dt seconds; returns true while still animating.</summary>
    public bool Advance(double dt) => _bridge.Tick(dt);
}
