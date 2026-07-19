namespace SwiftDotNet.Controls;

/// <summary>Barcode symbologies the camera's frame analyzer can detect (native Vision / ML Kit).</summary>
public enum BarcodeFormat
{
    Unknown,
    QR,
    Aztec,
    DataMatrix,
    Pdf417,
    Code128,
    Code39,
    Code93,
    Codabar,
    Ean8,
    Ean13,
    UpcA,
    UpcE,
    Itf,
}

static class BarcodeFormats
{
    /// <summary>
    /// Maps a native symbology token to a <see cref="BarcodeFormat"/>. Accepts Apple Vision raw values
    /// (<c>VNBarcodeSymbologyQR</c>, <c>org.iso.QRCode</c>, …) and Android ML Kit names (<c>QR_CODE</c>, …),
    /// case-insensitively, so the same wire string works across backends.
    /// </summary>
    public static BarcodeFormat Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return BarcodeFormat.Unknown;
        var s = raw.ToUpperInvariant();
        // Order matters: check the more specific tokens before the shorter substrings.
        if (Has(s, "QR")) return BarcodeFormat.QR;
        if (Has(s, "AZTEC")) return BarcodeFormat.Aztec;
        if (Has(s, "DATAMATRIX", "DATA_MATRIX")) return BarcodeFormat.DataMatrix;
        if (Has(s, "PDF417")) return BarcodeFormat.Pdf417;
        if (Has(s, "CODE128", "CODE_128")) return BarcodeFormat.Code128;
        if (Has(s, "CODE93", "CODE_93")) return BarcodeFormat.Code93;
        if (Has(s, "CODE39", "CODE_39")) return BarcodeFormat.Code39;
        if (Has(s, "CODABAR")) return BarcodeFormat.Codabar;
        if (Has(s, "EAN13", "EAN_13", "EAN-13")) return BarcodeFormat.Ean13;
        if (Has(s, "EAN8", "EAN_8", "EAN-8")) return BarcodeFormat.Ean8;
        if (Has(s, "UPCE", "UPC_E", "UPC-E")) return BarcodeFormat.UpcE;
        if (Has(s, "UPCA", "UPC_A", "UPC-A", "UPC")) return BarcodeFormat.UpcA;
        if (Has(s, "ITF", "INTERLEAVED")) return BarcodeFormat.Itf;
        return BarcodeFormat.Unknown;
    }

    static bool Has(string s, params string[] needles)
    {
        foreach (var n in needles) if (s.Contains(n, StringComparison.Ordinal)) return true;
        return false;
    }
}
