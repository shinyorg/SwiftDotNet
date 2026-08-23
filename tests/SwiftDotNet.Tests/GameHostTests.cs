using SkiaSharp;
using SwiftDotNet.Graphics;
using Xunit;
using GraphicsColor = SwiftDotNet.Graphics.Color;

namespace SwiftDotNet.Tests;

/// <summary>
/// The engine-level pieces the game-engine hosts (MonoGame, Godot, Unity) depend on.
/// </summary>
/// <remarks>
/// Those backends cannot be exercised here — one needs a game loop, one needs the Godot runtime — but the
/// two things they added to the shared engine can be, and they are exactly the parts whose breakage would
/// be invisible until a HUD renders as an opaque slab over someone's scene.
/// </remarks>
[Collection(nameof(SwiftAppSerial))]
public class GameHostTests
{
    const int W = 200, H = 200;

    [Fact]
    public void DefaultClear_PaintsTheThemeBackground()
    {
        var pixel = RenderCorner(clear: null, dark: false);

        Assert.Equal(255, pixel.Alpha);
        Assert.Equal(Graphics.Theme.Background(false).R, pixel.Red);
    }

    [Fact]
    public void TransparentClearColor_LeavesTheSceneBehindVisible()
    {
        // The HUD case: everything the UI did not draw must stay untouched, or a game gets a grey slab
        // where it expected an overlay.
        var pixel = RenderCorner(clear: new GraphicsColor(0, 0, 0, 0), dark: false);

        Assert.Equal(0, pixel.Alpha);
    }

    [Fact]
    public void ClearColor_IsHonoredInDarkModeToo()
    {
        // Regression guard for the obvious mis-fix — reading ClearColor only on the light path.
        var pixel = RenderCorner(clear: new GraphicsColor(0, 0, 0, 0), dark: true);

        Assert.Equal(0, pixel.Alpha);
    }

    [Fact]
    public void FrameLoopSyncContext_RunsPostedWorkOnTheDrainingThread()
    {
        var context = new FrameLoopSyncContext();
        var ran = 0;
        var thread = -1;

        // Posting must not run anything: a game loop drains once per frame, between input and paint.
        context.Post(_ => { ran++; thread = Environment.CurrentManagedThreadId; }, null);
        Assert.Equal(0, ran);

        context.Drain();
        Assert.Equal(1, ran);
        Assert.Equal(Environment.CurrentManagedThreadId, thread);

        // Drain is idempotent — the queue is consumed, not replayed.
        context.Drain();
        Assert.Equal(1, ran);
    }

    [Fact]
    public void FrameLoopSyncContext_RunsWorkQueuedFromAnotherThread()
    {
        var context = new FrameLoopSyncContext();
        var ran = false;

        var poster = new Thread(() => context.Post(_ => ran = true, null));
        poster.Start();
        poster.Join();

        Assert.False(ran);
        context.Drain();
        Assert.True(ran);
    }

    /// <summary>Renders an empty scene and samples a pixel nothing draws over.</summary>
    static SKColor RenderCorner(GraphicsColor? clear, bool dark)
    {
        var bridge = new SkiaBridge { ClearColor = clear };
        SwiftApp.Run(new Empty(), bridge);

        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        surface.Canvas.Clear(SKColors.Transparent);
        bridge.Paint(surface.Canvas, new SKSize(W, H), dark);

        using var image = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(W - 1, H - 1);
    }
}

file sealed class Empty : View
{
    public override View? Body => new ZStack();
}
