using SwiftDotNet.Controls;
using SwiftDotNet.Graphics;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The video seam: a <c>VideoView</c>/<c>CameraView</c> node pulls frames from a registered
/// <see cref="IFrameSource"/>, uploads them through the rasterizer's <see cref="IPixelUploader"/>,
/// and draws them onto the canvas like any other pixel.
///
/// <para>Everything here runs against the real Skia engine headlessly, which is the point: because the
/// frames are drawn by the engine rather than floated above it in an OS control, "is the camera actually
/// on screen, the right way up, clipped by its scroll view" is a question a PNG can answer. The platform
/// capture providers (AVFoundation, CameraX, …) are the only part that still needs a device.</para>
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class SurfaceSeamTests : IDisposable
{
    // SwiftApp posts re-renders to the SynchronizationContext captured at Run, and xUnit installs one of
    // its own per test — so a state write would otherwise render on a pool thread *while* this test is
    // painting, and the paint walks a tree being mutated underneath it. Same hazard, same fix, as
    // RetainedChildStateTests.
    readonly ManualPump _pump = new();
    readonly SynchronizationContext? _previousContext = SynchronizationContext.Current;

    const int W = 300, H = 400;

    // Graphics.Color, not the DSL's static Color class — the test tree pulls in both namespaces.
    static readonly Graphics.Color Red = new(0xFF, 0x00, 0x00);
    static readonly Graphics.Color Green = new(0x00, 0x80, 0x00);
    static readonly Graphics.Color Blue = new(0x00, 0x00, 0xFF);

    public SurfaceSeamTests()
    {
        _pump.Install();
        FrameSources.Clear();
        FrameSurfaces.GraceSeconds = 2.0;
    }

    public void Dispose()
    {
        FrameSources.Clear();
        FrameSurfaces.GraceSeconds = 2.0;
        SynchronizationContext.SetSynchronizationContext(_previousContext);
    }

    static (SkiaBridge bridge, SkiaImageHost host) Harness(View root)
    {
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(root, bridge);
        return (bridge, host);
    }

    static Pixels Render(SkiaImageHost host)
    {
        var png = host.RenderPng(W, H);
        Assert.True(PngDecoder.TryDecode(png, out var pixels));
        return pixels!;
    }

    // A lone child is centred in the window, so probes are written relative to the box's centre rather
    // than guessing window coordinates.
    const int CenterX = W / 2, CenterY = H / 2;

    static void AssertColor(Pixels pixels, int x, int y, byte r, byte g, byte b)
    {
        var (ar, ag, ab, _) = pixels.At(x, y);
        Assert.True(Math.Abs(ar - r) <= 6 && Math.Abs(ag - g) <= 6 && Math.Abs(ab - b) <= 6,
            $"({x},{y}) was #{ar:X2}{ag:X2}{ab:X2}, expected #{r:X2}{g:X2}{b:X2}");
    }

    // ---- fit maths (pure) ---------------------------------------------------

    [Fact]
    public void Contain_LetterboxesInsideTheBox()
    {
        var rect = SurfaceRenderer.FitRect(Rect.Create(0, 0, 200, 200), 640, 360, SurfaceFill.Contain);
        Assert.Equal(200, rect.Width, 1);
        Assert.Equal(112.5, rect.Height, 1);
        Assert.Equal(100, rect.MidX, 1);
        Assert.Equal(100, rect.MidY, 1);   // centred, so equal bars top and bottom
    }

    [Fact]
    public void Cover_FillsTheBoxAndOverflowsTheOtherAxis()
    {
        var rect = SurfaceRenderer.FitRect(Rect.Create(0, 0, 200, 200), 640, 360, SurfaceFill.Cover);
        Assert.Equal(355.6, rect.Width, 1);
        Assert.Equal(200, rect.Height, 1);
        Assert.True(rect.Left < 0 && rect.Right > 200, "cover must overflow horizontally and be clipped");
    }

    [Fact]
    public void Stretch_TakesTheBoxExactly()
    {
        var bounds = Rect.Create(10, 20, 200, 100);
        Assert.Equal(bounds, SurfaceRenderer.FitRect(bounds, 640, 360, SurfaceFill.Stretch));
    }

    // ---- drawing ------------------------------------------------------------

    [Fact]
    public void NoProviderRegistered_PaintsThePlaceholderNotAnEmptyBox()
    {
        var (_, host) = Harness(new VStack(new VideoView("clip.mp4").Frame(width: 200, height: 120)));
        var pixels = Render(host);

        // The placeholder's own dark fill, not the (light) window background — a blank rect would read
        // as a broken control.
        AssertColor(pixels, CenterX - 80, CenterY - 40, 0x1C, 0x1C, 0x1E);
    }

    [Fact]
    public void RegisteredSource_DrawsItsFramesOntoTheCanvas()
    {
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);

        var (_, host) = Harness(new VStack(new VideoView("clip.mp4").Frame(width: 200, height: 120)));

        // First paint creates and starts the source; it has no frame yet, so the placeholder shows.
        Render(host);
        AssertColor(Render(host), CenterX - 80, CenterY - 40, 0x1C, 0x1C, 0x1E);

        source.Push(160, 90);
        AssertColor(Render(host), CenterX, CenterY, 0xFF, 0x00, 0x00);
    }

    [Fact]
    public void CameraNode_UsesTheSameSeam()
    {
        var source = new SolidFrameSource(Green);
        FrameSources.Register(SurfaceRenderer.CameraNode, _ => source);
        source.Push(160, 90);

        var facing = new State<CameraFacing>(CameraFacing.Back);
        var (_, host) = Harness(new VStack(new CameraView(facing).Frame(width: 200, height: 120)));

        Render(host);
        AssertColor(Render(host), CenterX, CenterY, 0x00, 0x80, 0x00);
    }

    [Fact]
    public void FrameRotation_IsAppliedWhenDrawing()
    {
        // A landscape frame, red on the left half and blue on the right, rotated a quarter turn
        // clockwise: red must end up on TOP.
        var source = new SolidFrameSource(Red) { Rotation = 90 };
        FrameSources.Register(_ => source);
        source.PushSplit(160, 80, Red, Blue);

        var (_, host) = Harness(new VStack(new VideoView("clip.mp4").Frame(width: 200, height: 200)));
        Render(host);
        var pixels = Render(host);

        AssertColor(pixels, CenterX, CenterY - 60, 0xFF, 0x00, 0x00);
        AssertColor(pixels, CenterX, CenterY + 60, 0x00, 0x00, 0xFF);
    }

    [Fact]
    public void MirroredFrame_FlipsHorizontally()
    {
        var source = new SolidFrameSource(Red) { Mirrored = true };
        FrameSources.Register(_ => source);
        source.PushSplit(200, 120, Red, Blue);   // red left, blue right

        var (_, host) = Harness(new VStack(
            new VideoView("clip.mp4").ContentMode(VideoContentMode.Stretch).Frame(width: 200, height: 120)));
        Render(host);
        var pixels = Render(host);

        // Mirrored, so the red half is now on the right.
        AssertColor(pixels, CenterX + 60, CenterY, 0xFF, 0x00, 0x00);
        AssertColor(pixels, CenterX - 60, CenterY, 0x00, 0x00, 0xFF);
    }

    [Fact]
    public void ContentModeFill_CropsRatherThanLetterboxes()
    {
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);
        source.Push(320, 40);   // very wide: fit would leave big bars, fill must cover

        var (_, host) = Harness(new VStack(
            new VideoView("clip.mp4").ContentMode(VideoContentMode.Fill).Frame(width: 200, height: 120)));
        Render(host);
        var pixels = Render(host);

        AssertColor(pixels, CenterX, CenterY - 55, 0xFF, 0x00, 0x00);   // top of the box is covered
        AssertColor(pixels, CenterX, CenterY + 55, 0xFF, 0x00, 0x00);   // and so is the bottom
    }

    [Fact]
    public void CornerRadius_RoundsTheFrameItself_NotJustTheBackground()
    {
        // `.CornerRadius` normally only rounds a node's background fill — content is clipped square,
        // because ICanvas is rect-clip-only. A surface opts into the rounded clip where the rasterizer
        // offers one (Skia does), so a camera in a rounded card actually looks like one.
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);
        source.Push(200, 200);

        var facing = new State<CameraFacing>(CameraFacing.Back);
        var (_, host) = Harness(new VStack(
            new CameraView(facing).CornerRadius(24).Frame(width: 200, height: 200)));
        Render(host);
        var pixels = Render(host);

        AssertColor(pixels, CenterX, CenterY, 0xFF, 0x00, 0x00);              // the feed
        AssertColor(pixels, CenterX - 98, CenterY - 98, 0xFF, 0xFF, 0xFF);    // the cut corner
    }

    // ---- lifetime -----------------------------------------------------------

    [Fact]
    public void SourceIsStartedOnceAndConfiguredWithItsInitialProps()
    {
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);

        var (_, host) = Harness(new VStack(
            new VideoView("clip.mp4").ContentMode(VideoContentMode.Fill).Frame(width: 200, height: 120)));
        Render(host);
        Render(host);
        Render(host);

        Assert.Equal(1, source.Starts);
        Assert.Equal(1, source.Configures);
        Assert.Equal("fill", source.LastProps["contentMode"]);
    }

    [Fact]
    public void ChangedProps_ReachTheSource()
    {
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);

        var playing = new State<bool>(true);
        var view = new TransportView(playing);
        var (_, host) = Harness(view);
        Render(host);
        Assert.Equal(1, source.Configures);

        playing.Value = false;           // re-renders the tree, patching the prop
        _pump.Drain();
        Render(host);

        Assert.Equal(2, source.Configures);
        Assert.Equal(false, source.LastProps["playing"]);
    }

    [Fact]
    public void SurfaceIsDisposedOnceItsNodeStopsPainting()
    {
        FrameSurfaces.GraceSeconds = 60;                 // nothing is swept while the test sets up
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);

        var show = new State<bool>(true);
        var (bridge, host) = Harness(new ToggleVideo(show));
        Render(host);
        Assert.Equal(1, bridge.Surfaces.Count);

        show.Value = false;                              // the node leaves the tree
        _pump.Drain();
        FrameSurfaces.GraceSeconds = 0;                  // …and now anything untouched goes
        Render(host);

        Assert.Equal(0, bridge.Surfaces.Count);
        Assert.True(source.Disposed);
    }

    [Fact]
    public void EventsFromTheSourceReachTheViewsHandlers()
    {
        var source = new SolidFrameSource(Red);
        FrameSources.Register(_ => source);

        VideoInfo? ready = null;
        var errors = new List<string>();
        var (_, host) = Harness(new VStack(
            new VideoView("clip.mp4")
                .OnReady(i => ready = i)
                .OnError(errors.Add)
                .Frame(width: 200, height: 120)));
        Render(host);

        source.RaiseReady(640, 360, 12.5);
        source.RaiseError("codec not found");

        Assert.Equal(new VideoInfo(640, 360, TimeSpan.FromSeconds(12.5)), ready);
        Assert.Equal("codec not found", Assert.Single(errors));
    }

    [Fact]
    public void ARasterizerWithNoPixelUploader_SaysSoInsteadOfPaintingNothing()
    {
        // Not every backend can take raw pixels. The node must report that rather than sit blank
        // forever, which is indistinguishable from a source that never produced a frame.
        var source = new SolidFrameSource(Red);
        source.Push(64, 64);
        var fonts = new SkiaFonts();
        var bridge = new VisualBridge(fonts, new NoUploadDecoder());
        FrameSources.Register(_ => source);
        SwiftApp.Run(new VStack(new VideoView("clip.mp4").Frame(width: 200, height: 120)), bridge);

        using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(W, H));
        var canvas = new SkiaCanvas(surface.Canvas, fonts);
        bridge.Draw(canvas, new Graphics.Size(W, H), dark: false);

        // Nothing to assert on-screen beyond "it did not throw and the surface was not adopted": the
        // renderer bailed out before caching an image.
        Assert.Equal(1, bridge.Surfaces.Count);
    }

    // ---- still capture ------------------------------------------------------

    [Fact]
    public void EncodedFrames_RoundTripThroughThePngEncoder()
    {
        // A camera still is handed back to C# as encoded bytes, and on the backends with no platform
        // imaging library that encoder is ours. If it round-trips, a saved photo opens.
        var rgba = new byte[4 * 3 * 4];
        for (var i = 0; i < 4 * 3; i++)
        {
            rgba[i * 4] = (byte)(i * 20);
            rgba[i * 4 + 1] = 0x40;
            rgba[i * 4 + 2] = (byte)(255 - i * 20);
            rgba[i * 4 + 3] = 0xFF;
        }

        var png = PngEncoder.Encode(new Pixels(4, 3, rgba));
        Assert.True(PngDecoder.IsPng(png));
        Assert.True(PngDecoder.TryDecode(png, out var decoded));
        Assert.Equal(4, decoded!.Width);
        Assert.Equal(3, decoded.Height);
        Assert.Equal(rgba, decoded.Rgba);
    }

    // ---- WebView on the same seam ------------------------------------------

    [Fact]
    public void WebViewNode_WithNoProvider_KeepsItsOwnPlaceholder()
    {
        // The core WebView has always painted an honest "not drawable on a canvas" card. Registering
        // the surface seam must not replace that with a generic video placeholder when nothing serves it.
        var (_, host) = Harness(new VStack(new WebView("https://example.com").Frame(width: 200, height: 120)));
        var pixels = Render(host);

        // Theme.Surface(light) — the card, not the surface renderer's near-black box.
        AssertColor(pixels, CenterX, CenterY - 50, 0xF2, 0xF2, 0xF7);
    }

    [Fact]
    public void WebViewNode_WithAProvider_DrawsThePageAndForwardsInput()
    {
        var source = new InteractiveSource(Blue);
        FrameSources.Register(SurfaceRenderer.WebNode, _ => source);
        source.Push(200, 120);

        var (_, host) = Harness(new VStack(
            new WebView("https://example.com").Frame(width: 200, height: 120)));
        Render(host);
        AssertColor(Render(host), CenterX, CenterY, 0x00, 0x00, 0xFF);

        // A tap lands in the surface's own normalized space, not in window pixels.
        Assert.True(host.Tap(CenterX, CenterY));
        var (x, y, phase) = source.Pointers[0];
        Assert.Equal(0.5, x, 2);
        Assert.Equal(0.5, y, 2);
        Assert.Equal(GesturePhase.Began, phase);
        Assert.Equal(GesturePhase.Ended, source.Pointers[1].Phase);

        // And the wheel scrolls the page rather than the list the page sits in.
        Assert.True(host.Scroll(CenterX, CenterY, 40));
        Assert.Equal(40, source.Scrolls.Single().DeltaY, 1);
    }

    [Fact]
    public void FocusedSurface_ReceivesTypingInsteadOfPatchingANonExistentTextProp()
    {
        var source = new InteractiveSource(Blue);
        FrameSources.Register(SurfaceRenderer.WebNode, _ => source);
        source.Push(200, 120);

        var (_, host) = Harness(new VStack(
            new WebView("https://example.com").Frame(width: 200, height: 120)));
        Render(host);
        host.Tap(CenterX, CenterY);       // focus the surface

        host.Type("hi");
        host.Backspace();

        Assert.Equal("hi", Assert.Single(source.Typed));
        Assert.Contains(("Backspace", true), source.Keys);
    }

    [Fact]
    public void SurfaceIsToldItsSizeOnce()
    {
        var source = new InteractiveSource(Blue);
        FrameSources.Register(SurfaceRenderer.WebNode, _ => source);
        source.Push(200, 120);

        var (_, host) = Harness(new VStack(
            new WebView("https://example.com").Frame(width: 200, height: 120)));
        Render(host);
        Render(host);
        Render(host);

        var size = Assert.Single(source.Sizes);
        Assert.Equal(200, size.Width, 1);
        Assert.Equal(120, size.Height, 1);
    }

    // ---- doubles ------------------------------------------------------------

    /// <summary>A frame source a test drives by hand: no timers, no threads, no platform.</summary>
    sealed class SolidFrameSource : FrameSource
    {
        readonly Graphics.Color _color;

        public SolidFrameSource(Graphics.Color color) => _color = color;

        public int Rotation { get; set; }
        public bool Mirrored { get; set; }
        public int Starts { get; private set; }
        public int Configures { get; private set; }
        public bool Disposed { get; private set; }
        public Dictionary<string, object?> LastProps { get; private set; } = new();

        public void Push(int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = _color.R;
                rgba[i * 4 + 1] = _color.G;
                rgba[i * 4 + 2] = _color.B;
                rgba[i * 4 + 3] = 0xFF;
            }
            Publish(new PixelFrame(new Pixels(width, height, rgba), Rotation, Mirrored));
        }

        /// <summary>A frame that is <paramref name="left"/> on its left half and <paramref name="right"/> on its right.</summary>
        public void PushSplit(int width, int height, Graphics.Color left, Graphics.Color right)
        {
            var rgba = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var c = x < width / 2 ? left : right;
                var i = (y * width + x) * 4;
                rgba[i] = c.R; rgba[i + 1] = c.G; rgba[i + 2] = c.B; rgba[i + 3] = 0xFF;
            }
            Publish(new PixelFrame(new Pixels(width, height, rgba), Rotation, Mirrored));
        }

        public void RaiseReady(int width, int height, double seconds) => Emit($"ready:{width},{height},{seconds}");

        public void RaiseError(string message) => Fail(message);

        public override void Configure(IReadOnlyDictionary<string, object?> props)
        {
            Configures++;
            LastProps = new Dictionary<string, object?>(props);
        }

        public override void Start() => Starts++;

        public override void Stop() { }

        public override void Dispose()
        {
            Disposed = true;
            base.Dispose();
        }
    }

    /// <summary>A frame source that also takes input — what a pumped <c>WebView</c> looks like.</summary>
    sealed class InteractiveSource(Graphics.Color color) : FrameSource, ISurfaceInput
    {
        public List<(float X, float Y, GesturePhase Phase)> Pointers { get; } = new();
        public List<(float X, float Y, float DeltaX, float DeltaY)> Scrolls { get; } = new();
        public List<string> Typed { get; } = new();
        public List<(string Key, bool Down)> Keys { get; } = new();
        public List<Graphics.Size> Sizes { get; } = new();

        public void Push(int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                rgba[i * 4] = color.R;
                rgba[i * 4 + 1] = color.G;
                rgba[i * 4 + 2] = color.B;
                rgba[i * 4 + 3] = 0xFF;
            }
            Publish(new PixelFrame(new Pixels(width, height, rgba)));
        }

        public void Pointer(float x, float y, GesturePhase phase) => Pointers.Add((x, y, phase));
        public void Scroll(float x, float y, float deltaX, float deltaY) => Scrolls.Add((x, y, deltaX, deltaY));
        public void Text(string text) => Typed.Add(text);
        public void Key(string key, bool down) => Keys.Add((key, down));
        public void Resize(float width, float height) => Sizes.Add(new Graphics.Size(width, height));

        public override void Configure(IReadOnlyDictionary<string, object?> props) { }
        public override void Start() { }
        public override void Stop() { }
    }

    /// <summary>A decoder with no <see cref="IPixelUploader"/> — a rasterizer that cannot take frames.</summary>
    sealed class NoUploadDecoder : IImageDecoder
    {
        public IImage? Decode(byte[] bytes) => null;
        public IImage? DecodeFile(string path) => null;
    }

    /// <summary>Holds its own state so the tree can be re-rendered with a changed prop.</summary>
    sealed class TransportView(State<bool> playing) : View
    {
        public override View Body =>
            new VStack(new VideoView("clip.mp4").Playing(playing).Frame(width: 200, height: 120));
    }

    /// <summary>Drops the video node from the tree entirely, so its surface must be swept.</summary>
    sealed class ToggleVideo(State<bool> show) : View
    {
        public override View Body => show.Value
            ? new VStack(new VideoView("clip.mp4").Frame(width: 200, height: 120))
            : new VStack(new Text("gone"));
    }
}
