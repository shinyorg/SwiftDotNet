using System.Collections.Concurrent;

namespace SwiftDotNet.Graphics;

/// <summary>
/// One frame of live video, as straight RGBA8 pixels plus how they should be oriented on screen.
/// </summary>
/// <remarks>
/// <para>Pixels, not a texture handle: the seam has to work for a raster backend (Skia), a GPU backend
/// (WebGPU), a game engine (MonoGame/Godot/Unity) and a headless PNG harness, and the only thing all of
/// those can consume is a buffer. The cost is one copy + one upload per displayed frame, which at preview
/// resolutions is what every "software preview" pipeline pays anyway.</para>
///
/// <para><see cref="Rotation"/> and <see cref="Mirrored"/> are carried rather than baked in because
/// rotating a buffer on the CPU costs another full pass, while the canvas can fold both into the matrix
/// for free. A camera sensor is almost never mounted in the display's orientation — on Android the back
/// sensor is typically 90° out in portrait — and a front camera is expected to be mirrored, so a source
/// that ignored these would be sideways or "backwards" on most devices.</para>
/// </remarks>
public sealed class PixelFrame
{
    public PixelFrame(Pixels pixels, int rotation = 0, bool mirrored = false, TimeSpan timestamp = default)
    {
        Pixels = pixels;
        Rotation = ((rotation % 360) + 360) % 360;
        Mirrored = mirrored;
        Timestamp = timestamp;
    }

    /// <summary>The frame's pixels, straight (non-premultiplied) RGBA8, row-major.</summary>
    public Pixels Pixels { get; }

    /// <summary>Clockwise rotation, in degrees, to apply when drawing. 0/90/180/270.</summary>
    public int Rotation { get; }

    /// <summary>Flip horizontally when drawing (what a front-facing camera preview expects).</summary>
    public bool Mirrored { get; }

    /// <summary>Presentation time within the stream, or default for a live source.</summary>
    public TimeSpan Timestamp { get; }

    /// <summary>Displayed width after <see cref="Rotation"/> (a 90°/270° frame swaps its axes).</summary>
    public int DisplayWidth => Rotation is 90 or 270 ? Pixels.Height : Pixels.Width;

    /// <summary>Displayed height after <see cref="Rotation"/>.</summary>
    public int DisplayHeight => Rotation is 90 or 270 ? Pixels.Width : Pixels.Height;
}

/// <summary>
/// A producer of video frames for one node — a camera preview, a decoded movie, a screen share.
/// </summary>
/// <remarks>
/// <para>The contract is deliberately pull-with-a-nudge: the source parks its newest frame in
/// <see cref="CurrentFrame"/> from whatever thread the platform decodes on, bumps <see cref="Sequence"/>,
/// and calls the repaint hook it was handed. The renderer reads the frame on the UI thread during paint
/// and re-uploads only when the sequence moved. Nothing queues, so a source that outruns the display
/// drops frames instead of growing a backlog — which is what a preview wants.</para>
///
/// <para>Implement it by deriving from <see cref="FrameSource"/>, which owns the publish/sequence
/// bookkeeping; this interface exists so a provider can implement it over its own base class.</para>
/// </remarks>
public interface IFrameSource : IDisposable
{
    /// <summary>The newest frame, or null until the first one arrives (the node paints its placeholder).</summary>
    PixelFrame? CurrentFrame { get; }

    /// <summary>Increments on every published frame. The renderer uploads only when this changes.</summary>
    long Sequence { get; }

    /// <summary>A human-readable failure, or null. Set means the node paints an error state.</summary>
    string? Error { get; }

    /// <summary>The node's props changed (a new url, the other camera, play/pause). Called on the UI thread.</summary>
    void Configure(IReadOnlyDictionary<string, object?> props);

    /// <summary>Begin producing frames. Called once, on the UI thread, right after construction.</summary>
    void Start();

    /// <summary>Stop producing frames but stay reusable (the node scrolled out of view).</summary>
    void Stop();
}

