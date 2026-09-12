namespace SwiftDotNet.Graphics;

/// <summary>
/// A generated moving test pattern — SMPTE-ish colour bars with a sweeping band and a progress rule.
/// </summary>
/// <remarks>
/// <para>Registered with <see cref="FrameSources.RegisterTestPattern"/>, it makes <c>VideoView</c> and
/// <c>CameraView</c> show a live, obviously-synthetic feed on any backend, with no camera, no codec and
/// no permissions. That is what the test suite asserts against and what the headless PNG harness draws,
/// and it is the fastest way to check that a new host's repaint pump actually runs.</para>
///
/// <para>It is deliberately unmistakable for a real feed. The point of the placeholder rule elsewhere in
/// this file is not to fake capture; this exists to exercise the pipe.</para>
/// </remarks>
public sealed class TestPatternSource : FrameSource
{
    static readonly (byte R, byte G, byte B)[] Bars =
    [
        (0xC0, 0xC0, 0xC0), (0xC0, 0xC0, 0x00), (0x00, 0xC0, 0xC0), (0x00, 0xC0, 0x00),
        (0xC0, 0x00, 0xC0), (0xC0, 0x00, 0x00), (0x00, 0x00, 0xC0), (0x10, 0x10, 0x10),
    ];

    readonly int _width;
    readonly int _height;
    readonly double _fps;
    readonly byte[][] _buffers;
    readonly bool _camera;

    Timer? _timer;
    int _tick;
    bool _playing = true;
    bool _mirrored;

    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    /// <param name="fps">Frames published per second.</param>
    /// <param name="camera">Mirror + report as live, the way a front camera behaves.</param>
    public TestPatternSource(int width = 640, int height = 360, double fps = 30, bool camera = false)
    {
        _width = Math.Max(2, width);
        _height = Math.Max(2, height);
        _fps = Math.Clamp(fps, 1, 120);
        _camera = camera;
        // Double-buffered: the renderer reads the published frame on the UI thread while the next one is
        // being generated, and a single shared buffer would tear across that boundary.
        _buffers = [new byte[_width * _height * 4], new byte[_width * _height * 4]];
    }

    /// <summary>How many frames have been generated. Lets a test drive the pipe deterministically.</summary>
    public int TickCount => _tick;

    public override void Start()
    {
        Emit($"ready:{_width},{_height},{(_camera ? 0 : 10)}");
        var period = TimeSpan.FromSeconds(1 / _fps);
        _timer = new Timer(_ => Advance(), null, TimeSpan.Zero, period);
    }

    public override void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("playing", out var playing) && playing is bool on) _playing = on;
        if (props.TryGetValue("facing", out var facing)) _mirrored = facing as string == "front";
    }

    /// <summary>Generate and publish exactly one frame (what a test calls instead of waiting on the timer).</summary>
    public void Advance()
    {
        if (!_playing) return;
        var tick = Interlocked.Increment(ref _tick);
        var buffer = _buffers[tick % 2];
        Render(buffer, tick);
        Publish(new PixelFrame(new Pixels(_width, _height, buffer), mirrored: _camera && _mirrored,
            timestamp: TimeSpan.FromSeconds(tick / _fps)));
        if (!_camera) Emit($"position:{(tick / _fps).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}");
    }

    void Render(byte[] buffer, int tick)
    {
        var barWidth = Math.Max(1, _width / Bars.Length);
        var sweep = (int)((tick * 6L) % _width);
        var rule = _height - Math.Max(4, _height / 20);

        for (var y = 0; y < _height; y++)
        {
            var progress = y >= rule;
            for (var x = 0; x < _width; x++)
            {
                var i = (y * _width + x) * 4;
                byte r, g, b;
                if (progress)
                {
                    // A filling rule along the bottom: unambiguous evidence that frames are advancing,
                    // even in a single screenshot.
                    var on = x <= sweep;
                    r = g = b = on ? (byte)0xFF : (byte)0x20;
                }
                else
                {
                    var (br, bg, bb) = Bars[Math.Min(Bars.Length - 1, x / barWidth)];
                    var near = Math.Abs(x - sweep) < 24;
                    r = near ? Lift(br) : br;
                    g = near ? Lift(bg) : bg;
                    b = near ? Lift(bb) : bb;
                }
                buffer[i] = r;
                buffer[i + 1] = g;
                buffer[i + 2] = b;
                buffer[i + 3] = 0xFF;
            }
        }
    }

    static byte Lift(byte v) => (byte)Math.Min(0xFF, v + 0x40);

    public override void Dispose()
    {
        Stop();
        base.Dispose();
    }
}
