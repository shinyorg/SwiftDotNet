namespace SwiftDotNet.Controls;

/// <summary>A country entry for <see cref="CountryPicker"/> — flag emoji, name, ISO code, and dial code.</summary>
public sealed record Country(string Flag, string Name, string Iso, string DialCode);

/// <summary>
/// A starter country list for <see cref="CountryPicker"/> (a representative subset — extend
/// <see cref="All"/> or pass your own list to the picker for the full set).
/// </summary>
public static class CountryData
{
    public static readonly IReadOnlyList<Country> All = new[]
    {
        new Country("🇺🇸", "United States", "US", "+1"),
        new Country("🇨🇦", "Canada", "CA", "+1"),
        new Country("🇬🇧", "United Kingdom", "GB", "+44"),
        new Country("🇮🇪", "Ireland", "IE", "+353"),
        new Country("🇫🇷", "France", "FR", "+33"),
        new Country("🇩🇪", "Germany", "DE", "+49"),
        new Country("🇪🇸", "Spain", "ES", "+34"),
        new Country("🇵🇹", "Portugal", "PT", "+351"),
        new Country("🇮🇹", "Italy", "IT", "+39"),
        new Country("🇳🇱", "Netherlands", "NL", "+31"),
        new Country("🇧🇪", "Belgium", "BE", "+32"),
        new Country("🇨🇭", "Switzerland", "CH", "+41"),
        new Country("🇦🇹", "Austria", "AT", "+43"),
        new Country("🇸🇪", "Sweden", "SE", "+46"),
        new Country("🇳🇴", "Norway", "NO", "+47"),
        new Country("🇩🇰", "Denmark", "DK", "+45"),
        new Country("🇫🇮", "Finland", "FI", "+358"),
        new Country("🇵🇱", "Poland", "PL", "+48"),
        new Country("🇨🇿", "Czechia", "CZ", "+420"),
        new Country("🇬🇷", "Greece", "GR", "+30"),
        new Country("🇷🇺", "Russia", "RU", "+7"),
        new Country("🇺🇦", "Ukraine", "UA", "+380"),
        new Country("🇹🇷", "Türkiye", "TR", "+90"),
        new Country("🇮🇳", "India", "IN", "+91"),
        new Country("🇨🇳", "China", "CN", "+86"),
        new Country("🇯🇵", "Japan", "JP", "+81"),
        new Country("🇰🇷", "South Korea", "KR", "+82"),
        new Country("🇸🇬", "Singapore", "SG", "+65"),
        new Country("🇭🇰", "Hong Kong", "HK", "+852"),
        new Country("🇦🇺", "Australia", "AU", "+61"),
        new Country("🇳🇿", "New Zealand", "NZ", "+64"),
        new Country("🇧🇷", "Brazil", "BR", "+55"),
        new Country("🇦🇷", "Argentina", "AR", "+54"),
        new Country("🇲🇽", "Mexico", "MX", "+52"),
        new Country("🇿🇦", "South Africa", "ZA", "+27"),
        new Country("🇳🇬", "Nigeria", "NG", "+234"),
        new Country("🇪🇬", "Egypt", "EG", "+20"),
        new Country("🇦🇪", "United Arab Emirates", "AE", "+971"),
        new Country("🇸🇦", "Saudi Arabia", "SA", "+966"),
        new Country("🇮🇱", "Israel", "IL", "+972"),
    };
}