/// <summary>
/// A frame source that is also <b>interactive</b> — the half a pumped <c>WebView</c> needs and a video
/// feed does not. The engine forwards taps, scrolls and typing that land on the node, in the surface's
/// own normalized space, so an off-screen browser behaves like a real one even though it is only ever
/// seen as pixels.
/// </summary>
/// <remarks>
/// <para>Coordinates are <b>normalized 0–1 within the node's content rect</b>, not pixels. The source
/// renders at whatever resolution it chose (and may change it on <see cref="Resize"/>), so it is the only
/// party that can map a point onto its own document — passing engine pixels would make every provider
/// re-derive a scale the engine already divided out.</para>
///
/// <para>Everything here is called on the UI thread, from hit-testing.</para>
/// </remarks>
public interface ISurfaceInput
{
    /// <summary>A press, drag or release at a normalized point within the surface.</summary>
    void Pointer(float x, float y, GesturePhase phase);

    /// <summary>A wheel or pan scroll at a normalized point, in engine pixels of delta.</summary>
    void Scroll(float x, float y, float deltaX, float deltaY);

    /// <summary>Text typed while the surface holds focus.</summary>
    void Text(string text);

    /// <summary>A named key (<c>"Backspace"</c>, <c>"Enter"</c>, <c>"ArrowDown"</c>, …).</summary>
    void Key(string key, bool down);

    /// <summary>The node's laid-out size changed, in engine units (DIPs).</summary>
    void Resize(float width, float height);
}

/// <summary>
/// Base class for an <see cref="IFrameSource"/>: owns the frame slot, the sequence counter, the
/// error latch, and the plumbing back to the node (repaint + event emission).
/// </summary>
public abstract class FrameSource : IFrameSource
{
    PixelFrame? _current;
    long _sequence;
    Action<string?> _emit = _ => { };
    Action _repaint = () => { };

    /// <inheritdoc/>
    public PixelFrame? CurrentFrame => Volatile.Read(ref _current);

    /// <inheritdoc/>
    public long Sequence => Interlocked.Read(ref _sequence);

    /// <inheritdoc/>
    public string? Error { get; private set; }

    /// <summary>Wire up the node plumbing. Called by the registry before <see cref="Start"/>.</summary>
    internal void Attach(Action<string?> emit, Action repaint)
    {
        _emit = emit;
        _repaint = repaint;
    }

    /// <summary>Publish a frame from any thread and ask the host to repaint.</summary>
    protected void Publish(PixelFrame frame)
    {
        Volatile.Write(ref _current, frame);
        Interlocked.Increment(ref _sequence);
        Error = null;
        _repaint();
    }

    /// <summary>
    /// Latch a failure. The node paints its error state and keeps the last frame it had, so a transient
    /// hiccup mid-stream does not blank the preview.
    /// </summary>
    protected void Fail(string message)
    {
        Error = message;
        Emit("error:" + Base64(message));
        _repaint();
    }

    /// <summary>Raise this node's event back to its C# handler (the <c>kind:body</c> grammar).</summary>
    protected void Emit(string value) => _emit(value);

    /// <summary>Ask the host to repaint without publishing a frame (a transport-state change).</summary>
    protected void RequestRepaint() => _repaint();

    /// <summary>Base64 of a UTF-8 string — the wire encoding for free-text event bodies.</summary>
    protected static string Base64(string s) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));

    /// <inheritdoc/>
    public abstract void Configure(IReadOnlyDictionary<string, object?> props);

    /// <inheritdoc/>
    public abstract void Start();

    /// <inheritdoc/>
    public abstract void Stop();

    /// <inheritdoc/>
    public virtual void Dispose() => Stop();
}

/// <summary>What a <see cref="FrameSourceFactory"/> is asked to build a source for.</summary>
/// <param name="NodeId">The node's stable id — also the key its surface is cached under.</param>
/// <param name="NodeType">The node type (<c>"VideoView"</c>, <c>"CameraView"</c>, …).</param>
/// <param name="Props">The node's props at creation time (source url, camera facing, …).</param>
public readonly record struct FrameSourceRequest(
    string NodeId,
    string NodeType,
    IReadOnlyDictionary<string, object?> Props);

