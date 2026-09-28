using GColor = SwiftDotNet.Graphics.Color;

namespace SwiftDotNet;

/// <summary>
/// An image shown in a car template. The renderers turn it into a <c>UIImage</c> (CarPlay) or a
/// <c>CarIcon</c> (Android Auto).
/// </summary>
public abstract record CarImage
{
    /// <summary>Optional tint, applied on both platforms. Any DSL colour token works (<c>Color.Green</c>, <c>Color.Hex("#34C759")</c>).</summary>
    public SwiftColor? Tint { get; init; }

    /// <summary>
    /// A system/vector icon. On iOS <paramref name="iosSymbol"/> is an SF Symbol name. On Android
    /// <paramref name="androidDrawable"/> is a drawable resource name; when omitted, the SF Symbol name is used
    /// with <c>.</c> and <c>-</c> replaced by <c>_</c> (<c>"bolt.car"</c> → <c>bolt_car</c>).
    /// </summary>
    public static CarImage Symbol(string iosSymbol, string? androidDrawable = null) => new CarSymbolImage(iosSymbol, androidDrawable);

    /// <summary>A bundled image (iOS asset/bundle resource) or an Android drawable/mipmap, looked up by name.</summary>
    public static CarImage Resource(string name) => new CarResourceImage(name);

    /// <summary>Encoded image bytes (PNG/JPEG).</summary>
    public static CarImage FromBytes(byte[] data) => new CarBytesImage(data);

    public CarImage WithTint(SwiftColor tint) => this with { Tint = tint };
}

public sealed record CarSymbolImage(string IosSymbol, string? AndroidDrawable) : CarImage
{
    public string AndroidResourceName => AndroidDrawable ?? IosSymbol.Replace('.', '_').Replace('-', '_');
}

public sealed record CarResourceImage(string Name) : CarImage;

public sealed record CarBytesImage(byte[] Data) : CarImage;

/// <summary>
/// Resolves DSL colour tokens to concrete RGBA for the car renderers. Head units have no notion of our
/// semantic tokens, so the token is resolved here once — with the same table the self-drawing engine uses —
/// and both platforms receive identical colours.
/// </summary>
public static class CarColors
{
    /// <summary>The concrete colour for a token. Car screens are treated as light-appearance for semantic tokens.</summary>
    public static GColor Resolve(SwiftColor color) => Graphics.Theme.Color(color.Value, dark: false);
}
