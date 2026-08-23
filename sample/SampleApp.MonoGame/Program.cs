using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using SwiftDotNet.Sample;
using SwiftDotNet.Sample.Skia;
using XnaColor = Microsoft.Xna.Framework.Color;   // Core owns the `Color` token class

// MonoGame head for the shared sample UI — the same MAUI-style flyout every other backend renders, drawn
// by the self-drawing engine inside a MonoGame game loop.
//
//   dotnet run --project sample/SampleApp.MonoGame                 → window
//   dotnet run --project sample/SampleApp.MonoGame -- --shot out.png → render N frames, save the back
//                                                                     buffer, exit (the CI check)

SkiaSampleRenderers.RegisterAll();   // the Map CustomView's renderer, same as every Skia head

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var shot = Arg("--shot");
var tap = Arg("--tap");   // a node id to tap before capturing — proves the pointer path, not just paint

var swiftApp = SwiftProgram.CreateSwiftApp();
using var game = new SampleGame(swiftApp, shot, tap) { Title = "SwiftDotNet · MonoGame" };
game.Run();

/// <summary>
/// The sample head. Everything except <see cref="Draw"/> is the stock <see cref="SwiftDotNetGame"/>
/// wiring; the override exists only so the screenshot mode can prove a real frame reached the back buffer.
/// </summary>
file sealed class SampleGame : SwiftDotNetGame
{
    readonly SwiftDotNetApp _app;
    readonly string? _shot;
    readonly string? _tap;
    int _frames;

    public SampleGame(SwiftDotNetApp app, string? shot, string? tap)
    {
        _app = app;
        _shot = shot;
        _tap = tap;
        Ui.Services = app.Services;
    }

    protected override View BuildRoot() => _app.CreateRoot();

    protected override void Draw(GameTime gameTime)
    {
        base.Draw(gameTime);
        if (_shot is null) return;

        // A few frames in: the first Draw allocates the surface, and letting the loop settle means the
        // capture is of a steady-state frame rather than the one that built the texture.
        _frames++;

        // Tap a node by id once layout exists, then let the resulting patch land before capturing: this
        // walks the whole loop (hit-test → Emit → state → diff → repaint), not just the paint pass.
        if (_frames == 5 && _tap is not null)
        {
            if (!Ui.Bridge.TryGetFrame(_tap, out var frame))
            {
                Console.Error.WriteLine($"no node with id '{_tap}' in the current layout");
                Exit();
                return;
            }
            Ui.Bridge.DispatchPointer(new SKPoint(frame.MidX, frame.MidY));
        }

        if (_frames < 15) return;

        var w = GraphicsDevice.PresentationParameters.BackBufferWidth;
        var h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        var pixels = new XnaColor[w * h];
        GraphicsDevice.GetBackBufferData(pixels);

        using var texture = new Texture2D(GraphicsDevice, w, h);
        texture.SetData(pixels);
        using var stream = File.Create(_shot);
        texture.SaveAsPng(stream, w, h);

        Console.WriteLine($"wrote {Path.GetFullPath(_shot)} ({w}x{h})");
        Exit();
    }
}