/// <summary>
/// Builds a frame source for a node, or returns null to let the next factory try.
/// </summary>
public delegate IFrameSource? FrameSourceFactory(FrameSourceRequest request);

/// <summary>
/// Registry of frame-source providers — the seam a platform package plugs into so that
/// <c>VideoView</c>/<c>CameraView</c> show a real feed on a self-drawing backend.
/// </summary>
/// <remarks>
/// <para>Mirrors <see cref="VisualRenderers"/> and <see cref="PlatformViews"/>: a static registry the
/// engine consults, so adding a platform needs no engine fork. Factories are tried <b>most recently
/// registered first</b> and the first non-null result wins, which is what lets a test or a sample install
/// a synthetic source over a real one.</para>
///
/// <para>With nothing registered the node paints an honest placeholder rather than a fake feed.</para>
/// </remarks>
public static class FrameSources
{
    static readonly List<(string? Type, FrameSourceFactory Factory)> Factories = new();

    /// <summary>Register a factory for every video node type.</summary>
    public static void Register(FrameSourceFactory factory) => Register(null, factory);

    /// <summary>Register a factory for one node type only (<c>"CameraView"</c>, <c>"VideoView"</c>).</summary>
    public static void Register(string? nodeType, FrameSourceFactory factory)
    {
        lock (Factories) Factories.Insert(0, (nodeType, factory));
    }

    /// <summary>True when at least one provider is registered (the renderer's cheap early-out).</summary>
    public static bool Any
    {
        get { lock (Factories) return Factories.Count > 0; }
    }

    /// <summary>
    /// Register the built-in generated <see cref="TestPatternSource"/> for every video node — a
    /// live feed on any backend with no camera, codec or permission involved. For samples, harnesses
    /// and tests.
    /// </summary>
    public static void RegisterTestPattern(int width = 640, int height = 360, double fps = 30) =>
        Register(request => new TestPatternSource(
            width, height, fps, camera: request.NodeType == SurfaceRenderer.CameraNode));

    /// <summary>Drop every registered factory. For tests and host teardown.</summary>
    public static void Clear()
    {
        lock (Factories) Factories.Clear();
    }

    internal static IFrameSource? Create(FrameSourceRequest request)
    {
        (string? Type, FrameSourceFactory Factory)[] snapshot;
        lock (Factories) snapshot = Factories.ToArray();

        foreach (var (type, factory) in snapshot)
        {
            if (type is not null && !string.Equals(type, request.NodeType, StringComparison.Ordinal)) continue;
            IFrameSource? source;
            try
            {
                source = factory(request);
            }
            catch
            {
                // A provider that throws while probing (no camera permission yet, no decoder) must not take
                // the frame down — the next factory gets its turn and, failing that, the node placeholders.
                continue;
            }
            if (source is not null) return source;
        }
        return null;
    }
}

/// <summary>
/// Something that can turn raw pixels into a drawable <see cref="IImage"/> without going through an
/// encoded container. Implemented by a backend's <see cref="IImageDecoder"/> alongside its decode methods.
/// </summary>
/// <remarks>
/// This is what makes video possible on a self-drawing backend: <see cref="IImageDecoder"/> only speaks
/// PNG/JPEG bytes, and round-tripping every video frame through an encoder would cost more than the
/// decode did. A decoder that does not implement this leaves <c>VideoView</c> painting its placeholder —
/// the honest outcome, since that rasterizer has no way to accept the pixels.
/// </remarks>
public interface IPixelUploader
{
    /// <summary>Upload pixels as a new image, or null if this rasterizer cannot.</summary>
    IImage? Upload(Pixels pixels);

