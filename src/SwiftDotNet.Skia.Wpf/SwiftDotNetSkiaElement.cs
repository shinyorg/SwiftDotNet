using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SkiaSharp;
// This file lives in the SwiftDotNet namespace, where `Color`, `Brush`, `Image` and `Rectangle` are the
// *DSL* types — a simple name binds to the enclosing namespace's member before any using-imported one,
// so the WPF types are reached through distinctly-named aliases (a same-name alias is itself CS0576).
using WpfBrushes = System.Windows.Media.Brushes;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace SwiftDotNet;

/// <summary>
/// A WPF element that hosts a SwiftDotNet view tree via the SkiaSharp self-drawing backend. The engine
/// paints the whole UI onto one surface and WPF input feeds the bridge, so every DSL feature — transforms,
/// gradients, shadows, keyframes, gestures — works exactly as it does on the other Skia heads, with no
/// per-control WPF mapping. Drop it in a window as the content:
/// <code>window.Content = new SwiftDotNetSkiaElement(new ContentView());</code>
/// For a real WPF <em>control</em> tree instead (Win32 look, UI Automation accessibility), use the native
/// WPF backend in <c>SwiftDotNet.Wpf</c>.
/// </summary>
/// <remarks>
/// The surface is a <see cref="WriteableBitmap"/> whose back buffer an <see cref="SKSurface"/> draws
/// straight into, blitted in <see cref="OnRender"/>. That is the same thing SkiaSharp's own
/// <c>SKElement</c> does, done here directly so the project can stay on a plain net10.0 SkiaSharp
/// reference instead of the .NET-Framework-only <c>SkiaSharp.Views.WPF</c> package.
/// </remarks>
public class SwiftDotNetSkiaElement : FrameworkElement
{
    readonly SkiaBridge _bridge = new();
    readonly SkiaPointerRouter _pointer;
    readonly DispatcherTimer _timer;
    WriteableBitmap? _bitmap;
    int _pixelWidth, _pixelHeight;
    double _clock;

    public SwiftDotNetSkiaElement(View root) : this(root, null) { }

    /// <param name="services">
    /// The container <c>[Inject]</c> properties and <c>SwiftHost.Services</c> resolve from — pass a
    /// <c>SwiftDotNetApp.Services</c> to share one container with the rest of the app.
    /// </param>
    public SwiftDotNetSkiaElement(View root, IServiceProvider? services)
    {
        _pointer = new SkiaPointerRouter(_bridge);

        // A self-drawn surface has no focusable child, so the element itself must take focus for
        // OnTextInput/OnKeyDown to ever fire. FocusVisualStyle is cleared because WPF would otherwise
        // paint its dotted focus rectangle over the canvas.
        Focusable = true;
        FocusVisualStyle = null;

        _bridge.Invalidate += OnInvalidate;

        // WPF installs a DispatcherSynchronizationContext on the UI thread, so SwiftApp.Run captures a
        // real one here and off-thread State mutations marshal back correctly.
        SwiftApp.Run(root, _bridge, services);

        // Drives the implicit-animation clock, and doubles as the pointer router's clock — it needs one
        // to resolve a held press into a long-press.
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            _clock += 0.016;
            var animating = _bridge.Tick(0.016);
            _pointer.Poll(_clock);
            if (animating) InvalidateVisual();
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    /// <summary>The bridge, exposed so a host can drive text input or inspect laid-out frames.</summary>
    public SkiaBridge Bridge => _bridge;

    /// <summary>The gesture router, exposed so a host can tune tap slop / long-press timing.</summary>
    public SkiaPointerRouter Pointer => _pointer;

    /// <summary>
    /// Whether to paint the dark palette. Defaults to the Windows apps theme (read once, at construction
    /// of the first frame); set it explicitly to follow the app's own theme instead.
    /// </summary>
    public bool Dark { get; set; } = WindowsTheme.IsDark();

    void OnInvalidate()
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }

    // FrameworkElement has no intrinsic size; report what the parent offers so the canvas fills its slot
    // (an infinite constraint — inside a ScrollViewer or StackPanel — has nothing to fill, hence the 0).
    protected override WpfSize MeasureOverride(WpfSize availableSize) => new(
        double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
        double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        // HiDPI: the bitmap is allocated in device pixels and the canvas scaled up, so text and strokes
        // are crisp; the engine still lays out in DIPs.
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var pw = Math.Max(1, (int)Math.Ceiling(width * scale));
        var ph = Math.Max(1, (int)Math.Ceiling(height * scale));

        if (_bitmap is null || pw != _pixelWidth || ph != _pixelHeight)
        {
            _bitmap = new WriteableBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32, null);
            _pixelWidth = pw;
            _pixelHeight = ph;
        }

        _bitmap.Lock();
        try
        {
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, _bitmap.BackBuffer, _bitmap.BackBufferStride);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale((float)scale);
            _bridge.Paint(canvas, new SKSize((float)width, (float)height), Dark);
            canvas.Flush();
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, pw, ph));
        }
        finally
        {
            _bitmap.Unlock();
        }

        // The transparent fill is not decoration: WPF hit-tests against rendered geometry, and without a
        // brush covering the full bounds a click on a transparent region of the canvas would fall through.
        dc.DrawRectangle(WpfBrushes.Transparent, null, new WpfRect(0, 0, width, height));
        dc.DrawImage(_bitmap, new WpfRect(0, 0, width, height));
    }

    // ---- input ---------------------------------------------------------------
    // Raw down/move/up go into the router, which resolves tap / long-press / swipe / continuous drag /
    // slider scrub. Feeding it a synthesized click instead would leave .OnDrag and .OnMagnify dead and
    // sliders inert. Mouse positions are already in DIPs, which is the engine's layout space.

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        CaptureMouse();
        _pointer.Down(Sk(e.GetPosition(this)), _clock);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e) => _pointer.Move(Sk(e.GetPosition(this)), _clock);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _pointer.Up(Sk(e.GetPosition(this)), _clock);
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e) => _pointer.Cancel();

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        var p = Sk(e.GetPosition(this));
        // WPF has no pinch event for a mouse; ctrl+wheel is the conventional desktop zoom, matching the
        // Silk/GLFW head.
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            _pointer.PinchDelta(p, 1 + e.Delta / 120f * 0.05f);
        }
        else
        {
            _pointer.EndPinch(p);
            // A wheel notch is 120 units; 40px per notch matches the other desktop heads.
            _bridge.Scroll(p, -e.Delta / 120f * 40);
        }
        e.Handled = true;
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        // A real IME/keyboard composition arrives here as finished text, which is what survives dead keys
        // and candidate windows — the engine only ever needs the resulting characters.
        foreach (var ch in e.Text)
            if (!char.IsControl(ch))
                _bridge.InsertText(ch.ToString());
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Back:
                _bridge.DeleteBackward();
                e.Handled = true;
                break;
            case Key.Escape:
                _bridge.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    static SKPoint Sk(Point p) => new((float)p.X, (float)p.Y);
}
