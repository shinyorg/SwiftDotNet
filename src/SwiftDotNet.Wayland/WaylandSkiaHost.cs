using SkiaSharp;
using Wayland.Platform;
using Wayland.Platform.Desktop;
using Wayland.Platform.Input;
using Wayland.Platform.Shell;

namespace SwiftDotNet;

/// <summary>How the Wayland host should present a SwiftDotNet app.</summary>
public sealed class WaylandHostOptions
{
    /// <summary>Window title.</summary>
    public string Title { get; set; } = "SwiftDotNet";

    /// <summary>
    /// Reverse-DNS app id. Must match the basename of the installed <c>.desktop</c> file, or the desktop
    /// will not associate the window with the app's icon.
    /// </summary>
    public string AppId { get; set; } = "net.swiftdotnet.app";

    /// <summary>Initial client width in logical pixels.</summary>
    public int Width { get; set; } = 440;

    /// <summary>Initial client height in logical pixels.</summary>
    public int Height { get; set; } = 820;

    /// <summary>Follow the desktop's light/dark preference. Set false to pin <see cref="Dark"/>.</summary>
    public bool FollowSystemTheme { get; set; } = true;

    /// <summary>Dark appearance, when <see cref="FollowSystemTheme"/> is false.</summary>
    public bool Dark { get; set; }
}

/// <summary>
/// Runs a SwiftDotNet <c>Skia</c> scene in a real Wayland window — no GTK, no Silk.NET, no GLFW.
/// </summary>
/// <remarks>
/// <para>
/// This is the thinnest of the backend adapters, and deliberately so. <c>SwiftDotNet.Graphics</c> already
/// owns layout, hit-testing, gestures, focus and the animation clock; all a windowing host has to supply is
/// a canvas, a size, a scale and an input stream. Everything Wayland-specific — the configure handshake,
/// client-side decorations, the shm swapchain, xkb text input, fractional scaling — lives in
/// <c>Wayland.Platform</c> and is shared with the MAUI backend.
/// </para>
/// <para>
/// The buffer handed over each frame is premultiplied BGRA, which is exactly
/// <c>SKColorType.Bgra8888</c> + <c>SKAlphaType.Premul</c>, so Skia draws straight into the compositor's
/// shared memory with no intermediate surface and no format conversion.
/// </para>
/// <para>
/// <b>Status:</b> compiles and is wired end to end, but has not been run against a live compositor from this
/// machine — see <c>docs/backends/wayland.md</c>.
/// </para>
/// </remarks>
public sealed class WaylandSkiaHost : IDisposable
{
    readonly WaylandApplication _app;
    readonly WaylandWindow _window;
    readonly WaylandHostOptions _options;
    readonly SkiaBridge _bridge;
    readonly SkiaPointerRouter _pointer;

    SKTypeface? _titleTypeface;
    double _clock;
    long _lastFrameTicks;
    bool _dark;
    bool _disposed;

    WaylandSkiaHost(WaylandApplication app, WaylandWindow window, WaylandHostOptions options, SkiaBridge bridge)
    {
        _app = app;
        _window = window;
        _options = options;
        _bridge = bridge;
        _pointer = new SkiaPointerRouter(bridge);

        _dark = options.FollowSystemTheme ? app.Settings.IsDark : options.Dark;
        app.Settings.ColorSchemeChanged += _ =>
        {
            if (!options.FollowSystemTheme) return;
            _dark = app.Settings.IsDark;
            window.Invalidate();
        };

        window.Render += OnRender;
        window.Resized += (_, _) => window.Invalidate();
        window.ScaleChanged += _ => window.Invalidate();
        window.StateChanged += _ => window.Invalidate();

        WireInput(app.Input);
        _lastFrameTicks = Environment.TickCount64;
    }

    /// <summary>The bridge the scene is rendered through, for tests and tooling.</summary>
    public SkiaBridge Bridge => _bridge;

    /// <summary>The window, for title changes and maximize/minimize.</summary>
    public WaylandWindow Window => _window;

    /// <summary>True when painting in dark appearance.</summary>
    public bool Dark => _dark;

    /// <summary>
    /// Creates a window, starts the app and runs the event loop until the window closes.
    /// </summary>
    /// <param name="createRoot">Builds the root view — normally <c>swiftApp.CreateRoot()</c>.</param>
    /// <param name="services">The app's service provider, or null.</param>
    /// <param name="options">Window options.</param>
    public static void Run(Func<View> createRoot, IServiceProvider? services = null, WaylandHostOptions? options = null)
    {
        options ??= new WaylandHostOptions();

        using var app = WaylandApplication.Create();
        var window = app.CreateWindow(new WaylandWindowOptions
        {
            Title = options.Title,
            AppId = options.AppId,
            Width = options.Width,
            Height = options.Height,
            MinWidth = 240,
            MinHeight = 320,
        });

        var bridge = new SkiaBridge();
        using var host = new WaylandSkiaHost(app, window, options, bridge);

        // SwiftApp.Run captures the ambient SynchronizationContext, so the loop's context must be installed
        // first — otherwise a State mutation from a timer or the hot-reload agent would rebuild the tree on
        // a pool thread while the UI thread is mid-paint. app.Run() installs its own equivalent context
        // moments later; both funnel into the same queue.
        SynchronizationContext.SetSynchronizationContext(new WaylandPostContext(app));

        SwiftApp.Run(createRoot(), bridge, services);
        app.Run();
    }

