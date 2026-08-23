using SwiftDotNet;
using SwiftDotNet.Sample;

namespace SampleApp.Godot;

/// <summary>
/// Godot head for the shared sample UI — the same MAUI-style flyout every other backend renders, drawn
/// with Godot's own 2D renderer (no SkiaSharp).
/// </summary>
/// <remarks>
/// <para>Apart from the screenshot harness, this is the whole integration a real game writes: subclass
/// <see cref="SwiftDotNetControl"/> and return a root view.</para>
///
/// <code>
///   godot --path sample/SampleApp.Godot                                       # window
///   godot --path sample/SampleApp.Godot --quit-after 300 -- --shot out.png    # capture and exit
/// </code>
///
/// <para><see cref="SampleRootSkia"/> is the same sample on the Skia-into-a-texture route, for comparing
/// the two.</para>
/// </remarks>
public partial class SampleRoot : SwiftDotNetControl
{
    SampleCapture? _capture;

    protected override View BuildRoot()
    {
        var app = SwiftProgram.CreateSwiftApp();
        Services = app.Services;
        _capture = new SampleCapture(this);
        return app.CreateRoot();
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        _capture?.Step();
    }
}
