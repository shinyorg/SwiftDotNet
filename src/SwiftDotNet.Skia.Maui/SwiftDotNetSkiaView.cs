using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
// This file lives in the SwiftDotNet namespace, where `Grid` is the *DSL* Grid — alias MAUI's.
using MauiGrid = Microsoft.Maui.Controls.Grid;

namespace SwiftDotNet;

/// <summary>
/// A .NET MAUI control that hosts a SwiftDotNet view tree via the SkiaSharp self-drawing backend. One
/// canvas covers iOS / Android / Mac Catalyst / Windows: the engine paints the whole UI onto it and touch
/// events feed the bridge. Drop it in a page as the content:
/// <code>Content = new SwiftDotNetSkiaView(new ContentView());</code>
/// Because it's an ordinary MAUI view, it lives inside a MAUI app whose <c>MauiProgram</c> can
/// <c>.UseShiny(...)</c> — so the Skia UI and Shiny's plugins share the same DI container.
///
/// It wraps the canvas in a layout rather than *being* a bare <c>SKCanvasView</c> because a self-drawn canvas cannot
/// raise a soft keyboard: the platform only offers one to a focused native text input. So the grid holds
/// the canvas plus a 1×1 transparent <see cref="Entry"/> that is focused whenever the engine focuses a
/// text control, and whose text is mirrored into the engine. See <see cref="AttachSoftKeyboard"/>.
/// </summary>
public class SwiftDotNetSkiaView : Microsoft.Maui.Controls.ContentView
{
    readonly SkiaBridge _bridge = new();
    readonly SkiaPointerRouter _pointer;
    readonly SKCanvasView _canvas = new();
    readonly Entry _input = new();
    readonly MauiGrid _layout = new();
    readonly Microsoft.Maui.Controls.AbsoluteLayout _platformViews = MauiPlatformViewLayer.CreatePanel();
    readonly MauiPlatformViewLayer _platformViewLayer;
    float _scale = 1;
    double _clock;
    bool _syncingInput;         // suppress the TextChanged that our own Text assignment raises
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
        _platformViewLayer = new MauiPlatformViewLayer(_platformViews, _bridge);
        _canvas.EnableTouchEvents = true;
        _bridge.Invalidate += OnInvalidate;
        _canvas.PaintSurface += OnPaintSurface;
        _canvas.Touch += OnTouch;

