using Godot;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// A Godot <see cref="Control"/> node that renders a SwiftDotNet view tree with Godot's own 2D renderer.
/// </summary>
/// <remarks>
/// <para>Subclass it in your game and return a root view:</para>
/// <code>
/// public partial class MainMenu : SwiftDotNetControl
/// {
///     protected override View BuildRoot() => new MenuView();
/// }
/// </code>
/// <para>Attach that script to a <c>Control</c> node, anchor it however you like (full-rect for a screen,
/// a corner for a HUD panel), and it behaves like any other control — including inside a
/// <c>CanvasLayer</c> over a running scene.</para>
///
/// <para>Nothing here is SwiftDotNet-specific plumbing you could not write yourself: the node supplies a
/// canvas, a pointer stream and a repaint signal, and the engine in <c>SwiftDotNet.Graphics</c> does
/// everything else. See <see cref="GodotCanvas"/> for how the immediate-mode paint pass maps onto Godot's
/// retained canvas items.</para>
/// </remarks>
public partial class SwiftDotNetControl : Control
{
    readonly FrameLoopSyncContext _pump = new();

    GodotFonts _fonts = null!;
    GodotCanvas? _canvas;
    VisualBridge _bridge = null!;
    PointerRouter _router = null!;
    double _clock;
    bool _dirty = true;
    bool _pointerDown;

    /// <summary>The view to render. Set before the node enters the tree, or override <see cref="BuildRoot"/>.</summary>
    public View? Root { get; set; }

    /// <summary>Optional services for <c>[Inject]</c>, from a <c>SwiftDotNetApp</c> builder.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>
    /// Follow the OS dark appearance. Godot exposes this (unlike Unity), so it is on by default and
    /// <see cref="Dark"/> only matters on platforms that do not report one.
    /// </summary>
    [Export] public bool FollowSystemAppearance { get; set; } = true;

    /// <summary>Render the dark theme. Ignored while <see cref="FollowSystemAppearance"/> resolves one.</summary>
    [Export] public bool Dark { get; set; }

    /// <summary>
    /// Draw only the UI's own pixels, leaving the scene behind visible. This is the HUD / pause-menu
    /// setting; the default paints the theme's window background across the control.
    /// </summary>
    [Export] public bool Transparent { get; set; }

    /// <summary>Layout units scrolled per wheel notch.</summary>
    [Export] public float ScrollSpeed { get; set; } = 40;

    /// <summary>Pop the on-screen keyboard when a text field takes focus (Android/iOS exports).</summary>
    [Export] public bool UseVirtualKeyboard { get; set; } = true;

    /// <summary>The engine bridge, for a host that wants to drive it directly.</summary>
    public VisualBridge Bridge => _bridge;

    /// <summary>The Godot font provider, so a game can assign its own typeface.</summary>
    public GodotFonts Fonts => _fonts;

    /// <summary>Override to supply the root view. Called once, as the node enters the tree.</summary>
    protected virtual View? BuildRoot() => Root;

    /// <summary>
    /// Builds the engine bridge — i.e. picks the rasterizer. The default draws through Godot's own
    /// renderer; <c>SwiftDotNetTextureControl</c> overrides it to paint with SkiaSharp instead.
    /// </summary>
    protected virtual VisualBridge CreateBridge() => new(_fonts, new GodotImages());

    /// <summary>Whether this frame should paint the dark theme.</summary>
    protected bool ResolveDark() =>
        FollowSystemAppearance && DisplayServer.IsDarkModeSupported() ? DisplayServer.IsDarkMode() : Dark;