    /// <summary>
    /// Refresh an image previously returned by <see cref="Upload"/> in place, returning false if it
    /// cannot be reused (different dimensions, immutable texture) so the caller uploads a new one.
    /// Saves an allocation per frame where the backend supports it.
    /// </summary>
    /// <remarks>Not a default interface method on purpose — the netstandard2.1 (Unity/IL2CPP) target
    /// compiles this assembly too, and DIM support there is not something to bet a frame loop on.</remarks>
    bool TryUpdate(IImage image, Pixels pixels);
}

/// <summary>What the renderer needs to draw a node's surface this frame.</summary>
/// <param name="HasSource">False when no provider would serve this node at all — the node then keeps
/// whatever placeholder its type already paints.</param>
/// <param name="Image">The uploaded frame, or null when there is nothing to draw yet.</param>
/// <param name="Frame">The frame behind <paramref name="Image"/> (orientation, timing).</param>
/// <param name="Error">A failure to surface, or null.</param>
internal readonly record struct SurfaceDraw(bool HasSource, IImage? Image, PixelFrame? Frame, string? Error);

/// <summary>
/// One bridge's live surfaces — a camera preview, a decoded movie, an off-screen web page: per node,
/// the source, its uploaded texture, and the lifetime bookkeeping that decides when a source is no
/// longer on screen and should be released.
/// </summary>
/// <remarks>
/// <para>A custom renderer is only ever called to measure and paint — there is no "this node went away"
/// callback, and there cannot be one, because a <c>setChildren</c> patch can drop a whole subtree without
/// the renderer hearing about it. So lifetime is resolved by sweeping: every paint touches its surface,
/// and a surface untouched for <see cref="GraceSeconds"/> of wall clock is disposed at the end of a
/// frame. A camera that scrolls out of view or lands in a hidden tab therefore shuts its hardware down on
/// its own, and comes back when it is painted again.</para>
///
/// <para>The sweep only runs while frames are being drawn, which is exactly when a live source is also
/// pumping repaints. An app that goes completely idle keeps whatever it had, by design: nothing is
/// producing, so nothing is being held open.</para>
///
/// <para>Owned by the <see cref="VisualBridge"/> rather than static, because node ids are <em>structural
/// paths</em> and repeat across trees: two bridges in one process (two windows, or two tests) would
/// otherwise hand each other's surfaces out under the same key.</para>
/// </remarks>
public sealed class FrameSurfaces
{
    /// <summary>
    /// How long a surface survives without being painted before it is released. Generous on purpose: a
    /// tab switch or a fling that scrolls a preview off and straight back should not restart the camera.
    /// </summary>
    public static double GraceSeconds { get; set; } = 2.0;

    sealed class Surface : IDisposable
    {
        public required IFrameSource Source { get; init; }
        public IImage? Image;
        public long UploadedSequence = -1;
        public double LastTouched;
        public float LastWidth = -1, LastHeight = -1;
        public Dictionary<string, object?> LastProps = new();

        public void Dispose()
        {
            Source.Dispose();
            if (Image is IDisposable d) d.Dispose();
            Image = null;
        }
    }

    readonly ConcurrentDictionary<string, Surface> _live = new();
    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    /// <summary>Number of live surfaces on this bridge. Diagnostics and tests.</summary>
    public int Count => _live.Count;

