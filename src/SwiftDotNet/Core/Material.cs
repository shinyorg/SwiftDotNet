namespace SwiftDotNet;

/// <summary>
/// Frosted-glass material thickness (mirrors SwiftUI's <c>Material</c> family). Each maps to a backdrop
/// blur + translucent tint where the backend supports it (Web <c>backdrop-filter</c>, SwiftUI
/// <c>Material</c>), and degrades to a translucent tint everywhere else (F6).
/// </summary>
public enum MaterialStyle { UltraThin, Thin, Regular, Thick }

static class MaterialTokens
{
    public static string Token(this MaterialStyle s) => s switch
    {
        MaterialStyle.UltraThin => "ultraThin",
        MaterialStyle.Thin => "thin",
        MaterialStyle.Thick => "thick",
        _ => "regular",
    };

    /// <summary>Backdrop blur radius (points) and tint opacity (0–1) for the tint fallback, per style.</summary>
    public static (double Blur, double TintOpacity) Params(string token) => token switch
    {
        "ultraThin" => (8, 0.55),
        "thin" => (14, 0.65),
        "thick" => (30, 0.85),
        _ => (20, 0.75),
    };
}
