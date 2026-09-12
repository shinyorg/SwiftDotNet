#if !TVOS
using CoreGraphics;
using Foundation;
using SwiftDotNet.Graphics;
using WebKit;

namespace SwiftDotNet;

/// <summary>
/// An off-screen <see cref="WKWebView"/> snapshotted into engine frames — what makes <c>WebView</c> draw
/// on a self-drawing backend instead of painting its "not drawable on a canvas" card.
/// </summary>
/// <remarks>
/// <para><b>Read the trade-off before reaching for this.</b> WebKit renders out of process, so the only
/// supported way to get its pixels is <see cref="WKWebView.TakeSnapshot(WKSnapshotConfiguration, Action{UIKit.UIImage, NSError})"/>,
/// which costs an IPC round trip and a full recomposite — tens of milliseconds. This snapshots at a
/// modest rate, which is fine for a page you read (docs, a receipt, an OAuth form) and visibly poor for
/// one you fling around. Inside a MAUI host, floating a real web view over the canvas through
/// <c>IPlatformViewHost</c> stays the better choice; this is for the hosts that have no view hierarchy to
/// float anything in — Silk, MonoGame, Godot, a framebuffer.</para>
///
/// <para>Input is replayed rather than delivered: the off-screen view never sees a real touch, so taps
/// and scrolls arrive as script against the document. That covers links, buttons and scrolling; it does
/// not pretend to cover text selection or native gestures.</para>
/// </remarks>
public sealed class AppleWebSource : FrameSource, ISurfaceInput
{
    const double SnapshotIntervalSeconds = 0.2;

    readonly string? _url;
    readonly string? _html;

    WKWebView? _web;
    Timer? _pump;
    byte[]? _buffer;
    int _width = 1024, _height = 768;
    bool _snapshotting;
    bool _ready;

    public AppleWebSource(string? url, string? html)
    {
        _url = url;
        _html = html;
    }

    public override void Configure(IReadOnlyDictionary<string, object?> props)
    {
        // The node's url/html are fixed at construction by the DSL; a changed url arrives as a new node.
    }

    public override void Start() => Dispatch(() =>
    {
        try
        {
            _web = new WKWebView(new CGRect(0, 0, _width, _height), new WKWebViewConfiguration());
            if (_url is { Length: > 0 } url && NSUrl.FromString(url) is { } nsUrl)
                _web.LoadRequest(new NSUrlRequest(nsUrl));
            else if (_html is { Length: > 0 } html)
                _web.LoadHtmlString(new NSString(html), baseUrl: null);
            else
            {
                Fail("The WebView has neither a url nor html to load.");
                return;
            }

            _pump = new Timer(_ => Dispatch(Snapshot), null,
                TimeSpan.FromSeconds(SnapshotIntervalSeconds), TimeSpan.FromSeconds(SnapshotIntervalSeconds));
        }
        catch (Exception ex)
        {
            Fail(ex.Message);
        }
    });

    void Snapshot()
    {
        if (_web is null || _snapshotting) return;     // snapshots must not pile up on a slow page
        _snapshotting = true;
        _web.TakeSnapshot(new WKSnapshotConfiguration(), (image, error) =>
        {
            _snapshotting = false;
            if (error is not null || image is null) return;
            var pixels = Rasterize(image);
            if (pixels is null) return;
            if (!_ready)
            {
                _ready = true;
                Emit($"ready:{pixels.Width},{pixels.Height},0");
            }
            Publish(new PixelFrame(pixels));
        });
    }

    /// <summary>Draw the snapshot into a straight-RGBA bitmap context — the one format the engine takes.</summary>
    unsafe Pixels? Rasterize(
#if IOS || MACCATALYST
        UIKit.UIImage image
#else
        AppKit.NSImage image
#endif
    )
    {
#if IOS || MACCATALYST
        var cgImage = image.CGImage;
#else
        var rect = new CGRect(CGPoint.Empty, image.Size);
        var cgImage = image.AsCGImage(ref rect, null, null);
#endif
        if (cgImage is null) return null;

        var width = (int)cgImage.Width;
        var height = (int)cgImage.Height;
        if (width <= 0 || height <= 0) return null;

        var length = width * height * 4;
        if (_buffer is null || _buffer.Length != length) _buffer = new byte[length];

        fixed (byte* data = _buffer)
        {
            using var colorSpace = CGColorSpace.CreateDeviceRGB();
            using var context = new CGBitmapContext((nint)data, width, height, 8, width * 4, colorSpace,
                CGImageAlphaInfo.PremultipliedLast);
            context.DrawImage(new CGRect(0, 0, width, height), cgImage);
        }

        return new Pixels(width, height, _buffer);
    }

    // ---- ISurfaceInput ------------------------------------------------------

    public void Pointer(float x, float y, GesturePhase phase)
    {
        if (phase != GesturePhase.Ended) return;     // a replayed click is a single event, not a stream
        Script($"(function(){{var e=document.elementFromPoint({x:F3}*window.innerWidth,{y:F3}*window.innerHeight);" +
               "if(e){e.focus&&e.focus();e.click&&e.click();}})()");
    }

    public void Scroll(float x, float y, float deltaX, float deltaY) =>
        Script($"window.scrollBy({(-deltaX).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}," +
               $"{(-deltaY).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)})");

    public void Text(string text) =>
        Script("(function(){var e=document.activeElement;if(e&&'value' in e){e.value+=" +
               JsString(text) + ";e.dispatchEvent(new Event('input',{bubbles:true}));}})()");

    public void Key(string key, bool down)
    {
        if (!down) return;
        if (key == "Backspace")
            Script("(function(){var e=document.activeElement;if(e&&'value' in e){e.value=e.value.slice(0,-1);" +
                   "e.dispatchEvent(new Event('input',{bubbles:true}));}})()");
        else if (key == "Enter")
            Script("(function(){var e=document.activeElement;if(e&&e.form)e.form.submit();})()");
    }

    public void Resize(float width, float height) => Dispatch(() =>
    {
        _width = Math.Max(1, (int)width);
        _height = Math.Max(1, (int)height);
        if (_web is not null) _web.Frame = new CGRect(0, 0, _width, _height);
    });

    static string JsString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    void Script(string javascript) => Dispatch(() => _web?.EvaluateJavaScript(new NSString(javascript), (_, _) => { }));

    /// <summary>WebKit is main-thread-only; the engine's timers and input are not guaranteed to be.</summary>
    static void Dispatch(Action action)
    {
        if (NSThread.IsMain) action();
        else NSRunLoop.Main.BeginInvokeOnMainThread(action);
    }

    public override void Stop()
    {
        _pump?.Dispose();
        _pump = null;
    }

    public override void Dispose()
    {
        Stop();
        Dispatch(() =>
        {
            _web?.StopLoading();
            _web?.Dispose();
            _web = null;
        });
        base.Dispose();
    }
}
#endif
