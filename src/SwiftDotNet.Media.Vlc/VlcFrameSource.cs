using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using LibVLCSharp.Shared;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// Plays anything LibVLC can open — a file, http/HLS/DASH, RTSP, or a webcam — straight into engine
/// frames. The desktop frame producer for the self-drawing backends.
/// </summary>
/// <remarks>
/// <para>LibVLC has a first-class "render into my memory" mode (<c>libvlc_video_set_callbacks</c>), which
/// is exactly the shape this seam wants: it hands over a buffer, VLC writes decoded RGBA into it, and the
/// engine draws it. That one path covers Windows, macOS and Linux, files and network streams, and
/// capture devices — which is why a single provider can serve Silk.NET, WPF, WinForms, the headless
/// harness and a framebuffer Linux box that between them have no media stack in common.</para>
///
/// <para><b>The app supplies the native library.</b> This package references only the managed
/// LibVLCSharp; add <c>VideoLAN.LibVLC.Windows</c> or <c>VideoLAN.LibVLC.Mac</c> to the head project, or
/// install <c>libvlc</c> from the distro on Linux. Without it, <see cref="Start"/> reports the failure
/// through the node rather than throwing on a background thread.</para>
/// </remarks>
public sealed class VlcFrameSource : FrameSource
{
    /// <summary>RGBA, straight, 8 bits per channel — the chroma that needs no conversion on our side.</summary>
    const string Chroma = "RGBA";

    static readonly object InitLock = new();
    static LibVLC? _shared;
    static string? _initError;

    readonly string _mrl;
    readonly string[] _options;

    MediaPlayer? _player;
    Media? _media;

    // Pinned so LibVLC (which writes from its own decoder thread) has a stable address for the frame.
    GCHandle _pin;
    byte[]? _buffer;
    // Two publish buffers alternate: one is being drawn by the renderer on the UI thread while the next
    // frame is copied into the other. (VLC's own buffer stays put — it is pinned, and moving it would
    // mean re-pinning inside the display callback.)
    byte[]?[] _publish = new byte[]?[2];
    int _next;
    uint _width, _height, _pitch;
    bool _ready;
    bool _playing = true;
    bool _loop;
    bool _muted;
    int _volume = 100;
    double _lastSeekRequest = double.NaN;
    double _lastReportedPosition = -1;

    // Held as fields: these are passed to native code, and a delegate that only exists as an argument is
    // collected while VLC is still calling it — a crash that reproduces once an hour under load.
    MediaPlayer.LibVLCVideoFormatCb? _formatCb;
    MediaPlayer.LibVLCVideoCleanupCb? _cleanupCb;
    MediaPlayer.LibVLCVideoLockCb? _lockCb;
    MediaPlayer.LibVLCVideoUnlockCb? _unlockCb;
    MediaPlayer.LibVLCVideoDisplayCb? _displayCb;

    /// <param name="mrl">A VLC media locator: a path, a URL, or a capture device such as <c>v4l2:///dev/video0</c>.</param>
    /// <param name="options">Per-media VLC options (<c>":network-caching=300"</c>, …).</param>
    public VlcFrameSource(string mrl, params string[] options)
    {
        _mrl = mrl;
        _options = options;
    }

    /// <summary>
    /// The default capture device locator for this OS, for serving <c>CameraView</c> from LibVLC on a
    /// desktop that has no platform camera provider.
    /// </summary>
    public static string DefaultCameraMrl =>
        OperatingSystem.IsWindows() ? "dshow://"
        : OperatingSystem.IsMacOS() ? "avcapture://"
        : "v4l2:///dev/video0";

    /// <summary>The shared <see cref="LibVLC"/>, or null with <paramref name="error"/> set.</summary>
    static LibVLC? Shared(out string? error)
    {
        lock (InitLock)
        {
            error = _initError;
            if (_shared is not null || _initError is not null) return _shared;
            try
            {
                Core.Initialize();
                _shared = new LibVLC("--no-osd", "--quiet");
            }
            catch (Exception ex)
            {
                // Almost always "the native libvlc is missing" — say that, rather than letting a
                // DllNotFoundException surface as a blank box.
                _initError = $"LibVLC could not be initialized ({ex.Message}). Add VideoLAN.LibVLC.Windows / .Mac, or install libvlc.";
                error = _initError;
            }
            return _shared;
        }
    }

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        if (props.TryGetValue("playing", out var playing) && playing is bool on) SetPlaying(on);
        if (props.TryGetValue("loop", out var loop) && loop is bool l) _loop = l;
        if (props.TryGetValue("muted", out var muted) && muted is bool m) { _muted = m; ApplyVolume(); }
        if (props.TryGetValue("volume", out var volume) && volume is double v) { _volume = (int)(Math.Clamp(v, 0, 1) * 100); ApplyVolume(); }

