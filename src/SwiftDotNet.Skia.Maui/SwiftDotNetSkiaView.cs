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
    float _scale = 1;
    IDispatcherTimer? _timer;

    public SwiftDotNetSkiaView(View root)
    {
        EnableTouchEvents = true;
        _bridge.Invalidate += OnInvalidate;
        PaintSurface += OnPaintSurface;
        Touch += OnTouch;
        SwiftApp.Run(root, _bridge);
    }

    /// <summary>The bridge, exposed so a host can drive text input from a soft-keyboard / hidden entry.</summary>
    public SkiaBridge Bridge => _bridge;

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
            _timer.Tick += (_, _) => _bridge.Tick(0.016);
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
            case SKTouchAction.Pressed:
                _bridge.DispatchPointer(p);
                break;
            case SKTouchAction.Moved:
                if (e.InContact) _bridge.DispatchPointer(p); // drag → slider set
                break;
            case SKTouchAction.WheelChanged:
                _bridge.Scroll(p, -e.WheelDelta);
                break;
        }
        e.Handled = true;
    }
}
