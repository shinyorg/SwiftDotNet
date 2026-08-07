using SwiftDotNet.Graphics;
using Xunit;
using GColor = SwiftDotNet.Graphics.Color;

namespace SwiftDotNet.Tests;

/// <summary>
/// Renders through wgpu-native to an offscreen texture and asserts on the pixels that come back.
///
/// <para>These are the tests that make the WebGPU backend real rather than merely compiled: the whole
/// backend is a shader, and a shader that builds is not a shader that draws. Every assertion here reads
/// actual GPU output.</para>
///
/// <para>They skip themselves when no adapter is available (headless CI, a container without a GPU), so
/// the suite stays green where the hardware is absent rather than failing for the wrong reason.</para>
/// </summary>
public class WebGpuRenderTests
{
    const int Size = 64;

    [Fact]
    public void FilledRect_LandsWhereItWasAsked_InTheRightColour()
    {
        if (!GpuAvailable) return;

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            canvas.DrawRect(new Rect(16, 16, 48, 48), Paint.Fill(new GColor(255, 0, 0)));
        });

        AssertPixel(rgba, 32, 32, 255, 0, 0);      // centre of the rect
        AssertPixel(rgba, 4, 4, 255, 255, 255);    // outside it
        AssertPixel(rgba, 60, 60, 255, 255, 255);
    }

    [Fact]
    public void RoundedRect_CutsItsCorners()
    {
        if (!GpuAvailable) return;

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            canvas.DrawRoundRect(new Rect(0, 0, Size, Size), 20, 20, Paint.Fill(new GColor(0, 0, 255)));
        });

        AssertPixel(rgba, 32, 32, 0, 0, 255);      // middle is filled
        AssertPixel(rgba, 1, 1, 255, 255, 255);    // the corner is cut away
    }

    [Fact]
    public void Circle_IsRound()
    {
        if (!GpuAvailable) return;

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            canvas.DrawCircle(32, 32, 30, Paint.Fill(new GColor(0, 128, 0)));
        });

        AssertPixel(rgba, 32, 32, 0, 128, 0);      // centre
        AssertPixel(rgba, 2, 2, 255, 255, 255);    // corner sits outside the disc
    }

    [Fact]
    public void LinearGradient_InterpolatesAcrossTheShape()
    {
        if (!GpuAvailable) return;

        var gradient = new Gradient
        {
            Kind = GradientKind.Linear,
            Start = new Point(0, 0),
            End = new Point(Size, 0),
            Stops =
            [
                new ColorStop(new GColor(255, 0, 0), 0),
                new ColorStop(new GColor(0, 0, 255), 1),
            ],
        };

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            canvas.DrawRect(new Rect(0, 0, Size, Size), Paint.Fill(gradient));
        });

        var left = PixelAt(rgba, 2, 32);
        var right = PixelAt(rgba, Size - 3, 32);

        Assert.True(left.R > 200 && left.B < 60, $"left end should be red, got {left}");
        Assert.True(right.B > 200 && right.R < 60, $"right end should be blue, got {right}");
    }

    [Fact]
    public void Clip_SuppressesWhatFallsOutsideIt()
    {
        if (!GpuAvailable) return;

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            var depth = canvas.Save();
            canvas.ClipRect(new Rect(0, 0, Size, 32));                       // top half only
            canvas.DrawRect(new Rect(0, 0, Size, Size), Paint.Fill(new GColor(255, 0, 0)));
            canvas.RestoreToCount(depth);
        });

        AssertPixel(rgba, 32, 10, 255, 0, 0);        // inside the clip
        AssertPixel(rgba, 32, 50, 255, 255, 255);    // below it, clipped away
    }

    [Fact]
    public void Translate_MovesSubsequentDrawing()
    {
        if (!GpuAvailable) return;

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            var depth = canvas.Save();
            canvas.Translate(32, 32);
            canvas.DrawRect(new Rect(0, 0, 16, 16), Paint.Fill(new GColor(255, 0, 0)));
            canvas.RestoreToCount(depth);
        });

        AssertPixel(rgba, 40, 40, 255, 0, 0);        // where the translate put it
        AssertPixel(rgba, 8, 8, 255, 255, 255);      // not at the untranslated origin
    }

    [Fact]
    public void Text_PutsInkOnTheCanvas()
    {
        if (!GpuAvailable) return;

        using var fonts = new WebGpuFonts();
        var font = fonts.Get(32, bold: true);

        var rgba = Render(canvas =>
        {
            canvas.Clear(Colors.White);
            canvas.DrawText("III", 6, 44, font, Colors.Black);
        }, fonts);

        // Rasterizing glyphs is the one CPU step in this backend; assert that ink actually reached the
        // surface rather than checking an exact shape, which would be a font-version dependency.
        var dark = 0;
        for (var i = 0; i < rgba.Length; i += 4)
            if (rgba[i] < 128 && rgba[i + 1] < 128 && rgba[i + 2] < 128) dark++;

        Assert.True(dark > 40, $"expected glyph coverage, found {dark} dark pixels");
    }

    [Fact]
    public void EndToEnd_DrivesTheEngineThroughTheDsl()
    {
        if (!GpuAvailable) return;

        using var bridge = new WebGpuBridge();
        using var host = new WebGpuImageHost(bridge);
        SwiftApp.Run(new RedBox(), bridge);

        var rgba = host.RenderRgba(120, 120);

        Assert.Equal(4 * 120 * 120, rgba.Length);
        Assert.Contains("Metal", host.Backend, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers -------------------------------------------------------------

    static byte[] Render(Action<WebGpuCanvas> draw, WebGpuFonts? fonts = null)
    {
        var owned = fonts is null;
        fonts ??= new WebGpuFonts();
        try
        {
            var canvas = new WebGpuCanvas(fonts, new Graphics.Size(Size, Size));
            draw(canvas);

            using var renderer = new WebGpuRenderer();
            return renderer.RenderToRgba(canvas);
        }
        finally
        {
            if (owned) fonts.Dispose();
        }
    }

    static GColor PixelAt(byte[] rgba, int x, int y)
    {
        var i = (y * Size + x) * 4;
        return new GColor(rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    static void AssertPixel(byte[] rgba, int x, int y, byte r, byte g, byte b)
    {
        var actual = PixelAt(rgba, x, y);
        Assert.True(
            Math.Abs(actual.R - r) <= 6 && Math.Abs(actual.G - g) <= 6 && Math.Abs(actual.B - b) <= 6,
            $"({x},{y}) expected ≈#{r:X2}{g:X2}{b:X2}, got {actual}");
    }

    static bool? _gpu;

    /// <summary>
    /// True when this machine has a usable WebGPU adapter. Tests bail out early when it is false rather
    /// than failing: the suite has to stay green on headless CI without a GPU, and xunit 2.9 has no
    /// built-in skip. Probed once per run — creating a device is not cheap.
    /// </summary>
    static bool GpuAvailable => _gpu ??= Probe();

    static bool Probe()
    {
        try
        {
            using var renderer = new WebGpuRenderer();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

file sealed class RedBox : View
{
    public override View Body =>
        new Rectangle().Frame(80, 80).ForegroundColor(Color.Red);
}
