using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>The CameraView — its own sample: a live preview with barcode scanning (native renderer required).</summary>
public sealed class CameraSample : View
{
    readonly State<CameraFacing> _facing = State(CameraFacing.Back);
    readonly State<int> _capture = State(0);

    public override View Body =>
        new ScrollView(new VStack(
            new Text("Live camera preview on camera-capable backends (AVFoundation on Apple, CameraX on Android). " +
                     "Shows the ⚠️ placeholder where no camera renderer is registered.").Font(Font.Caption),

            new CameraView(_facing, _capture)
                .Analyze(CameraAnalyzers.Barcodes)
                .OnBarcode(b => Toast.Show($"{b.Kind}: {b.Value}"))
                .OnError(msg => Toast.Show(msg))
                .Frame(height: 320),

            new HStack(
                new Button("Flip", () => _facing.Value = _facing.Value == CameraFacing.Back ? CameraFacing.Front : CameraFacing.Back),
                new Button("Capture", () => _capture.Value++)
            ).Spacing(12)
        ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Camera");
}
