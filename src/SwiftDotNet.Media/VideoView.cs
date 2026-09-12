using System.Globalization;

namespace SwiftDotNet.Controls;

/// <summary>How a video frame fills its frame. Mirrors <see cref="ImageContentMode"/>, plus a stretch.</summary>
public enum VideoContentMode
{
    /// <summary>Letterbox — the whole frame is visible, aspect preserved.</summary>
    Fit,

    /// <summary>Crop — the box is filled edge to edge, aspect preserved.</summary>
    Fill,

    /// <summary>Stretch to the box, ignoring aspect.</summary>
    Stretch,
}

/// <summary>What a <see cref="VideoView"/> reports once its stream has opened.</summary>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Duration">Stream duration, or <see cref="TimeSpan.Zero"/> for a live source.</param>
public readonly record struct VideoInfo(int Width, int Height, TimeSpan Duration)
{
    /// <summary>True for a source with no end — a camera, a live stream.</summary>
    public bool IsLive => Duration <= TimeSpan.Zero;
}

/// <summary>
/// An embedded video surface — a file, a URL, or an HLS/RTSP stream — playing inside the layout.
/// </summary>
/// <remarks>
/// <para>A <see cref="CustomView"/> like <c>Map</c> and <c>CameraView</c>: it emits a <c>"VideoView"</c>
/// node, and what renders it depends on the backend. On the <b>self-drawing</b> backends (Skia, WebGPU,
/// MonoGame, Godot, Unity) the engine's built-in surface renderer draws decoded frames straight onto the
/// canvas, fed by whichever <c>SwiftDotNet.Media.*</c> provider the app registered — see
/// <c>docs/video-and-camera.md</c>. On the widget-backed backends a native renderer can be registered
/// under the same node name. With neither, the node paints a labelled placeholder rather than an empty
/// box.</para>
///
/// <para>There are deliberately <b>no built-in transport controls</b>. Play/pause/scrub/volume are
/// ordinary state in your view, so the chrome is composed from the DSL (and therefore themed, localized
/// and testable) instead of being a native widget nobody can restyle.</para>
///
/// <code>
/// readonly State&lt;bool&gt; _playing = new(true);
///
/// new VStack(
///     new VideoView("https://example.com/clip.mp4")
///         .ContentMode(VideoContentMode.Fill)
///         .Loop()
///         .Playing(_playing)
///         .OnReady(i =&gt; _duration.Value = i.Duration)
///         .CornerRadius(12)
///         .Frame(height: 220),
///     new Button(_playing.Value ? "Pause" : "Play", () =&gt; _playing.Value = !_playing.Value))
/// </code>
/// </remarks>
public sealed class VideoView : CustomView
{
    readonly string _source;
    VideoContentMode _mode = VideoContentMode.Fit;
    State<bool>? _playing;
    bool _autoPlay = true;
    bool _loop;
    bool _muted;
    double _volume = 1;
    State<TimeSpan>? _seek;

    Action<VideoInfo>? _onReady;
    Action<TimeSpan>? _onPosition;
    Action? _onEnded;
    Action<string>? _onError;

    /// <param name="source">A URL (<c>https://</c>, <c>file://</c>, <c>rtsp://</c>, …) or a local path.</param>
    public VideoView(string source) => _source = source;

    protected override string TypeName => "VideoView";

    /// <summary>How the frame fills the space the layout gave this view. Defaults to <see cref="VideoContentMode.Fit"/>.</summary>
    public VideoView ContentMode(VideoContentMode mode) { _mode = mode; return this; }

    /// <summary>
    /// Two-way transport binding: set it to play or pause, and it is written back when the stream stops
    /// by itself (it ended, or it failed).
    /// </summary>
    public VideoView Playing(State<bool> playing) { _playing = playing; return this; }

    /// <summary>Start as soon as the stream opens. On by default; ignored when <see cref="Playing"/> is bound.</summary>
    public VideoView AutoPlay(bool autoPlay = true) { _autoPlay = autoPlay; return this; }

