using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace SwiftDotNet;

/// <summary>
/// A .NET MAUI control that hosts a SwiftDotNet view tree via the SkiaSharp self-drawing backend. One
/// <see cref="SKCanvasView"/> covers iOS / Android / Mac Catalyst / Windows: the engine paints the whole
/// UI onto the canvas and touch events feed the bridge. Drop it in a page as the content:
/// <code>Content = new SwiftDotNetSkiaView(new ContentView());</code>
/// Because it's an ordinary MAUI view, it lives inside a MAUI app whose <c>MauiProgram</c> can
/// <c>.UseShiny(...)</c> — so the Skia UI and Shiny's plugins share the same DI container.
/// </summary>
public class SwiftDotNetSkiaView : SKCanvasView
{
    readonly SkiaBridge _bridge = new();
    readonly SkiaPointerRouter _pointer;
    float _scale = 1;
    double _clock;
    IDispatcherTimer? _timer;

    public SwiftDotNetSkiaView(View root) : this(root, null) { }

    /// <param name="services">
    /// The container `[Inject]` properties and `SwiftHost.Services` resolve from — pass a
    /// `SwiftDotNetApp.Services`, or the MAUI `IPlatformApplication.Current.Services` to share one
    /// container between the Skia UI and the rest of the app.
    /// </param>
    public SwiftDotNetSkiaView(View root, IServiceProvider? services)
    {
        _pointer = new SkiaPointerRouter(_bridge);
        EnableTouchEvents = true;
        _bridge.Invalidate += OnInvalidate;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
        SwiftApp.Run(root, _bridge, services);

        // MAUI has a real pinch recognizer, so .OnMagnify gets a true two-finger gesture. Its Scale is an
        // *incremental* factor per event (not cumulative), which is exactly what PinchDelta accumulates.
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            var p = new SKPoint((float)(e.ScaleOrigin.X * Width), (float)(e.ScaleOrigin.Y * Height));
            if (e.Status == GestureStatus.Running) _pointer.PinchDelta(p, (float)e.Scale);
            else if (e.Status is GestureStatus.Completed or GestureStatus.Canceled) _pointer.EndPinch(p);
        };
        GestureRecognizers.Add(pinch);
    }

    /// <summary>The bridge, exposed so a host can drive text input from a soft-keyboard / hidden entry.</summary>
    public SkiaBridge Bridge => _bridge;

    /// <summary>The gesture router, exposed so a host can tune tap slop / long-press timing.</summary>
    public SkiaPointerRouter Pointer => _pointer;

    void OnInvalidate()
    {
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(InvalidateSurface);
        else InvalidateSurface();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        // Drive the implicit-animation clock at ~60fps once we're attached; repaints only while animating.
        if (Handler is not null && _timer is null)
        {
            _timer = Dispatcher.CreateTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(16);
            // Doubles as the pointer router's clock — it needs one to resolve a hold into a long-press.
            _timer.Tick += (_, _) => { _clock += 0.016; _bridge.Tick(0.016); _pointer.Poll(_clock); };
            _timer.Start();
        }
    }

    void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var info = e.Info;
        var canvas = e.Surface.Canvas;
        _scale = Width > 0 ? (float)(info.Width / Width) : 1;   // device pixels ÷ DIPs
        canvas.Scale(_scale);
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _bridge.Paint(canvas, new SKSize((float)Width, (float)Height), dark);
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        var p = new SKPoint(e.Location.X / _scale, e.Location.Y / _scale); // pixels → DIPs (layout space)
        switch (e.ActionType)
        {
            // The router resolves the raw stream into tap / long-press / swipe / continuous drag; without
            // it .OnDrag and .OnMagnify never fire and the Controls sliders/panels are inert.
            case SKTouchAction.Pressed:
                _pointer.Down(p, _clock);
                break;
            case SKTouchAction.Moved:
                if (e.InContact) _pointer.Move(p, _clock);
                break;
            case SKTouchAction.Released:
                _pointer.Up(p, _clock);
                break;
            case SKTouchAction.Cancelled or SKTouchAction.Exited:
                _pointer.Cancel();
                break;
            case SKTouchAction.WheelChanged:
                _bridge.Scroll(p, -e.WheelDelta);
                break;
        }
        e.Handled = true;
    }
}
