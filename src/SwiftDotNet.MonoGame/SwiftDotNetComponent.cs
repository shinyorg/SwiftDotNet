using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Microsoft.Xna.Framework.Input.Touch;
using SkiaSharp;
using SwiftDotNet.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace SwiftDotNet;

/// <summary>
/// Hosts a SwiftDotNet UI inside a MonoGame game — a full-screen UI, or a panel over the scene.
/// </summary>
/// <remarks>
/// <para>MonoGame ships no UI toolkit at all, which is what this is for. The engine is unchanged: layout,
/// hit-testing, gestures, scrolling, animation and the paint pass are the same
/// <c>SwiftDotNet.Graphics</c> code every other self-drawing backend runs. This component supplies only
/// what a host owes the engine — a surface to draw into, a pointer stream, and a repaint signal.</para>
///
/// <para>Skia composites into a managed pixel buffer, which is uploaded to a <see cref="Texture2D"/> and
/// blitted by a <see cref="SpriteBatch"/>. Repaint is demand-driven: a frame costs nothing but the blit
/// unless a patch landed or an animation is running.</para>
///
/// <para>Usage — add it to <c>Game.Components</c> and give it a root view:</para>
/// <code>
/// Components.Add(new SwiftDotNetComponent(this, new ContentView()));
/// </code>
/// <para>For a UI that owns the whole window, <see cref="SwiftDotNetGame"/> does the same wiring plus the
/// graphics-device setup. For a HUD over a scene, set <see cref="Bounds"/> and
/// <see cref="Transparent"/>.</para>
/// </remarks>
public class SwiftDotNetComponent : DrawableGameComponent
{
    readonly FrameLoopSyncContext _pump = new();

    SkiaBridge _bridge = null!;
    SkiaPointerRouter _router = null!;
    SpriteBatch _sprites = null!;
    Texture2D? _texture;
    SKSurface? _surface;
    byte[] _pixels = [];
    GCHandle _pin;
    int _width, _height;
    bool _dirty = true;
    bool _pointerDown;
    int _wheel;
    XnaRectangle _lastBounds;
    bool _lastTransparent;

    /// <param name="game">The owning game.</param>
    /// <param name="root">
    /// The view to render. May be null here and assigned to <see cref="Root"/> before
    /// <see cref="GameComponent.Initialize"/> runs — usually by overriding <see cref="BuildRoot"/>.
    /// </param>
    public SwiftDotNetComponent(Game game, View? root = null) : base(game) => Root = root;

    /// <summary>The view to render. Set before <c>Initialize</c>, or override <see cref="BuildRoot"/>.</summary>
    public View? Root { get; set; }

    /// <summary>Optional services for <c>[Inject]</c>, from a <c>SwiftDotNetApp</c> builder.</summary>
    public IServiceProvider? Services { get; set; }

    /// <summary>Match the dark theme. MonoGame exposes no OS appearance API, so this is the host's call.</summary>
    public bool Dark
    {
        get => _dark;
        set { if (_dark != value) { _dark = value; _dirty = true; } }
    }
    bool _dark;

    /// <summary>
    /// Where the UI is drawn, in back-buffer pixels. Empty (the default) means the whole back buffer.
    /// A HUD or an inspector panel sets this to its own rect; pointer coordinates are mapped into it.
    /// </summary>
    public XnaRectangle Bounds { get; set; }

    /// <summary>
    /// Clear to transparent instead of the theme background, so the scene shows through wherever the UI
    /// draws nothing. This is the HUD/menu-overlay setting.
    /// </summary>
    public bool Transparent { get; set; }

    /// <summary>
    /// Extra device pixels per layout unit. 1 renders at back-buffer resolution; 2 renders the UI at
    /// double resolution into the same rect, which is what a HiDPI display wants.
    /// </summary>
    public float RenderScale
    {
        get => _renderScale;
        set { if (Math.Abs(_renderScale - value) > float.Epsilon) { _renderScale = Math.Max(0.1f, value); _dirty = true; } }
    }
    float _renderScale = 1;

    /// <summary>Wheel notches to layout units, per notch. MonoGame reports 120 per detent.</summary>
    public float ScrollSpeed { get; set; } = 40;

    /// <summary>Feed keyboard text into the focused field. Off for a game that owns the keyboard.</summary>
    public bool HandleTextInput { get; set; } = true;

    /// <summary>The engine bridge, for hosts that want to drive it directly (custom renderers, tests).</summary>
    public SkiaBridge Bridge => _bridge;

    /// <summary>Force a repaint on the next <see cref="Draw"/>. Rarely needed — the engine invalidates
    /// itself when a patch lands — but a host that changes <see cref="Bounds"/> or resizes the window
    /// wants the next frame re-laid-out.</summary>
    public void Invalidate() => _dirty = true;

    /// <summary>Override to supply the root view. Called once, during <c>Initialize</c>.</summary>
    protected virtual View? BuildRoot() => Root;

