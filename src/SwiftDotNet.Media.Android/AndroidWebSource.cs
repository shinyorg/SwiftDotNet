using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Webkit;
using Java.Nio;
using SwiftDotNet.Graphics;
using AndroidWebView = Android.Webkit.WebView;

namespace SwiftDotNet;

/// <summary>
/// An off-screen <see cref="AndroidWebView"/> drawn into a bitmap and published as engine frames — what
/// makes <c>WebView</c> render on a self-drawing backend.
/// </summary>
/// <remarks>
/// <para>Android is the platform where this works properly. An <c>android.webkit.WebView</c> is an
/// ordinary <see cref="View"/>: measure it, lay it out, and <see cref="View.Draw(Canvas)"/> paints the
/// live page into any <see cref="Bitmap"/> you hand it — no snapshot IPC, no compositor round trip. And
/// because it is a real view, input is <b>real</b> too: taps go in as <see cref="MotionEvent"/>s and
/// keys as <see cref="KeyEvent"/>s, so links, forms, scrolling and text selection behave the way the
/// page expects rather than being approximated with script.</para>
///
/// <para>The view is never attached to a window, so nothing composites it to the screen twice; the
/// engine's canvas is the only thing that ever shows it.</para>
/// </remarks>
public sealed class AndroidWebSource : FrameSource, ISurfaceInput
{
    const int FramesPerSecond = 20;

    readonly Handler _main = new(Looper.MainLooper!);
    readonly string? _url;
    readonly string? _html;

    AndroidWebView? _web;
    Bitmap? _bitmap;
    Canvas? _canvas;
    byte[]? _buffer;
    Timer? _pump;
    int _width = 1024, _height = 768;
    bool _ready;

    public AndroidWebSource(string? url, string? html)
    {
        _url = url;
        _html = html;
    }

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        // url/html are fixed by the DSL at construction; a different address arrives as a new node.
    }

    public override void Start() => _main.Post(() =>
    {
        try
        {
            var web = new AndroidWebView(Android.App.Application.Context);
            web.Settings.JavaScriptEnabled = true;
            web.Settings.DomStorageEnabled = true;
            web.SetWebViewClient(new WebViewClient());       // keep navigation inside the view
            web.Layout(0, 0, _width, _height);
            _web = web;

            if (_url is { Length: > 0 } url) web.LoadUrl(url);
            else if (_html is { Length: > 0 } html) web.LoadData(html, "text/html; charset=utf-8", "utf-8");
            else { Fail("The WebView has neither a url nor html to load."); return; }

            var period = TimeSpan.FromSeconds(1.0 / FramesPerSecond);
            _pump = new Timer(_ => _main.Post(Capture), null, period, period);
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The web view could not be created.");
        }
    });

    void Capture()
    {
        var web = _web;
        if (web is null) return;

        try
        {
            if (_bitmap is null || _bitmap.Width != _width || _bitmap.Height != _height)
            {
                _bitmap?.Recycle();
                _bitmap = Bitmap.CreateBitmap(_width, _height, Bitmap.Config.Argb8888!);
                if (_bitmap is null) return;
                _canvas = new Canvas(_bitmap);
                _buffer = new byte[_width * _height * 4];
            }

            _bitmap.EraseColor(unchecked((int)0xFFFFFFFF));
            web.Draw(_canvas!);

            var buffer = ByteBuffer.Wrap(_buffer!);
            _bitmap.CopyPixelsToBuffer(buffer);

            if (!_ready)
            {
                _ready = true;
                Emit($"ready:{_width},{_height},0");
            }

            // ARGB_8888 is RGBA in memory on every Android device that matters, which is the engine's
            // own layout — so this is a copy, not a conversion.
            Publish(new PixelFrame(new Pixels(_width, _height, _buffer!)));
        }
        catch (Java.Lang.Exception ex)
        {
            Fail(ex.Message ?? "The page could not be drawn.");
        }
    }

    // ---- ISurfaceInput ------------------------------------------------------

    public void Pointer(float x, float y, GesturePhase phase) => _main.Post(() =>
    {
        var web = _web;
        if (web is null) return;
        var action = phase switch
        {
            GesturePhase.Began => MotionEventActions.Down,
            GesturePhase.Changed => MotionEventActions.Move,
            _ => MotionEventActions.Up,
        };
        var now = SystemClock.UptimeMillis();
        using var e = MotionEvent.Obtain(now, now, action, x * _width, y * _height, metaState: 0);
        web.DispatchTouchEvent(e);
    });

    public void Scroll(float x, float y, float deltaX, float deltaY) =>
        _main.Post(() => _web?.ScrollBy((int)deltaX, (int)deltaY));

    public void Text(string text) => _main.Post(() =>
    {
        var web = _web;
        if (web is null || text.Length == 0) return;
        // The multi-character KeyEvent constructor delivers a whole string as one event, which is what an
        // IME does — sending one synthetic keystroke per char would fight autocorrect and composition.
        using var typed = new KeyEvent(SystemClock.UptimeMillis(), text, deviceId: 0, flags: 0);
        web.DispatchKeyEvent(typed);
    });

    public void Key(string key, bool down) => _main.Post(() =>
    {
        var code = key switch
        {
            "Backspace" => Keycode.Del,
            "Enter" => Keycode.Enter,
            "ArrowUp" => Keycode.DpadUp,
            "ArrowDown" => Keycode.DpadDown,
            "ArrowLeft" => Keycode.DpadLeft,
            "ArrowRight" => Keycode.DpadRight,
            _ => Keycode.Unknown,
        };
        if (code == Keycode.Unknown) return;
        var now = SystemClock.UptimeMillis();
        using var e = new KeyEvent(now, now, down ? KeyEventActions.Down : KeyEventActions.Up, code, repeat: 0);
        _web?.DispatchKeyEvent(e);
    });

    public void Resize(float width, float height) => _main.Post(() =>
    {
        _width = Math.Max(1, (int)width);
        _height = Math.Max(1, (int)height);
        _web?.Layout(0, 0, _width, _height);
    });

    public override void Stop()
    {
        _pump?.Dispose();
        _pump = null;
    }

    public override void Dispose()
    {
        Stop();
        _main.Post(() =>
        {
            _web?.StopLoading();
            _web?.Destroy();
            _web = null;
            _canvas = null;
            _bitmap?.Recycle();
            _bitmap = null;
        });
        base.Dispose();
    }
}