    public override void _Ready()
    {
        // Install the pump before SwiftApp.Run captures the ambient context: Godot signals, HTTP requests
        // and timers can all land off the main thread, and a State change from one of those would
        // otherwise rebuild the tree while _Draw is walking it.
        SynchronizationContext.SetSynchronizationContext(_pump);

        _fonts = new GodotFonts();
        _bridge = CreateBridge();
        _router = new PointerRouter(_bridge);
        _bridge.Invalidate += () => _dirty = true;
        _bridge.FocusChanged += OnFocusChanged;

        var root = BuildRoot() ?? throw new InvalidOperationException(
            $"{nameof(SwiftDotNetControl)} has no root view. Set {nameof(Root)} or override {nameof(BuildRoot)}.");

        SwiftApp.Run(root, _bridge, Services);

        // Controls do not receive input unless they say they want it, and a UI that ignores clicks is the
        // single most common Godot wiring mistake.
        MouseFilter = MouseFilterEnum.Stop;
        Resized += () => _dirty = true;
    }

    public override void _ExitTree()
    {
        _canvas?.Dispose();
        base._ExitTree();
    }

    public override void _Process(double delta)
    {
        _pump.Drain();
        _clock += delta;

        if (_bridge.Tick(delta)) _dirty = true;
        _router.Poll(_clock);           // resolves a held press into a long-press

        if (_dirty)
        {
            _dirty = false;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _canvas ??= new GodotCanvas(_fonts);
        _bridge.ClearColor = Transparent ? new Graphics.Color(0, 0, 0, 0) : null;

        _canvas.Begin(GetCanvasItem());
        _bridge.Draw(_canvas, new Graphics.Size(Size.X, Size.Y), ResolveDark());
        _canvas.End();
    }

    // ---- input ---------------------------------------------------------------

    /// <summary>
    /// Feeds Godot's input events into the engine's gesture recognizer, which resolves taps, long-presses,
    /// swipes, drags and scrolls identically to every other backend.
    /// </summary>
    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                var at = ToCanvas(click.Position);
                if (click.Pressed) { _pointerDown = true; _router.Down(at, _clock); }
                else if (_pointerDown) { _pointerDown = false; _router.Up(at, _clock); }
                AcceptEvent();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                var direction = wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                if (wheel.Pressed && _bridge.Scroll(ToCanvas(wheel.Position), -direction * ScrollSpeed))
                {
                    _dirty = true;
                    AcceptEvent();
                }
                break;

            case InputEventMouseMotion motion when _pointerDown:
                _router.Move(ToCanvas(motion.Position), _clock);
                break;

            // Touch and mouse are separate event families in Godot; a phone export only ever sees these.
            case InputEventScreenTouch touch:
                var point = ToCanvas(touch.Position);
                if (touch.Pressed) { _pointerDown = true; _router.Down(point, _clock); }
                else if (_pointerDown) { _pointerDown = false; _router.Up(point, _clock); }
                AcceptEvent();
                break;

            case InputEventScreenDrag drag when _pointerDown:
                _router.Move(ToCanvas(drag.Position), _clock);
                break;

            // Godot has a real system pinch recognizer (trackpad and touch), so the engine's .OnMagnify
            // gets the OS gesture rather than the ctrl+wheel substitute the GLFW host has to use.
            case InputEventMagnifyGesture magnify:
                _router.PinchDelta(ToCanvas(magnify.Position), magnify.Factor);
                AcceptEvent();
                break;

            case InputEventKey { Pressed: true } key when _bridge.FocusedId is not null:
                if (key.Keycode == Key.Backspace) _bridge.DeleteBackward();
                else if (key.Unicode >= 32) _bridge.InsertText(char.ConvertFromUtf32((int)key.Unicode));
                else break;
                AcceptEvent();
                break;
        }
    }

    void OnFocusChanged(string? id)
    {
        if (!UseVirtualKeyboard || !DisplayServer.HasFeature(DisplayServer.Feature.VirtualKeyboard)) return;

        if (id is null) DisplayServer.VirtualKeyboardHide();
        else DisplayServer.VirtualKeyboardShow(_bridge.FocusedText ?? "");
    }

    Graphics.Point ToCanvas(Vector2 position) => new(position.X, position.Y);
}
