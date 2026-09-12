using System.Globalization;
using AVFoundation;
using CoreMedia;
using CoreVideo;
using Foundation;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Plays a movie with <see cref="AVPlayer"/> and hands the engine its frames — the Apple half of
/// <c>VideoView</c> on the self-drawing backends.
/// </summary>
/// <remarks>
/// <para>An <see cref="AVPlayerItemVideoOutput"/> is attached to the item and polled: AVFoundation does
/// all the decoding, timing and audio, and only the <em>picture</em> is pulled out as a
/// <see cref="CVPixelBuffer"/>. That keeps hardware decode and the audio clock — the two things a
/// hand-rolled decoder gets wrong — while still letting the canvas own compositing.</para>
///
/// <para>The poll runs on a timer rather than a <c>CADisplayLink</c> so one implementation covers iOS,
/// Mac Catalyst, macOS and tvOS. It asks for a frame at the display's current item time and publishes
/// only when the output says a new one exists, so polling faster than the movie's frame rate costs a
/// comparison, not a copy.</para>
/// </remarks>
public sealed class AppleVideoSource : FrameSource
{
    readonly PixelBufferConverter _converter = new();
    readonly string _source;

    AVPlayer? _player;
    AVPlayerItemVideoOutput? _output;
    NSObject? _endObserver;
    Timer? _pump;

    bool _playing = true;
    bool _loop;
    bool _muted;
    float _volume = 1;
    double _lastSeekRequest = double.NaN;
    bool _ready;
    double _lastReportedPosition = -1;

    public AppleVideoSource(string source) => _source = source;

    /// <summary>True when this looks like something AVFoundation can open.</summary>
    public static bool CanOpen(string source) =>
        source.Length > 0 && (Uri.TryCreate(source, UriKind.Absolute, out _) || File.Exists(source));

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("playing", out var playing) && playing is bool on) _playing = on;
        if (props.TryGetValue("loop", out var loop) && loop is bool l) _loop = l;
        if (props.TryGetValue("muted", out var muted) && muted is bool m) _muted = m;
        if (props.TryGetValue("volume", out var volume) && volume is double v) _volume = (float)v;

        if (props.TryGetValue("seekTo", out var seek) && seek is double seconds && !Equals(seconds, _lastSeekRequest))
        {
            _lastSeekRequest = seconds;
            _player?.Seek(CMTime.FromSeconds(seconds, 600));
        }

        Apply();
    }

    void Apply()
    {
        if (_player is null) return;
        _player.Muted = _muted;
        _player.Volume = _volume;
        if (_playing) _player.Play();
        else _player.Pause();
    }

    public override void Start()
    {
        var url = _source.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                  _source.Contains("://", StringComparison.Ordinal)
            ? NSUrl.FromString(_source)
            : NSUrl.FromFilename(_source);

        if (url is null)
        {
            Fail($"Not a URL AVFoundation can open: {_source}");
            return;
        }

        var item = new AVPlayerItem(url);
        _output = new AVPlayerItemVideoOutput(new CVPixelBufferAttributes
        {
            PixelFormatType = CVPixelFormatType.CV32BGRA,
        });
        item.AddOutput(_output);

        _player = new AVPlayer(item);
        _endObserver = NSNotificationCenter.DefaultCenter.AddObserver(
            AVPlayerItem.DidPlayToEndTimeNotification, HandleEnded, item);

        Apply();

        // ~60Hz. The output is only asked for a frame it says it has, so a 24fps movie still costs one
        // HasNewPixelBuffer call per tick and copies 24 times a second.
        _pump = new Timer(_ => Pump(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(16));
    }

    void HandleEnded(NSNotification _)
    {
        if (_loop)
        {
            _player?.Seek(CMTime.Zero);
            _player?.Play();
            return;
        }
        Emit("ended:");
    }

    void Pump()
    {
        if (_output is null || _player is null) return;

        var itemTime = _output.GetItemTime(CoreAnimation.CAAnimation.CurrentMediaTime());
        if (!_output.HasNewPixelBufferForItemTime(itemTime)) return;

        var displayTime = CMTime.Invalid;
        using var buffer = _output.CopyPixelBuffer(itemTime, ref displayTime);
        if (buffer is null) return;

        var pixels = _converter.Convert(buffer);
        if (pixels is null) return;

        if (!_ready)
        {
            _ready = true;
            var duration = _player.CurrentItem?.Duration ?? CMTime.Invalid;
            var seconds = duration.IsInvalid || duration.IsIndefinite ? 0 : duration.Seconds;
            Emit($"ready:{pixels.Width},{pixels.Height},{Fmt(seconds)}");
        }

        Publish(new PixelFrame(pixels, timestamp: TimeSpan.FromSeconds(itemTime.Seconds)));

        // Position is reported at ~4Hz, not per frame: it drives UI state (a scrubber, a time label) and
        // pushing 60 renders a second through the bridge for a label is how a video view eats a CPU.
        var position = Math.Floor(itemTime.Seconds * 4) / 4;
        if (Math.Abs(position - _lastReportedPosition) > 0.001)
        {
            _lastReportedPosition = position;
            Emit($"position:{Fmt(position)}");
        }
    }

    static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public override void Stop()
    {
        _pump?.Dispose();
        _pump = null;
        _player?.Pause();
    }

    public override void Dispose()
    {
        Stop();
        if (_endObserver is not null)
        {
            NSNotificationCenter.DefaultCenter.RemoveObserver(_endObserver);
            _endObserver.Dispose();
            _endObserver = null;
        }
        _output?.Dispose();
        _output = null;
        _player?.Dispose();
        _player = null;
        base.Dispose();
    }
}
