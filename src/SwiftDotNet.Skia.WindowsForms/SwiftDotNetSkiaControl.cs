using System.ComponentModel;
using System.Drawing.Imaging;
using System.Windows.Forms;
using SkiaSharp;
// This file lives in the SwiftDotNet namespace, where `Color`, `Image` and `Rectangle` are the *DSL*
// types — a simple name binds to the enclosing namespace's member before any using-imported one, so the
// GDI+/WinForms types are reached through distinctly-named aliases (a same-name alias is itself CS0576).
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;
// Also aliased because a bare `Timer` is ambiguous between the WinForms and threading ones once
// ImplicitUsings has imported System.Threading.
using WinFormsTimer = System.Windows.Forms.Timer;

namespace SwiftDotNet;

/// <summary>
/// A Windows Forms control that hosts a SwiftDotNet view tree via the SkiaSharp self-drawing backend.
/// Drop it on a form as the content:
/// <code>
/// Controls.Add(new SwiftDotNetSkiaControl(new ContentView()) { Dock = DockStyle.Fill });
/// </code>
/// </summary>
/// <remarks>
/// <para>This is the <em>only</em> WinForms backend, by design. Translating the view tree to real WinForms
/// controls was considered and rejected: GDI controls have no render transforms, no per-element opacity,
/// no rounded-corner clipping, no vector shapes and no animation system, so <c>.Rotation</c>,
/// <c>.ScaleEffect</c>, <c>.Opacity</c>, gradients, shadows, corner radii and <c>.Keyframes</c> would all
/// have had to become silent no-ops. Painting the surface ourselves gives WinForms the complete feature
/// set instead, identical to every other Skia head.</para>
/// <para>The surface is a 32-bit premultiplied GDI+ bitmap whose locked bits an <see cref="SKSurface"/>
/// draws straight into, blitted in <see cref="OnPaint"/> — the same thing SkiaSharp's own
/// <c>SKControl</c> does, done here directly so the project can stay on a plain net10.0 SkiaSharp
/// reference instead of the .NET-Framework-only <c>SkiaSharp.Views.WindowsForms</c> package.</para>
/// </remarks>
public class SwiftDotNetSkiaControl : Control
{
    readonly SkiaBridge _bridge = new();
    readonly SkiaPointerRouter _pointer;
    readonly WinFormsTimer _timer;
    GdiBitmap? _bitmap;
    double _clock;

    public SwiftDotNetSkiaControl(View root) : this(root, null) { }