    /// <summary>Restart from the beginning on reaching the end.</summary>
    public VideoView Loop(bool loop = true) { _loop = loop; return this; }

    /// <summary>Mute the audio track (what an autoplaying background video wants).</summary>
    public VideoView Muted(bool muted = true) { _muted = muted; return this; }

    /// <summary>Audio volume, 0–1.</summary>
    public VideoView Volume(double volume) { _volume = Math.Clamp(volume, 0, 1); return this; }

    /// <summary>
    /// Bind a seek target: writing a new value to this state seeks there. Writing the <em>same</em> value
    /// again re-seeks, which is what a "restart" button needs.
    /// </summary>
    public VideoView Seek(State<TimeSpan> position) { _seek = position; return this; }

    /// <summary>Fires once the stream's dimensions and duration are known.</summary>
    public VideoView OnReady(Action<VideoInfo> handler) { _onReady = handler; return this; }

    /// <summary>Fires as playback advances (roughly per displayed frame; do not do heavy work here).</summary>
    public VideoView OnPosition(Action<TimeSpan> handler) { _onPosition = handler; return this; }

    /// <summary>Fires when the stream reaches its end (not raised when <see cref="Loop"/> is on).</summary>
    public VideoView OnEnded(Action handler) { _onEnded = handler; return this; }

    /// <summary>Fires with a message when the stream cannot be opened or decoded.</summary>
    public VideoView OnError(Action<string> handler) { _onError = handler; return this; }

    protected override void Configure(CustomNode node)
    {
        node.Prop("source", _source);
        node.Prop("contentMode", _mode switch
        {
            VideoContentMode.Fill => "fill",
            VideoContentMode.Stretch => "stretch",
            _ => "fit",
        });
        node.Prop("playing", _playing?.Value ?? _autoPlay);
        node.Prop("loop", _loop);
        node.Prop("muted", _muted);
        node.Prop("volume", _volume);

        // A seek is a *value*, not an edge: the source compares it against the position it last sought to,
        // so re-issuing the same target after the user scrubbed away still seeks. That keeps the prop
        // idempotent under the diff, which a "seek token" counter would not be across a tree rebuild.
        if (_seek is not null) node.Prop("seekTo", _seek.Value.TotalSeconds);

        node.OnEvent(Dispatch);
    }

    // Event value grammar (owned by VideoView; base64 for bodies that can contain arbitrary characters):
    //   "ready:<width>,<height>,<durationSeconds>"  → OnReady
    //   "position:<seconds>"                        → OnPosition
    //   "state:playing" | "state:paused"            → writes back to the Playing binding
    //   "ended:"                                    → OnEnded (+ Playing := false)
    //   "error:<base64>"                            → OnError (+ Playing := false)
    void Dispatch(string? value)
    {
        if (value is null) return;
        var colon = value.IndexOf(':');
        if (colon < 0) return;
        var kind = value[..colon];
        var body = value[(colon + 1)..];

        switch (kind)
        {
            case "ready":
                var parts = body.Split(',');
                if (parts.Length == 3 && int.TryParse(parts[0], out var w) && int.TryParse(parts[1], out var h))
                    _onReady?.Invoke(new VideoInfo(w, h, TimeSpan.FromSeconds(D(parts[2]) ?? 0)));
                break;
            case "position" when D(body) is { } seconds:
                _onPosition?.Invoke(TimeSpan.FromSeconds(seconds));
                break;
            case "state":
                if (_playing is not null && body is "playing" or "paused") _playing.Value = body == "playing";
                break;
            case "ended":
                if (_playing is not null) _playing.Value = false;
                _onEnded?.Invoke();
                break;
            case "error":
                if (_playing is not null) _playing.Value = false;
                if (_onError is not null && TryB64(body, out var bytes))
                    _onError(System.Text.Encoding.UTF8.GetString(bytes));
                break;
        }
    }

    static bool TryB64(string s, out byte[] bytes)
    {
        try { bytes = Convert.FromBase64String(s); return true; }
        catch { bytes = Array.Empty<byte>(); return false; }
    }

    static double? D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
