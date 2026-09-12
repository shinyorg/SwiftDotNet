using System.Diagnostics;
using System.Globalization;
using Android.Media;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Decodes a movie with <see cref="MediaCodec"/> and hands the engine its frames — the Android half of
/// <c>VideoView</c> on the self-drawing backends.
/// </summary>
/// <remarks>
/// <para><b>Why not <see cref="MediaPlayer"/> alone?</b> A <c>MediaPlayer</c> can only render into a
/// <c>Surface</c>, and a Surface belongs to a native view the canvas cannot composite. So the picture is
/// decoded here on the CPU path — <see cref="MediaExtractor"/> feeding a <c>MediaCodec</c> in ByteBuffer
/// mode, whose <c>YUV_420_888</c> output images convert to RGBA — while a <b>surface-less
/// <c>MediaPlayer</c> on the same source plays the audio and acts as the clock</b>. A MediaPlayer with no
/// surface renders no video, which is exactly what is wanted: it becomes an audio track that knows what
/// time it is, and frames are shown against its position, so audio and video stay in sync without this
/// code owning a clock.</para>
///
/// <para>Costs to know about: the source is opened twice (once per component), so a remote file is
/// fetched twice — prefer a local path or a cached file for anything large; and the colour conversion is
/// software, which is fine at 720p and not at 4K. On a device with a view hierarchy, the platform-view
/// route is still the better choice for full-screen playback. This is what makes video possible at all on
/// a host that has no views.</para>
/// </remarks>
public sealed class AndroidVideoSource : FrameSource
{
    readonly YuvConverter _converter = new();
    readonly string _source;
    readonly Stopwatch _fallbackClock = new();

    MediaExtractor? _extractor;
    MediaCodec? _codec;
    MediaPlayer? _audio;
    Thread? _worker;
    volatile bool _running;

    bool _playing = true;
    bool _loop;
    bool _muted;
    float _volume = 1;
    double _lastSeekRequest = double.NaN;
    volatile bool _seekPending;
    long _seekToUs;
    long _fallbackBaseUs;
    bool _audioReady;
    bool _ready;
    double _lastReportedPosition = -1;