    /// <summary>
    /// The frame to draw for <paramref name="ctx"/>, creating and starting the source on first paint and
    /// re-uploading only when a new frame has been published. Null while there is nothing to show.
    /// </summary>
    internal SurfaceDraw Acquire(VisualRenderContext ctx)
    {
        var id = ctx.Id;
        if (!_live.TryGetValue(id, out var surface))
        {
            if (!FrameSources.Any) return default;
            var source = FrameSources.Create(new FrameSourceRequest(id, ctx.NodeType, ctx.Props));
            if (source is null) return default;

            // Capture the emit/repaint plumbing before Start so a source that fails synchronously (no
            // permission, missing file) still reports through the node's own event channel.
            if (source is FrameSource concrete)
                concrete.Attach(ctx.Emit, ctx.RequestRepaint);

            surface = new Surface { Source = source, LastProps = Snapshot(ctx.Props) };
            if (!_live.TryAdd(id, surface))
            {
                surface.Dispose();
                surface = _live[id];
            }
            else
            {
                // Configure before Start so a source only ever parses props in one place: it is handed
                // the initial values exactly as it will be handed every later change.
                try
                {
                    source.Configure(ctx.Props);
                    source.Start();
                }
                catch (Exception ex) { return new SurfaceDraw(true, null, null, ex.Message); }
            }
        }
        else if (PropsChanged(surface.LastProps, ctx.Props))
        {
            surface.LastProps = Snapshot(ctx.Props);
            try { surface.Source.Configure(ctx.Props); }
            catch (Exception ex) { return new SurfaceDraw(true, null, null, ex.Message); }
        }

        surface.LastTouched = Clock.Elapsed.TotalSeconds;

        var frame = surface.Source.CurrentFrame;
        if (frame is null) return new SurfaceDraw(true, null, null, surface.Source.Error);

        var sequence = surface.Source.Sequence;
        if (sequence != surface.UploadedSequence)
        {
            if (ctx.Images is IPixelUploader uploader)
            {
                if (surface.Image is not null && uploader.TryUpdate(surface.Image, frame.Pixels))
                {
                    surface.UploadedSequence = sequence;
                }
                else
                {
                    var image = uploader.Upload(frame.Pixels);
                    if (image is not null)
                    {
                        if (surface.Image is IDisposable old) old.Dispose();
                        surface.Image = image;
                        surface.UploadedSequence = sequence;
                    }
                }
            }
            else
            {
                // The rasterizer cannot take raw pixels. Say so once, through the same channel a decode
                // failure uses, rather than silently painting a dead placeholder forever.
                return new SurfaceDraw(true, null, frame,
                    "This backend cannot display frames (no pixel uploader).");
            }
        }

        return new SurfaceDraw(true, surface.Image, frame, surface.Source.Error);
    }

    /// <summary>
    /// The interactive source behind a node, or null when it has none (no source yet, or a source that
    /// only produces pixels). How a tap on a pumped <c>WebView</c> reaches the browser.
    /// </summary>
    internal ISurfaceInput? InputFor(string nodeId) =>
        _live.TryGetValue(nodeId, out var surface) ? surface.Source as ISurfaceInput : null;

    /// <summary>True when this node already has a live source (so the engine should not placeholder it).</summary>
    internal bool Has(string nodeId) => _live.ContainsKey(nodeId);

    /// <summary>Tell an interactive source its box changed, at most once per size.</summary>
    internal void NotifySize(string nodeId, float width, float height)
    {
        if (!_live.TryGetValue(nodeId, out var surface)) return;
        if (surface.LastWidth == width && surface.LastHeight == height) return;
        surface.LastWidth = width;
        surface.LastHeight = height;
        (surface.Source as ISurfaceInput)?.Resize(width, height);
    }

    /// <summary>Release surfaces that were not painted this frame. Called by the engine after each draw.</summary>
    internal void Sweep()
    {
        if (_live.IsEmpty) return;
        var now = Clock.Elapsed.TotalSeconds;
        foreach (var pair in _live)
        {
            if (now - pair.Value.LastTouched <= GraceSeconds) continue;
            if (_live.TryRemove(pair.Key, out var dead)) dead.Dispose();
        }
    }

    /// <summary>Dispose every live surface on this bridge (host teardown, tests).</summary>
    public void Clear()
    {
        foreach (var key in _live.Keys)
            if (_live.TryRemove(key, out var s)) s.Dispose();
    }

    static Dictionary<string, object?> Snapshot(IReadOnlyDictionary<string, object?> props) => new(props);

    static bool PropsChanged(Dictionary<string, object?> last, IReadOnlyDictionary<string, object?> now)
    {
        if (last.Count != now.Count) return true;
        foreach (var (key, value) in now)
            if (!last.TryGetValue(key, out var previous) || !Equals(previous, value)) return true;
        return false;
    }
}