        if (props.TryGetValue("seekTo", out var seek) && seek is double seconds && !Equals(seconds, _lastSeekRequest))
        {
            _lastSeekRequest = seconds;
            if (_player is { IsSeekable: true }) _player.Time = (long)(seconds * 1000);
        }
    }

    void SetPlaying(bool on)
    {
        _playing = on;
        if (_player is null) return;
        if (on) _player.Play();
        else _player.SetPause(true);
    }

    void ApplyVolume()
    {
        if (_player is null) return;
        _player.Mute = _muted;
        _player.Volume = _volume;
    }

    public override void Start()
    {
        var vlc = Shared(out var error);
        if (vlc is null)
        {
            Fail(error ?? "LibVLC is unavailable.");
            return;
        }

        try
        {
            _media = new Media(vlc, _mrl, _mrl.Contains("://", StringComparison.Ordinal) ? FromType.FromLocation : FromType.FromPath);
            foreach (var option in _options) _media.AddOption(option);

            _player = new MediaPlayer(_media);
            _formatCb = OnFormat;
            _cleanupCb = OnCleanup;
            _lockCb = OnLock;
            _unlockCb = OnUnlock;
            _displayCb = OnDisplay;
            _player.SetVideoFormatCallbacks(_formatCb, _cleanupCb);
            _player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);

            _player.EndReached += OnEndReached;
            _player.EncounteredError += (_, _) => Fail("LibVLC could not play this media.");

            ApplyVolume();
            if (_playing) _player.Play();
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    }

    // ---- LibVLC video callbacks (called on VLC's own threads) ---------------

    uint OnFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        // Tell VLC the chroma we want by overwriting the 4-byte FourCC it passes in.
        var fourcc = Encoding.ASCII.GetBytes(Chroma);
        Marshal.Copy(fourcc, 0, chroma, 4);

        _width = width;
        _height = height;
        _pitch = width * 4;
        pitches = _pitch;
        lines = height;

        Free();
        _buffer = new byte[_pitch * height];
        _publish = [new byte[_pitch * height], new byte[_pitch * height]];
        _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);

        if (!_ready)
        {
            _ready = true;
            var duration = _player?.Length ?? 0;
            Emit($"ready:{width},{height},{Fmt(duration / 1000.0)}");
        }

        return 1;   // one plane
    }

    void OnCleanup(ref IntPtr opaque) => Free();

    IntPtr OnLock(IntPtr opaque, IntPtr planes)
    {
        if (!_pin.IsAllocated) return IntPtr.Zero;
        var address = _pin.AddrOfPinnedObject();
        Marshal.WriteIntPtr(planes, 0, address);
        return address;
    }

    void OnUnlock(IntPtr opaque, IntPtr picture, IntPtr planes) { }

    void OnDisplay(IntPtr opaque, IntPtr picture)
    {
        var source = _buffer;
        var target = _publish[_next];
        if (source is null || target is null || _width == 0) return;
        _next ^= 1;

        // Copy out of the buffer VLC owns before publishing: VLC starts writing the next frame the
        // moment this returns, and the renderer may still be drawing this one.
        Buffer.BlockCopy(source, 0, target, 0, source.Length);
        Publish(new PixelFrame(new Pixels((int)_width, (int)_height, target)));

        var seconds = (_player?.Time ?? 0) / 1000.0;
        var position = Math.Floor(seconds * 4) / 4;
        if (Math.Abs(position - _lastReportedPosition) > 0.001)
        {
            _lastReportedPosition = position;
            Emit($"position:{Fmt(position)}");
        }
    }

    void OnEndReached(object? sender, EventArgs e)
    {
        if (_loop)
        {
            // A player that reached the end cannot be restarted from its event handler — VLC documents
            // this — so the restart is bounced onto the pool.
            var player = _player;
            Task.Run(() => { player?.Stop(); player?.Play(); });
            return;
        }
        Emit("ended:");
    }

    static string Fmt(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    void Free()
    {
        if (_pin.IsAllocated) _pin.Free();
        _buffer = null;
        _publish = new byte[]?[2];
    }

    public override void Stop() => _player?.SetPause(true);

    public override void Dispose()
    {
        if (_player is not null)
        {
            _player.EndReached -= OnEndReached;
            _player.Stop();
            _player.Dispose();
            _player = null;
        }
        _media?.Dispose();
        _media = null;
        Free();
        base.Dispose();
    }
}
