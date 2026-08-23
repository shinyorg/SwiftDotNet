using SwiftDotNet;
using SwiftDotNet.Sample;

namespace SampleApp.Godot;

/// <summary>
/// The same sample on the Skia-into-a-texture route, for comparing the two rendering paths side by side.
/// </summary>
/// <remarks>
/// <code>
///   godot --path sample/SampleApp.Godot res://MainSkia.tscn --quit-after 300 -- --shot out.png
/// </code>
/// </remarks>
public partial class SampleRootSkia : SwiftDotNetTextureControl
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
