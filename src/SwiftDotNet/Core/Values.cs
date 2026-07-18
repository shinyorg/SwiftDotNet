namespace SwiftDotNet;

/// <summary>Mirrors SwiftUI's <c>Font</c> semantic styles. Value is the token the Swift host maps back to a real <c>Font</c>.</summary>
public readonly struct SwiftFont
{
    public string Value { get; }
    SwiftFont(string value) => Value = value;

    public static SwiftFont LargeTitle => new("largeTitle");
    public static SwiftFont Title => new("title");
    public static SwiftFont Headline => new("headline");
    public static SwiftFont Body => new("body");
    public static SwiftFont Caption => new("caption");
}

/// <summary>Mirrors a subset of SwiftUI's semantic <c>Color</c>s.</summary>
public readonly struct SwiftColor
{
    public string Value { get; }
    SwiftColor(string value) => Value = value;

    public static SwiftColor Primary => new("primary");
    public static SwiftColor Secondary => new("secondary");
    public static SwiftColor Red => new("red");
    public static SwiftColor Green => new("green");
    public static SwiftColor Blue => new("blue");
    public static SwiftColor Accent => new("accentColor");

    /// <summary>A color from a hex string, e.g. <c>SwiftColor.Hex("#F2F2F7")</c>.</summary>
    public static SwiftColor Hex(string hex) => new(hex);
}

/// <summary>Convenience so call sites read like SwiftUI: <c>.Font(Font.LargeTitle)</c>.</summary>
public static class Font
{
    public static SwiftFont LargeTitle => SwiftFont.LargeTitle;
    public static SwiftFont Title => SwiftFont.Title;
    public static SwiftFont Headline => SwiftFont.Headline;
    public static SwiftFont Body => SwiftFont.Body;
    public static SwiftFont Caption => SwiftFont.Caption;
}

public static class Color
{
    public static SwiftColor Primary => SwiftColor.Primary;
    public static SwiftColor Secondary => SwiftColor.Secondary;
    public static SwiftColor Red => SwiftColor.Red;
    public static SwiftColor Green => SwiftColor.Green;
    public static SwiftColor Blue => SwiftColor.Blue;
    public static SwiftColor Accent => SwiftColor.Accent;
    public static SwiftColor Hex(string hex) => SwiftColor.Hex(hex);
}
