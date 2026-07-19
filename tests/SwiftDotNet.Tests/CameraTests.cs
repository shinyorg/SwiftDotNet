using System.Text;
using System.Text.Json;
using SwiftDotNet;
using SwiftDotNet.Controls;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The camera family core (Plan 2 Wave 6). CameraView is a CustomView like Map — no live camera can run
/// headless, but its prop serialization and the event-channel dispatch grammar are fully testable here.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class CameraTests
{
    [Fact]
    public void CameraView_SerializesFacingFlashAndAnalyzers()
    {
        var node = Render(new CameraView(new State<CameraFacing>(CameraFacing.Front))
            .Flash(CameraFlash.Auto)
            .Analyze(CameraAnalyzers.Barcodes | CameraAnalyzers.Text));
        var props = node.GetProperty("props");
        Assert.Equal("CameraView", node.GetProperty("type").GetString());
        Assert.Equal("front", props.GetProperty("facing").GetString());
        Assert.Equal("auto", props.GetProperty("flash").GetString());
        Assert.Equal("barcode,text", props.GetProperty("analyzers").GetString());
    }

    [Fact]
    public void CameraView_CaptureTrigger_EmitsToken()
    {
        var node = Render(new CameraView(new State<CameraFacing>(CameraFacing.Back), new State<int>(7)));
        Assert.Equal(7, node.GetProperty("props").GetProperty("captureToken").GetDouble());
    }

    [Fact]
    public void CameraView_BarcodeEvent_DecodesFormatAndValue()
    {
        DetectedBarcode? got = null;
        var bridge = new CamBridge();
        SwiftApp.Run(new CamHost(new CameraView(new State<CameraFacing>(CameraFacing.Back)).OnBarcode(b => got = b)), bridge);

        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("https://shiny.dev"));
        bridge.Fire("0", $"barcode:qr:{payload}");

        Assert.NotNull(got);
        Assert.Equal("qr", got!.Format);
        Assert.Equal("https://shiny.dev", got.Value);
    }

    [Fact]
    public void CameraView_FacesAndFocusEvents_Parse()
    {
        int faces = -1;
        (double X, double Y) focus = default;
        var bridge = new CamBridge();
        SwiftApp.Run(new CamHost(new CameraView(new State<CameraFacing>(CameraFacing.Back))
            .OnFaceCount(n => faces = n)
            .OnTapToFocus(p => focus = p)), bridge);

        bridge.Fire("0", "faces:3");
        bridge.Fire("0", "focus:0.25,0.75");

        Assert.Equal(3, faces);
        Assert.Equal((0.25, 0.75), focus);
    }

    static JsonElement Render(View view)
    {
        var bridge = new CamBridge();
        SwiftApp.Run(new CamHost(view), bridge);
        var op = JsonDocument.Parse(bridge.Json!).RootElement.GetProperty("ops").EnumerateArray().First();
        return (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
    }
}

file sealed class CamHost(View child) : View { public override View Body => child; }

file sealed class CamBridge : IBridge
{
    Action<string, string?>? _handler;
    public string? Json { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Render(string json) => Json = json;
    public void Fire(string id, string? value) => _handler!(id, value);
}
