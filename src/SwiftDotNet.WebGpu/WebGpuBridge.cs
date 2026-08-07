using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// The WebGPU binding of the self-drawing engine.
/// </summary>
/// <remarks>
/// All the behaviour lives in <see cref="VisualBridge"/>; this subclass pins the GPU rasterizer — a
/// stb_truetype font provider and a pure-managed PNG decoder. Hosts drive it exactly like the Skia
/// bridge: push patches through <see cref="VisualBridge.Render"/>, feed pointer events, and call
/// <see cref="Record"/> each frame to produce a canvas for <see cref="WebGpuRenderer"/>.
/// </remarks>
public sealed class WebGpuBridge : VisualBridge, IDisposable
{
    readonly WebGpuFonts _fonts;

    public WebGpuBridge() : this(new WebGpuFonts(), new WebGpuImages()) { }

    WebGpuBridge(WebGpuFonts fonts, WebGpuImages images) : base(fonts, images) => _fonts = fonts;

    /// <summary>
    /// Lays out and records the current scene at <paramref name="size"/>, returning the canvas to hand to
    /// <see cref="WebGpuRenderer.Render"/>. Recording is CPU-only — nothing touches the GPU until the
    /// renderer submits.
    /// </summary>
    public WebGpuCanvas Record(Graphics.Size size, bool dark)
    {
        var canvas = new WebGpuCanvas(_fonts, size);
        Draw(canvas, size, dark);
        return canvas;
    }

    public void Dispose() => _fonts.Dispose();
}

/// <summary>
/// A headless host that renders the current scene to an off-screen texture and reads the pixels back —
/// the WebGPU analog of <c>SkiaImageHost</c>. Lets the backend be verified against real GPU output, and
/// drives interaction (tap → emit → re-render) without opening a window.
/// </summary>
public sealed class WebGpuImageHost : IDisposable
{
    readonly WebGpuBridge _bridge;
    readonly WebGpuRenderer _renderer;

    public WebGpuImageHost(WebGpuBridge bridge)
    {
        _bridge = bridge;
        _renderer = new WebGpuRenderer();
    }

    public bool Dark { get; set; }

    /// <summary>Which native API wgpu chose (Metal, Vulkan, D3D12, …).</summary>
    public string Backend => _renderer.BackendName;

    /// <summary>Render at <paramref name="width"/>×<paramref name="height"/> and return straight RGBA8 bytes.</summary>
    public byte[] RenderRgba(int width, int height)
    {
        var canvas = _bridge.Record(new Graphics.Size(width, height), Dark);
        return _renderer.RenderToRgba(canvas);
    }

    /// <summary>The pixel at (x, y) of a freshly rendered frame. For tests and diagnostics.</summary>
    public Graphics.Color PixelAt(byte[] rgba, int width, int x, int y)
    {
        var i = (y * width + x) * 4;
        return new Graphics.Color(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    /// <summary>Simulate a tap. Layout must be current, so render at the same size first.</summary>
    public bool Tap(float x, float y) => _bridge.DispatchPointer(new Graphics.Point(x, y));

    /// <summary>Scroll the scrollable under (x,y) by dy pixels.</summary>
    public bool Scroll(float x, float y, float dy) => _bridge.Scroll(new Graphics.Point(x, y), dy);

    /// <summary>Advance implicit animations by dt seconds; true while still animating.</summary>
    public bool Advance(double dt) => _bridge.Tick(dt);

    public void Dispose() => _renderer.Dispose();
}
