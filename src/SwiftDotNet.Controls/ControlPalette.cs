namespace SwiftDotNet.Controls;

/// <summary>
/// A compact semantic palette for the ported controls. The Shiny MAUI controls read Material-style theme
/// tokens (<c>SuccessContainer</c>/<c>OnSuccessContainer</c>/…) via <c>SetDynamicResource</c>; SwiftDotNet's
/// <see cref="SwiftDotNet.Theme"/> is simpler, so we resolve those roles to concrete colors here. This keeps
/// the control library self-contained and gives every control a consistent, tuned look on every backend.
/// </summary>
static class ControlPalette
{
    // (container background, on-container text/foreground, role/border accent) per semantic role.
    public static (SwiftColor Bg, SwiftColor Fg, SwiftColor Accent) Pill(PillType type) => type switch
    {
        PillType.Success  => (Hex("#DCF5E3"), Hex("#0F5132"), Hex("#34C759")),
        PillType.Info     => (Hex("#DCEBFF"), Hex("#0A3A66"), Hex("#007AFF")),
        PillType.Warning  => (Hex("#FFF3D6"), Hex("#6B4E00"), Hex("#FFB300")),
        PillType.Caution  => (Hex("#FFE7D6"), Hex("#7A3A00"), Hex("#FF7A00")),
        PillType.Critical => (Hex("#FFE0DE"), Hex("#7A0C12"), Hex("#FF3B30")),
        _                 => (Hex("#EFEFF4"), Hex("#3C3C43"), Hex("#C7C7CC")),   // None / neutral
    };

    // Accent color for a Badge / status role, used where only a single fill is needed.
    public static SwiftColor Accent(PillType type) => Pill(type).Accent;

    public static SwiftColor Surface => Hex("#FFFFFF");
    public static SwiftColor SurfaceVariant => Hex("#F2F2F7");
    public static SwiftColor OnSurface => Hex("#1C1C1E");
    public static SwiftColor OnSurfaceVariant => Hex("#8E8E93");
    public static SwiftColor Outline => Hex("#C7C7CC");
    public static SwiftColor Shimmer => Hex("#D3D3DC");           // visible against a white surface
    public static SwiftColor ShimmerHighlight => Hex("#F4F4F8");  // the moving highlight band

    static SwiftColor Hex(string hex) => SwiftColor.Hex(hex);
}