    public AndroidVideoSource(string source) => _source = source;

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("playing", out var playing) && playing is bool on) SetPlaying(on);
        if (props.TryGetValue("loop", out var loop) && loop is bool l) _loop = l;
        if (props.TryGetValue("muted", out var muted) && muted is bool m) { _muted = m; ApplyVolume(); }
        if (props.TryGetValue("volume", out var volume) && volume is double v) { _volume = (float)v; ApplyVolume(); }

        if (props.TryGetValue("seekTo", out var seek) && seek is double seconds && !Equals(seconds, _lastSeekRequest))
        {
            _lastSeekRequest = seconds;
            _seekToUs = (long)(seconds * 1_000_000);
            _seekPending = true;
            if (_audioReady) _audio?.SeekTo((int)(seconds * 1000));
        }
    }

    void SetPlaying(bool on)
    {
        _playing = on;
        if (_audioReady)
        {
            if (on) _audio?.Start();
            else _audio?.Pause();
        }
        if (on) _fallbackClock.Start();
        else
        {
            _fallbackBaseUs = FallbackPositionUs();
            _fallbackClock.Reset();
        }
    }

    void ApplyVolume()
    {
        var level = _muted ? 0 : Math.Clamp(_volume, 0, 1);
        try { _audio?.SetVolume(level, level); } catch (Java.Lang.Exception) { /* not prepared yet */ }
    }

    long FallbackPositionUs() => _fallbackBaseUs + _fallbackClock.ElapsedMilliseconds * 1000;

    /// <summary>Where playback is now: the audio track's clock when there is one, else our own.</summary>
    long PositionUs() => _audioReady && _audio is { } audio ? audio.CurrentPosition * 1000L : FallbackPositionUs();

    public override void Start()
    {
        try
        {
            _extractor = new MediaExtractor();
            _extractor.SetDataSource(_source);

            var track = -1;
            MediaFormat? format = null;
            for (var i = 0; i < _extractor.TrackCount; i++)
            {
                var candidate = _extractor.GetTrackFormat(i);
                if (candidate.GetString(MediaFormat.KeyMime)?.StartsWith("video/", StringComparison.Ordinal) != true) continue;
                track = i;
                format = candidate;
                break;
            }

            if (track < 0 || format is null)
            {
                Fail("The media has no video track.");
                return;
            }

            _extractor.SelectTrack(track);

            var mime = format.GetString(MediaFormat.KeyMime)!;
            _codec = MediaCodec.CreateDecoderByType(mime);
            // A null surface is what puts the codec in ByteBuffer mode, which is the only mode whose
            // output this process can actually read.
            _codec.Configure(format, surface: null, crypto: null, flags: MediaCodecConfigFlags.None);
            _codec.Start();

            StartAudio();

            var durationUs = format.ContainsKey(MediaFormat.KeyDuration) ? format.GetLong(MediaFormat.KeyDuration) : 0;
            var width = format.ContainsKey(MediaFormat.KeyWidth) ? format.GetInteger(MediaFormat.KeyWidth) : 0;
            var height = format.ContainsKey(MediaFormat.KeyHeight) ? format.GetInteger(MediaFormat.KeyHeight) : 0;
            _ready = true;
            Emit($"ready:{width},{height},{Fmt(durationUs / 1_000_000.0)}");

            if (_playing) _fallbackClock.Start();
            _running = true;
            _worker = new Thread(Decode) { IsBackground = true, Name = "swiftdotnet-video" };
            _worker.Start();
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The media could not be opened.");
        }
    }

    void StartAudio()
    {
        try
        {
            _audio = new MediaPlayer();
            _audio.SetDataSource(_source);
            _audio.Prepared += (_, _) =>
            {
                _audioReady = true;
                ApplyVolume();
                if (_playing) _audio?.Start();
            };
            _audio.Error += (_, _) => { _audioReady = false; };   // silent video beats no video
            _audio.PrepareAsync();
        }
        catch (Java.Lang.Exception)
        {
            _audio = null;   // no audio track, or the source only opens once: fall back to our own clock
        }
    }

    void Decode()
    {
        var info = new MediaCodec.BufferInfo();
        var inputDone = false;

        while (_running)
        {
            var codec = _codec;
            var extractor = _extractor;
            if (codec is null || extractor is null) return;

            try
            {
                if (_seekPending)
                {
                    _seekPending = false;
                    extractor.SeekTo(_seekToUs, MediaExtractorSeekTo.ClosestSync);
                    codec.Flush();
                    inputDone = false;
                    if (!_audioReady)
                    {
                        _fallbackBaseUs = _seekToUs;
                        _fallbackClock.Restart();
                        if (!_playing) _fallbackClock.Reset();
                    }
                }

                if (!inputDone)
                {
                    var index = codec.DequeueInputBuffer(2000);
                    if (index >= 0)
                    {
                        var buffer = codec.GetInputBuffer(index);
                        var size = buffer is null ? -1 : extractor.ReadSampleData(buffer, 0);
                        if (size < 0)
                        {
                            codec.QueueInputBuffer(index, 0, 0, 0, MediaCodecBufferFlags.EndOfStream);
                            inputDone = true;
                        }
                        else
                        {
                            codec.QueueInputBuffer(index, 0, size, extractor.SampleTime, 0);
                            extractor.Advance();
                        }
                    }
                }

                var output = codec.DequeueOutputBuffer(info, 2000);
                if (output < 0) continue;

                if ((info.Flags & MediaCodecBufferFlags.EndOfStream) != 0)
                {
                    codec.ReleaseOutputBuffer(output, render: false);
                    HandleEnd(ref inputDone);
                    continue;
                }

                // Hold the frame until its presentation time arrives, so playback runs at the movie's
                // rate (and with the audio) rather than as fast as the decoder can go.
                WaitFor(info.PresentationTimeUs);

                using (var image = codec.GetOutputImage(output))
                {
                    if (image is not null)
                    {
                        var pixels = _converter.Convert(image);
                        if (pixels is not null)
                            Publish(new PixelFrame(pixels, timestamp: TimeSpan.FromMilliseconds(info.PresentationTimeUs / 1000.0)));
                    }
                }
                codec.ReleaseOutputBuffer(output, render: false);

                var position = Math.Floor(info.PresentationTimeUs / 250_000.0) / 4;
                if (Math.Abs(position - _lastReportedPosition) > 0.001)
                {
                    _lastReportedPosition = position;
                    Emit($"position:{Fmt(position)}");
                }
            }
            catch (Java.Lang.Exception ex)
            {
                if (_running) Fail(ex.Message ?? "Playback failed.");
                return;
            }
        }
    }

    void WaitFor(long presentationTimeUs)
    {
        while (_running)
        {
            if (!_playing) { Thread.Sleep(30); continue; }
            var lead = presentationTimeUs - PositionUs();
            if (lead <= 0) return;
            Thread.Sleep((int)Math.Clamp(lead / 1000, 1, 100));
        }
    }

    void HandleEnd(ref bool inputDone)
    {
        if (!_loop)
        {
            Emit("ended:");
            _running = false;
            return;
        }

        _extractor?.SeekTo(0, MediaExtractorSeekTo.ClosestSync);
        _codec?.Flush();
        inputDone = false;
        _fallbackBaseUs = 0;
        _fallbackClock.Restart();
        if (_audioReady) { _audio?.SeekTo(0); if (_playing) _audio?.Start(); }
    }

    static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public override void Stop()
    {
        _running = false;
        _fallbackClock.Stop();
        try { if (_audioReady) _audio?.Pause(); } catch (Java.Lang.Exception) { /* already released */ }
    }

    public override void Dispose()
    {
        _running = false;
        _worker?.Join(500);
        _worker = null;

        try { _codec?.Stop(); } catch (Java.Lang.Exception) { /* never started */ }
        _codec?.Release();
        _codec = null;
        _extractor?.Release();
        _extractor = null;
        _audio?.Release();
        _audio = null;
        base.Dispose();
    }
}
