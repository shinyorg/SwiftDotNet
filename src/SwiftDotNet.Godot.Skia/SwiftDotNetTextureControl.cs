using System.Runtime.InteropServices;
using Godot;
using SkiaSharp;
using SwiftDotNet.Graphics;

namespace SwiftDotNet;

/// <summary>
/// The Skia-into-a-texture variant of <see cref="SwiftDotNetControl"/>: SkiaSharp paints the UI into an
/// <see cref="ImageTexture"/> which the control blits.
/// </summary>
/// <remarks>
/// <para>Use this when pixel-for-pixel parity with the other self-drawing backends matters more than the
/// native dependency — a screenshot test suite shared with the Skia head, say, or a control whose custom
/// renderer is written against <c>SKCanvas</c>. Otherwise prefer <see cref="SwiftDotNetControl"/>: it
/// draws through Godot's own renderer, needs no <c>libSkiaSharp</c> on any export target, and does not
/// re-upload the whole surface when one pixel changes.</para>
///
/// <para>Everything except rendering is inherited, so gestures, focus, the virtual keyboard, dark mode and
/// the transparent-HUD switch behave identically between the two.</para>
/// </remarks>
public partial class SwiftDotNetTextureControl : SwiftDotNetControl
{
    SkiaBridge _skia = null!;
    ImageTexture? _texture;
    Godot.Image? _image;
    SKSurface? _surface;
    byte[] _pixels = [];
    GCHandle _pin;
    int _width, _height;

    /// <summary>
    /// Extra device pixels per layout unit. Godot's own content scale already applies; this is on top of
    /// it, for rendering the UI at a higher resolution than the control's layout size.
    /// </summary>
    [Export] public float RenderScale { get; set; } = 1;

    /// <summary>The Skia bridge, for custom <c>ISkiaRenderer</c> registration.</summary>
    public SkiaBridge SkiaBridge => _skia;

    /// <summary>Builds the bridge on Skia's rasterizer rather than Godot's.</summary>
    protected override VisualBridge CreateBridge() => _skia = new SkiaBridge();

    public override void _ExitTree()
    {
        Release();
        base._ExitTree();
    }

    public override void _Draw()
    {
        var width = Math.Max(1, (int)Math.Round(Size.X * RenderScale));
        var height = Math.Max(1, (int)Math.Round(Size.Y * RenderScale));
        if (width != _width || height != _height) Allocate(width, height);

        Bridge.ClearColor = Transparent ? new Graphics.Color(0, 0, 0, 0) : null;

        var canvas = _surface!.Canvas;
        canvas.Clear(SKColors.Transparent);
        var restore = canvas.Save();
        canvas.Scale(RenderScale, RenderScale);
        _skia.Paint(canvas, new SKSize(Size.X, Size.Y), Dark);
        canvas.RestoreToCount(restore);
        _surface.Flush();

        // SetData over the same Image object, then Update on the same texture: no new RID per frame, and
        // Godot re-uploads in place.
        _image!.SetData(_width, _height, false, Godot.Image.Format.Rgba8, _pixels);
        _texture!.Update(_image);

        DrawTextureRect(_texture, new Rect2(Vector2.Zero, Size), false);
    }

    void Allocate(int width, int height)
    {
        Release();
        _width = width;
        _height = height;

        // Skia composites straight into the array Godot uploads — one buffer, no staging bitmap.
        _pixels = new byte[width * height * 4];
        _pin = GCHandle.Alloc(_pixels, GCHandleType.Pinned);
        _surface = SKSurface.Create(
            new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
            _pin.AddrOfPinnedObject(), width * 4);

        _image = Godot.Image.CreateFromData(width, height, false, Godot.Image.Format.Rgba8, _pixels);
        _texture = ImageTexture.CreateFromImage(_image);
    }

    void Release()
    {
        _surface?.Dispose();
        _surface = null;
        if (_pin.IsAllocated) _pin.Free();
        _pixels = [];
        _width = _height = 0;
    }
}
