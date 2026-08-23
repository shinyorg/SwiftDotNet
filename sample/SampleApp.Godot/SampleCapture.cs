using Godot;
using SwiftDotNet;
using SwiftDotNet.Graphics;

namespace SampleApp.Godot;

/// <summary>
/// The sample's screenshot harness, shared by both Godot heads.
/// </summary>
/// <remarks>
/// Not something an app needs — it exists so the two rendering routes (Godot-native and Skia-into-a-texture)
/// can be captured under identical conditions and compared, and so CI has a non-interactive check.
/// </remarks>
public sealed class SampleCapture
{
    readonly SwiftDotNetControl _control;
    readonly string? _shot;
    readonly string? _tap;
    int _frames;

    public SampleCapture(SwiftDotNetControl control)
    {
        _control = control;
        var args = OS.GetCmdlineUserArgs();
        _shot = Argument(args, "--shot");
        _tap = Argument(args, "--tap");
    }

    /// <summary>True when the process was started to capture rather than to be used.</summary>
    public bool Active => _shot is not null;

    /// <summary>Drives the capture. Call once per frame from <c>_Process</c>.</summary>
    public void Step()
    {
        if (_shot is null) return;
        _frames++;

        // Tap a node by id once layout exists, then let the patch land before capturing — this walks the
        // whole loop (hit-test → Emit → state → diff → repaint), not just the paint pass.
        if (_frames == 5 && _tap is not null)
        {
            if (_control.Bridge.TryGetFrame(_tap, out var frame))
                _control.Bridge.DispatchPointer(new Point(frame.MidX, frame.MidY));
            else
                GD.PrintErr($"no node with id '{_tap}' in the current layout");
        }

        if (_frames == 15) Capture();
    }

    async void Capture()
    {
        // The viewport texture is only complete once the frame has been drawn, so wait for the signal that
        // says so rather than reading a half-built frame.
        await _control.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = _control.GetViewport().GetTexture().GetImage();
        image.SavePng(_shot);
        GD.Print($"wrote {_shot} ({image.GetWidth()}x{image.GetHeight()})");
        _control.GetTree().Quit();
    }

    static string? Argument(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
