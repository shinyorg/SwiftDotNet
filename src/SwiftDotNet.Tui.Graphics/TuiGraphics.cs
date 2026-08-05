using XenoAtom.Terminal.Graphics;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Graphics;

using TImage = XenoAtom.Terminal.UI.Graphics.Image;

namespace SwiftDotNet;

/// <summary>
/// Upgrades the terminal backend's <c>Image</c> nodes from character art to <b>real pixels</b>, using
/// whichever graphics protocol the terminal speaks (Sixel, Kitty, iTerm2). Call it once, before
/// <see cref="SwiftDotNetHost.Run"/>:
/// <code>
/// TuiGraphics.Enable();
/// return SwiftDotNetHost.Run(new ContentView());
/// </code>
///
/// <para>Two things happen. Images become a real <c>Image</c> visual whose <c>FallbackContent</c> is the
/// character-art visual the core backend already built — so a terminal without graphics support degrades
/// automatically rather than showing nothing. And the Skia-backed rasterizer that ships with this package
/// is registered as a decoder, which is what adds JPEG / WebP / GIF to the core's PNG-only support (for
/// both paths, art included).</para>
/// </summary>
public static class TuiGraphics
{
    static TerminalImageGraphicsPresenter? _presenter;

    /// <summary>
    /// Installs the presenter and decoder. Idempotent; safe to call from a host bootstrap that may run
    /// more than once in a process (tests, a preview host).
    /// </summary>
    /// <param name="options">Presenter options — protocol pinning, resampling quality, matte colour.</param>
    public static void Enable(TerminalImageGraphicsPresenterOptions? options = null)
    {
        if (_presenter is null)
        {
            _presenter = new TerminalImageGraphicsPresenter(options ?? new TerminalImageGraphicsPresenterOptions
            {
                // Never throw on a terminal that can't do graphics — that is exactly the case the art
                // fallback exists for.
                ThrowIfUnsupported = false,
            });
            TuiImageDecoders.Register(new SkiaImageDecoder());
        }

        TuiImageOptions.VisualFactory = Build;
        TuiImageOptions.GraphicsPresenter = _presenter;
    }

    /// <summary>The presenter installed by <see cref="Enable"/>; null until it is called.</summary>
    public static ITerminalGraphicsPresenter? Presenter => _presenter;

    static Visual Build(TuiImageRequest request)
    {
        var source = Source(request);
        if (source is null) return request.Fallback;

        return new TImage(source)
        {
            ScaleMode = request.Fill ? ImageScaleMode.Fill : ImageScaleMode.Fit,
            PreserveAspectRatio = true,
            FallbackContent = request.Fallback,
        };
    }

    static TerminalImageSource? Source(TuiImageRequest request)
    {
        // A remote URL has no synchronous source here — the core backend already fetches it into the art
        // visual off-thread, so a URL image stays on the art path. Local bytes and files map directly.
        if (request.Bytes is { Length: > 0 } bytes)
            return TerminalImageSource.FromEncodedBytes(bytes, sourceId: null);
        if (request.File is { Length: > 0 } file && File.Exists(file))
            return TerminalImageSource.FromFile(file);
        return null;
    }
}

/// <summary>
/// Decodes with the rasterizer that ships with XenoAtom.Terminal.Graphics (SkiaSharp underneath), which
/// is what widens the character-art path from PNG-only to every format Skia reads. Registered by
/// <see cref="TuiGraphics.Enable"/>; the core <see cref="TuiPngDecoder"/> stays in the chain behind it.
/// </summary>
sealed class SkiaImageDecoder : ITuiImageDecoder
{
    public bool TryDecode(ReadOnlySpan<byte> bytes, out TuiPixels? pixels)
    {
        pixels = null;
        var data = bytes.ToArray();
        try
        {
            var source = TerminalImageSource.FromEncodedBytes(data, sourceId: null);
            var frame = source.GetFrameAsync(TerminalImageFrameRequest.Default, default)
                .AsTask().GetAwaiter().GetResult();
            // An unrecognised payload yields no frame — hand it back to the next decoder in the chain.
            if (frame is null) return false;

            var raster = TerminalImageRasterizer.Default
                .RasterizeAsync(frame, new TerminalRasterizeRequest(
                    new TerminalImageSize(frame.PixelWidth, frame.PixelHeight)), default)
                .AsTask().GetAwaiter().GetResult();

            // The art renderer wants tightly packed RGBA; a rasterized image may be strided.
            var width = raster.PixelWidth;
            var height = raster.PixelHeight;
            var src = raster.PixelBytes.Span;
            var rgba = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
                src.Slice(y * raster.StrideBytes, width * 4).CopyTo(rgba.AsSpan(y * width * 4));

            pixels = new TuiPixels(width, height, rgba);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
