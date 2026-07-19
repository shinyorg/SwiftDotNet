using System.Globalization;

namespace SwiftDotNet.Controls;

/// <summary>
/// A live camera preview — ported from Shiny's <c>CameraView</c> (Plan 2 Wave 6). It's a
/// <see cref="CustomView"/> exactly like <see cref="Map"/>: it emits a <c>"CameraView"</c> node whose
/// native renderer (AVFoundation on Apple, CameraX on Android, MediaCapture on Windows, getUserMedia on
/// Web) hosts the real preview; a backend with no camera renderer registered shows the standard ⚠️
/// placeholder. Frame analyzers (barcode/face/text/motion) run natively (Vision / ML Kit) and their
/// results — plus captured photos and tap-to-focus — flow back over the single event channel with a
/// <c>kind:body</c> grammar, the same shortcut Map uses.
///
/// The camera family combines Shiny's separate <c>Camera.Barcode</c>/<c>.Face</c>/<c>.Ocr</c>/<c>.Motion</c>
/// packages into one <see cref="CameraAnalyzers"/> flag set here, since in SwiftDotNet the detection is
/// native and only the results cross the bridge.
/// </summary>
public sealed class CameraView : CustomView
{
    readonly State<CameraFacing> _facing;
    readonly State<int>? _captureTrigger;
    CameraFlash _flash = CameraFlash.Off;
    CameraAnalyzers _analyzers = CameraAnalyzers.None;

    Action<CameraPhoto>? _onPhoto;
    Action<DetectedBarcode>? _onBarcode;
    Action<int>? _onFaceCount;
    Action<string>? _onText;
    Action<(double X, double Y)>? _onTapToFocus;
    Action<string>? _onError;

    /// <param name="facing">Which camera to preview (two-way: write it back to switch cameras).</param>
    /// <param name="captureTrigger">Bump this <c>State</c>'s value from your UI to request a still capture;
    /// the photo arrives on <see cref="OnPhoto"/>.</param>
    public CameraView(State<CameraFacing> facing, State<int>? captureTrigger = null)
    {
        _facing = facing;
        _captureTrigger = captureTrigger;
    }

    protected override string TypeName => "CameraView";

    public CameraView Flash(CameraFlash mode) { _flash = mode; return this; }

    /// <summary>Enable native live-frame analyzers (barcode/face/text/motion).</summary>
    public CameraView Analyze(CameraAnalyzers analyzers) { _analyzers = analyzers; return this; }

    /// <summary>Fires with the captured still after <c>captureTrigger</c> is bumped.</summary>
    public CameraView OnPhoto(Action<CameraPhoto> handler) { _onPhoto = handler; return this; }

    /// <summary>Fires when a barcode/QR is detected (requires <see cref="CameraAnalyzers.Barcodes"/>).</summary>
    public CameraView OnBarcode(Action<DetectedBarcode> handler) { _onBarcode = handler; return this; }

    /// <summary>Fires with the number of faces in frame (requires <see cref="CameraAnalyzers.Faces"/>).</summary>
    public CameraView OnFaceCount(Action<int> handler) { _onFaceCount = handler; return this; }

    /// <summary>Fires with recognized text (requires <see cref="CameraAnalyzers.Text"/>).</summary>
    public CameraView OnText(Action<string> handler) { _onText = handler; return this; }

    /// <summary>Fires with the normalized (0–1) tap point the user tapped to focus.</summary>
    public CameraView OnTapToFocus(Action<(double X, double Y)> handler) { _onTapToFocus = handler; return this; }

    /// <summary>Fires with a message if the camera fails to start or capture.</summary>
    public CameraView OnError(Action<string> handler) { _onError = handler; return this; }

    protected override void Configure(CustomNode node)
    {
        node.Prop("facing", _facing.Value == CameraFacing.Front ? "front" : "back");
        node.Prop("flash", _flash switch { CameraFlash.On => "on", CameraFlash.Auto => "auto", _ => "off" });
        node.Prop("analyzers", AnalyzerCsv(_analyzers));
        if (_captureTrigger is not null) node.Prop("captureToken", _captureTrigger.Value);
        node.OnEvent(Dispatch);
    }

    static string AnalyzerCsv(CameraAnalyzers a)
    {
        var parts = new List<string>(4);
        if (a.HasFlag(CameraAnalyzers.Barcodes)) parts.Add("barcode");
        if (a.HasFlag(CameraAnalyzers.Faces)) parts.Add("face");
        if (a.HasFlag(CameraAnalyzers.Text)) parts.Add("text");
        if (a.HasFlag(CameraAnalyzers.Motion)) parts.Add("motion");
        return string.Join(",", parts);
    }

    // Event value grammar (owned by CameraView; base64 for payloads that can contain arbitrary chars):
    //   "photo:<base64>"                 → OnPhoto
    //   "barcode:<format>:<base64value>" → OnBarcode
    //   "faces:<int>"                    → OnFaceCount
    //   "text:<base64>"                  → OnText
    //   "focus:<x>,<y>"                  → OnTapToFocus (normalized 0–1)
    //   "error:<base64>"                 → OnError
    void Dispatch(string? value)
    {
        if (value is null) return;
        var colon = value.IndexOf(':');
        if (colon < 0) return;
        var kind = value[..colon];
        var body = value[(colon + 1)..];

        switch (kind)
        {
            case "photo" when _onPhoto is not null && TryB64(body, out var bytes):
                _onPhoto(new CameraPhoto(bytes));
                break;
            case "barcode" when _onBarcode is not null:
                var sep = body.IndexOf(':');
                if (sep > 0 && TryB64(body[(sep + 1)..], out var raw))
                    _onBarcode(new DetectedBarcode(System.Text.Encoding.UTF8.GetString(raw), body[..sep]));
                break;
            case "faces" when _onFaceCount is not null && int.TryParse(body, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n):
                _onFaceCount(n);
                break;
            case "text" when _onText is not null && TryB64(body, out var textBytes):
                _onText(System.Text.Encoding.UTF8.GetString(textBytes));
                break;
            case "focus" when _onTapToFocus is not null:
                var comma = body.IndexOf(',');
                if (comma > 0 && D(body[..comma]) is { } x && D(body[(comma + 1)..]) is { } y)
                    _onTapToFocus((x, y));
                break;
            case "error" when _onError is not null && TryB64(body, out var errBytes):
                _onError(System.Text.Encoding.UTF8.GetString(errBytes));
                break;
        }
    }

    static bool TryB64(string s, out byte[] bytes)
    {
        try { bytes = Convert.FromBase64String(s); return true; }
        catch { bytes = Array.Empty<byte>(); return false; }
    }

    static double? D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