    public override void Initialize()
    {
        // Install the pump *before* SwiftApp.Run captures the ambient context, or an off-thread State
        // mutation rebuilds the tree while Draw is reading it.
        SynchronizationContext.SetSynchronizationContext(_pump);

        _bridge = new SkiaBridge();
        _router = new SkiaPointerRouter(_bridge);
        _bridge.Invalidate += () => _dirty = true;

        var root = BuildRoot() ?? throw new InvalidOperationException(
            $"{nameof(SwiftDotNetComponent)} has no root view. Pass one to the constructor, set {nameof(Root)}, or override {nameof(BuildRoot)}.");

        SwiftApp.Run(root, _bridge, Services);

        if (HandleTextInput) Game.Window.TextInput += OnTextInput;

        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sprites = new SpriteBatch(GraphicsDevice);
        base.LoadContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (HandleTextInput) Game.Window.TextInput -= OnTextInput;
            ReleaseSurface();
            _sprites?.Dispose();
        }
        base.Dispose(disposing);
    }

    // ---- frame ---------------------------------------------------------------

    public override void Update(GameTime gameTime)
    {
        _pump.Drain();

        var seconds = gameTime.TotalGameTime.TotalSeconds;
        PumpInput(seconds);

        // Implicit animations advance on the engine's clock and keep the surface dirty while running.
        if (_bridge.Tick(gameTime.ElapsedGameTime.TotalSeconds)) _dirty = true;

        base.Update(gameTime);
    }

    public override void Draw(GameTime gameTime)
    {
        var target = TargetBounds();
        if (target.Width <= 0 || target.Height <= 0) return;

        EnsureSurface(target);
        if (_dirty) Repaint(target);

        // Premultiplied alpha: Skia's Premul surface is already in the form AlphaBlend expects, so an
        // opaque UI and a transparent HUD both composite correctly with one blend state.
        _sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);
        _sprites.Draw(_texture!, target, XnaColor.White);
        _sprites.End();

        base.Draw(gameTime);
    }

    XnaRectangle TargetBounds() =>
        Bounds.IsEmpty
            ? new XnaRectangle(0, 0, GraphicsDevice.PresentationParameters.BackBufferWidth,
                                     GraphicsDevice.PresentationParameters.BackBufferHeight)
            : Bounds;

    void EnsureSurface(XnaRectangle target)
    {
        var width = Math.Max(1, (int)Math.Round(target.Width * RenderScale));
        var height = Math.Max(1, (int)Math.Round(target.Height * RenderScale));
        if (_texture is not null && width == _width && height == _height &&
            target == _lastBounds && Transparent == _lastTransparent) return;

        ReleaseSurface();

        _width = width;
        _height = height;
        _lastBounds = target;
        _lastTransparent = Transparent;

        // Skia writes straight into the array MonoGame uploads — one buffer, no staging bitmap. The pin is
        // required for the whole surface's life, which is why it is released explicitly rather than by a
        // fixed block.
        _pixels = new byte[width * height * 4];
        _pin = GCHandle.Alloc(_pixels, GCHandleType.Pinned);

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        _surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), width * 4);

        _texture = new Texture2D(GraphicsDevice, width, height, mipmap: false, SurfaceFormat.Color);
        _bridge.ClearColor = Transparent ? new Graphics.Color(0, 0, 0, 0) : null;
        _dirty = true;
    }

    void ReleaseSurface()
    {
        _surface?.Dispose();
        _surface = null;
        _texture?.Dispose();
        _texture = null;
        if (_pin.IsAllocated) _pin.Free();
        _pixels = [];
    }

    void Repaint(XnaRectangle target)
    {
        _dirty = false;

        var canvas = _surface!.Canvas;
        var restore = canvas.Save();
        canvas.Scale(_width / (float)target.Width, _height / (float)target.Height);
        _bridge.Paint(canvas, new SKSize(target.Width, target.Height), Dark);
        canvas.RestoreToCount(restore);
        _surface.Flush();

        _texture!.SetData(_pixels);
    }

    // ---- input ---------------------------------------------------------------

    /// <summary>
    /// Feeds MonoGame's polled input into the engine's gesture recognizer, which resolves taps,
    /// long-presses, swipes, drags and scrolls. A host never re-derives any of that — that is the whole
    /// point of <see cref="PointerRouter"/>.
    /// </summary>
    void PumpInput(double time)
    {
        var handled = false;

        // Touch first: on a phone or tablet head there is no mouse, and MonoGame reports a phantom
        // (0,0) mouse position rather than nothing.
        var touches = TouchPanel.GetState();
        if (touches.Count > 0)
        {
            var touch = touches[0];
            var p = ToCanvas(touch.Position.X, touch.Position.Y);
            switch (touch.State)
            {
                case TouchLocationState.Pressed: _pointerDown = true; _router.Down(p, time); break;
                case TouchLocationState.Moved when _pointerDown: _router.Move(p, time); break;
                case TouchLocationState.Released when _pointerDown: _pointerDown = false; _router.Up(p, time); break;
            }
        }
        else
        {
            var mouse = Mouse.GetState();
            var p = ToCanvas(mouse.X, mouse.Y);
            var down = mouse.LeftButton == ButtonState.Pressed;

            if (down && !_pointerDown) { _pointerDown = true; _router.Down(p, time); }
            else if (!down && _pointerDown) { _pointerDown = false; _router.Up(p, time); }
            else if (down) _router.Move(p, time);

            // ScrollWheelValue is cumulative, 120 per detent.
            var wheel = mouse.ScrollWheelValue;
            if (wheel != _wheel)
            {
                var notches = (wheel - _wheel) / 120f;
                _wheel = wheel;
                if (_bridge.Scroll(p, -notches * ScrollSpeed)) handled = true;
            }
        }

        // The long-press timer needs a clock tick even when nothing moved.
        _router.Poll(time);
        if (handled) _dirty = true;
    }

    void OnTextInput(object? sender, TextInputEventArgs e)
    {
        if (_bridge.FocusedId is null) return;
        if (e.Key == Keys.Back) _bridge.DeleteBackward();
        else if (!char.IsControl(e.Character)) _bridge.InsertText(e.Character.ToString());
    }

    /// <summary>Back-buffer pixels to layout units inside <see cref="Bounds"/>.</summary>
    SKPoint ToCanvas(float x, float y)
    {
        var target = TargetBounds();
        return new SKPoint(x - target.X, y - target.Y);
    }
}
