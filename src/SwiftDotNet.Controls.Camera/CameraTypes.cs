namespace SwiftDotNet.Controls;

/// <summary>Which physical camera the <see cref="CameraView"/> previews.</summary>
public enum CameraFacing { Back, Front }

/// <summary>The capture flash/torch mode.</summary>
public enum CameraFlash { Off, On, Auto }

/// <summary>A photo captured by <see cref="CameraView"/> (encoded image bytes, typically JPEG/PNG).</summary>
public sealed record CameraPhoto(byte[] Data);

/// <summary>A barcode/QR detected in the live preview (its decoded value and symbology).</summary>
public sealed record DetectedBarcode(string Value, string Format)
{
    /// <summary>The symbology as a normalized <see cref="BarcodeFormat"/> (mapped from the native <see cref="Format"/> token).</summary>
    public BarcodeFormat Kind => BarcodeFormats.Parse(Format);
}

/// <summary>Which live frame analyzers to run on the preview (native Vision / ML Kit).</summary>
[Flags]
public enum CameraAnalyzers
{
    None = 0,
    Barcodes = 1,
    Faces = 2,
    Text = 4,
    Motion = 8,
}