    void WireInput(WaylandInput? input)
    {
        if (input is null) return;

        input.PointerMoved += e =>
        {
            _pointer.Move(new SKPoint((float)e.ClientX, (float)e.ClientY), _clock);
            // An I-beam over text is the toolkit's call; the frame still overrides it on resize edges.
            input.SetContentCursor(Wayland.Client.Protocol.WpCursorShape.Default);
            _window.Invalidate();
        };

        input.PointerPressed += e =>
        {
            if (e.Button != PointerButton.Left) return;
            _pointer.Down(new SKPoint((float)e.ClientX, (float)e.ClientY), _clock);
            _window.Invalidate();
        };

        input.PointerReleased += e =>
        {
            if (e.Button != PointerButton.Left) return;
            _pointer.Up(new SKPoint((float)e.ClientX, (float)e.ClientY), _clock);
            _window.Invalidate();
        };

        input.Scrolled += e =>
        {
            var at = new SKPoint((float)e.ClientX, (float)e.ClientY);
            if (e.Modifiers.HasFlag(WaylandModifiers.Control))
            {
                // Ctrl+wheel is the desktop zoom convention, and the closest thing a mouse has to a pinch.
                _pointer.PinchDelta(at, 1 + (float)e.DeltaY * 0.002f);
            }
            else
            {
                _pointer.EndPinch(at);
                _bridge.Scroll(at, -(float)e.DeltaY);
            }
            _window.Invalidate();
        };

        input.TextInput += text =>
        {
            _bridge.InsertText(text);
            _window.Invalidate();
        };

        input.KeyPressed += e =>
        {
            switch (e.Key)
            {
                case WaylandKey.Backspace:
                    _bridge.DeleteBackward();
                    _window.Invalidate();
                    break;
                case WaylandKey.Escape:
                    // Left to the app: SwiftDotNet's navigation stack handles back itself.
                    break;
            }
        };
    }

    void OnRender(WaylandFrameContext ctx)
    {
        var now = Environment.TickCount64;
        var dt = Math.Clamp((now - _lastFrameTicks) / 1000.0, 0, 0.25);
        _lastFrameTicks = now;
        _clock += dt;

        // Advance implicit animations, and keep asking for frames while any are still running.
        var animating = _bridge.Tick(dt);
        _pointer.Poll(_clock);

        var palette = _dark ? DecorationPalette.Dark : DecorationPalette.Light;
        var clientBackground = _dark ? 0xFF1C1C1EU : 0xFFFFFFFFU;

        // Raw pixel writes for the frame first: this clears the buffer, paints the shadow, the rounded
        // window, the titlebar and the caption buttons. Skia then draws on top of it.
        SolidDecorationPainter.Paint(ctx, palette, clientBackground);

        var info = new SKImageInfo(ctx.Buffer.Width, ctx.Buffer.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, ctx.Buffer.Data, ctx.Buffer.Stride);
        if (surface is null) return;

        var canvas = surface.Canvas;
        canvas.Save();
        canvas.Scale((float)ctx.Scale);   // everything below is in logical pixels

        DrawTitle(canvas, ctx, palette);

        // Clip and translate so the scene is painted in client coordinates, matching what input delivers.
        var (ox, oy) = ctx.ClientOrigin;
        canvas.ClipRect(SKRect.Create(ox, oy, ctx.ClientWidth, ctx.ClientHeight));
        canvas.Translate(ox, oy);
        _bridge.Paint(canvas, new SKSize(ctx.ClientWidth, ctx.ClientHeight), _dark);

        canvas.Restore();
        canvas.Flush();

        if (animating)
            _window.Invalidate();
    }

    void DrawTitle(SKCanvas canvas, WaylandFrameContext ctx, DecorationPalette palette)
    {
        if (!ctx.Frame.ClientSide || string.IsNullOrEmpty(_options.Title)) return;

        // SolidDecorationPainter deliberately draws no text — it has no font stack. We do.
        _titleTypeface ??= SKFontManager.Default.MatchFamily("Cantarell")
                           ?? SKFontManager.Default.MatchFamily("Inter")
                           ?? SKTypeface.Default;

        using var font = new SKFont(_titleTypeface, 13.5f) { Subpixel = true, Edging = SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint
        {
            Color = _dark ? new SKColor(0xFF, 0xFF, 0xFF, ctx.State.Activated ? (byte)0xE0 : (byte)0x80)
                          : new SKColor(0x00, 0x00, 0x00, ctx.State.Activated ? (byte)0xC8 : (byte)0x70),
            IsAntialias = true,
        };

        var width = font.MeasureText(_options.Title);
        var metrics = font.Metrics;
        var x = (ctx.SurfaceWidth - width) / 2f;
        var y = ctx.Frame.Margin + (WindowFrame.TitleBarHeight - (metrics.Descent - metrics.Ascent)) / 2f - metrics.Ascent;

        // Never let a long title run under the caption buttons.
        var buttonsLeft = ctx.SurfaceWidth - ctx.Frame.Margin - WindowFrame.ButtonInset
                          - WindowFrame.ButtonSize * 3 - WindowFrame.ButtonGap * 2 - 12;
        canvas.Save();
        canvas.ClipRect(SKRect.Create(ctx.Frame.Margin, ctx.Frame.Margin,
            Math.Max(0, buttonsLeft - ctx.Frame.Margin), WindowFrame.TitleBarHeight));
        canvas.DrawText(_options.Title, Math.Max(ctx.Frame.Margin + 12, x), y, font, paint);
        canvas.Restore();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _titleTypeface?.Dispose();
    }

    /// <summary>Bridges <see cref="SynchronizationContext"/> onto the Wayland event loop.</summary>
    sealed class WaylandPostContext(WaylandApplication app) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => app.Post(() => d(state));
        public override void Send(SendOrPostCallback d, object? state) => app.Post(() => d(state));
    }
}