        // Both children share the single implicit cell; the entry is sized to nothing and pinned to the
        // top-left so it can never affect the canvas's layout or intercept a touch.
        _layout.Children.Add(_canvas);
        AttachSoftKeyboard();
        AttachPlatformViews();
        Content = _layout;

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
        _canvas.GestureRecognizers.Add(pinch);
    }

    /// <summary>The bridge, exposed so a host can drive text input from a soft-keyboard / hidden entry.</summary>
    public SkiaBridge Bridge => _bridge;

    /// <summary>The gesture router, exposed so a host can tune tap slop / long-press timing.</summary>
    public SkiaPointerRouter Pointer => _pointer;

    /// <summary>The canvas the engine paints into, for hosts that need to reach the SkiaSharp view itself.</summary>
    public SKCanvasView Canvas => _canvas;

    /// <summary>The platform-view layer, exposed so a host can inspect or extend what it places.</summary>
    public MauiPlatformViewLayer PlatformViews => _platformViewLayer;

    /// <summary>
    /// Let the tree hold real MAUI controls the canvas cannot draw — an embedded
    /// <see cref="MauiView"/>, and <c>WebView</c>, which until now painted a "not drawable on a canvas"
    /// placeholder on every self-drawing backend.
    ///
    /// The whole mechanism is one more child in the same grid: the engine reports where each such node
    /// landed and this layer positions a MAUI view there. It goes in <em>last</em> so the real controls sit
    /// above both the canvas and the shadow entry, which is also the constraint behind their one visible
    /// compromise — a native view always floats above canvas pixels, so the engine hides every platform
    /// view while a Sheet / Alert / Menu is presented rather than letting one punch through the overlay.
    /// </summary>
    void AttachPlatformViews()
    {
        _layout.Children.Add(_platformViews);
        _bridge.PlatformViewHost = _platformViewLayer;
        _bridge.FocusChanged += _platformViewLayer.OnEngineFocused;
    }

    /// <summary>
    /// Bridge the engine's focus model to the platform IME. The engine owns *what* is focused (tapping a
    /// TextField sets <see cref="SkiaBridge.FocusedId"/>); this makes the OS agree, by focusing an
    /// invisible native <see cref="Entry"/> so the keyboard appears, and by mirroring text both ways.
    ///
    /// It has to be the whole string in both directions, not keystrokes: a system keyboard reports the
    /// *resulting* text, which is the only thing that survives autocorrect, dictation, paste and
    /// selection edits.
    /// </summary>
    void AttachSoftKeyboard()
    {
        // Deliberately NOT InputTransparent: on iOS that maps to UserInteractionEnabled=false, and a view
        // that cannot be interacted with cannot become first responder — Focus() just returns false and no
        // keyboard ever appears. (Android is laxer and allows programmatic focus either way.) It is 1×1 in
        // the top-left corner instead, so the canvas still owns every pixel that matters.
        _input.WidthRequest = 1;
        _input.HeightRequest = 1;
        _input.Opacity = 0;                  // must stay IsVisible=true — an invisible view cannot take focus
        _input.HorizontalOptions = LayoutOptions.Start;
        _input.VerticalOptions = LayoutOptions.Start;
        _layout.Children.Add(_input);

        _bridge.FocusChanged += id =>
        {
            _syncingInput = true;
            _input.IsPassword = _bridge.FocusedIsSecure;
            _input.ReturnType = _bridge.FocusedIsMultiline ? ReturnType.Default : ReturnType.Done;
            _input.Text = _bridge.FocusedText ?? "";
            _syncingInput = false;

            if (id is null) _input.Unfocus();
            else _input.Focus();
        };

        // The engine may also change the text under us (a binding wrote to the State) — keep the shadow
        // entry in step so the next keystroke doesn't resurrect a stale value.
        _input.TextChanged += (_, e) =>
        {
            if (_syncingInput) return;
            _bridge.SetFocusedText(e.NewTextValue ?? "");
        };

        // "Done"/return closes the keyboard and drops the engine's focus, so the caret stops blinking.
        _input.Completed += (_, _) => _bridge.ClearFocus();
        _input.Unfocused += (_, _) => { if (_bridge.FocusedId is not null) _bridge.ClearFocus(); };
    }

    void OnInvalidate()
    {
        if (Dispatcher.IsDispatchRequired) Dispatcher.Dispatch(_canvas.InvalidateSurface);
        else _canvas.InvalidateSurface();

        // A patch can change the focused control's text (the binding round-trip); mirror it so the
        // platform entry and the engine never disagree about what is in the field.
        if (_bridge.FocusedText is { } text && _input.Text != text)
        {
            _syncingInput = true;
            _input.Text = text;
            _syncingInput = false;
        }
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
        var w = _canvas.Width;
        var h = _canvas.Height;
        _scale = w > 0 ? (float)(info.Width / w) : 1;   // device pixels ÷ DIPs
        canvas.Scale(_scale);
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        _bridge.Paint(canvas, new SKSize((float)w, (float)h), dark);
    }

    void OnTouch(object? sender, SKTouchEventArgs e)
    {
        var p = new SKPoint(e.Location.X / _scale, e.Location.Y / _scale); // pixels → DIPs (layout space)
        switch (e.ActionType)
        {
            // The router resolves the raw stream into tap / long-press / swipe / continuous drag / slider
            // scrub / scroll pan; without it .OnDrag never fires, sliders are inert, and a touch host has
            // no way at all to scroll (there is no wheel).
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