    /// <param name="services">
    /// The container <c>[Inject]</c> properties and <c>SwiftHost.Services</c> resolve from — pass a
    /// <c>SwiftDotNetApp.Services</c> to share one container with the rest of the app.
    /// </param>
    public SwiftDotNetSkiaControl(View root, IServiceProvider? services)
    {
        _pointer = new SkiaPointerRouter(_bridge);

        // UserPaint + AllPaintingInWmPaint + OptimizedDoubleBuffer: we own every pixel and want no
        // background erase (which is what makes a hand-painted WinForms control flicker). Selectable is
        // what lets the control take focus, without which no key ever reaches the engine.
        SetStyle(
            ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
            true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        TabStop = true;

        _bridge.Invalidate += OnBridgeInvalidate;

        // WinForms installs a WindowsFormsSynchronizationContext on the UI thread once a control handle
        // exists, so SwiftApp.Run captures a real one and off-thread State mutations marshal back.
        SwiftApp.Run(root, _bridge, services);

        // Drives the implicit-animation clock, and doubles as the pointer router's clock — it needs one
        // to resolve a held press into a long-press.
        _timer = new WinFormsTimer { Interval = 16 };
        _timer.Tick += (_, _) =>
        {
            _clock += 0.016;
            var animating = _bridge.Tick(0.016);
            _pointer.Poll(_clock);
            if (animating) Invalidate();
        };
        _timer.Start();
    }

    // Runtime-only surface: none of these are designer-authored, so they are hidden from the property
    // grid and excluded from code serialization (which is also what analyzer WFO1000 asks for).

    /// <summary>The bridge, exposed so a host can drive text input or inspect laid-out frames.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SkiaBridge Bridge => _bridge;

    /// <summary>The gesture router, exposed so a host can tune tap slop / long-press timing.</summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public SkiaPointerRouter Pointer => _pointer;

    /// <summary>
    /// Whether to paint the dark palette. Defaults to the Windows apps theme; set it explicitly to
    /// follow the app's own theme instead.
    /// </summary>
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dark { get; set; } = WindowsTheme.IsDark();

    /// <summary>Device pixels per DIP. WinForms measures in physical pixels; the engine lays out in DIPs.
    /// (Named DipScale, not Scale: <c>Control.Scale(float)</c> already owns that name.)</summary>
    float DipScale => DeviceDpi / 96f;

    void OnBridgeInvalidate()
    {
        if (!IsHandleCreated) return;
        if (InvokeRequired) BeginInvoke(Invalidate);
        else Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var pw = ClientSize.Width;
        var ph = ClientSize.Height;
        if (pw <= 0 || ph <= 0) return;

        if (_bitmap is null || _bitmap.Width != pw || _bitmap.Height != ph)
        {
            _bitmap?.Dispose();
            _bitmap = new GdiBitmap(pw, ph, PixelFormat.Format32bppPArgb);
        }

        var data = _bitmap.LockBits(new GdiRectangle(0, 0, pw, ph), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var info = new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, data.Scan0, data.Stride);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            // HiDPI: the bitmap is in device pixels and the canvas is scaled up, so the engine keeps
            // laying out in DIPs and text/strokes stay crisp.
            canvas.Scale(DipScale);
            _bridge.Paint(canvas, new SKSize(pw / DipScale, ph / DipScale), Dark);
            canvas.Flush();
        }
        finally
        {
            _bitmap.UnlockBits(data);
        }

        // Unscaled: the bitmap is already in the client area's own (physical) pixels.
        e.Graphics.DrawImageUnscaled(_bitmap, 0, 0);
    }

    /// <summary>Nothing to erase — the paint pass covers every pixel, and erasing here is what flickers.</summary>
    protected override void OnPaintBackground(PaintEventArgs e) { }

    // ---- input ---------------------------------------------------------------
    // Raw down/move/up go into the router, which resolves tap / long-press / swipe / continuous drag /
    // slider scrub. Feeding it a synthesized click instead would leave .OnDrag and .OnMagnify dead and
    // sliders inert. WinForms reports physical pixels, so each position is scaled back into DIPs.

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _pointer.Down(Sk(e), _clock);
    }

    protected override void OnMouseMove(MouseEventArgs e) => _pointer.Move(Sk(e), _clock);

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _pointer.Up(Sk(e), _clock);
    }

    protected override void OnMouseLeave(EventArgs e) => _pointer.Cancel();

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var p = Sk(e);
        // WinForms has no pinch event for a mouse; ctrl+wheel is the conventional desktop zoom, matching
        // the Silk/GLFW and WPF heads.
        if ((ModifierKeys & Keys.Control) != 0)
        {
            _pointer.PinchDelta(p, 1 + e.Delta / 120f * 0.05f);
        }
        else
        {
            _pointer.EndPinch(p);
            // A wheel notch is 120 units; 40px per notch matches the other desktop heads.
            _bridge.Scroll(p, -e.Delta / 120f * 40);
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar)) _bridge.InsertText(e.KeyChar.ToString());
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Back:
                _bridge.DeleteBackward();
                e.Handled = true;
                break;
            case Keys.Escape:
                _bridge.ClearFocus();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Backspace, Escape and the arrows are "dialog keys" that WinForms routes to the parent form instead
    /// of raising KeyDown here. The canvas owns text editing, so it claims them.
    /// </summary>
    protected override bool IsInputKey(Keys keyData) => keyData switch
    {
        Keys.Back or Keys.Escape or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Tab => true,
        _ => base.IsInputKey(keyData),
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _bitmap?.Dispose();
            _bitmap = null;
        }
        base.Dispose(disposing);
    }

    SKPoint Sk(MouseEventArgs e) => new(e.X / DipScale, e.Y / DipScale);
}
