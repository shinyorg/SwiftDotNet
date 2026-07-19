namespace SwiftDotNet.Controls;

/// <summary>
/// A determinate (0–1 value) or indeterminate progress bar — ported from Shiny's <c>ProgressBar</c>.
/// Renders over the built-in <see cref="ProgressView"/> so it uses each platform's native bar. (A fully
/// custom-styled fill bar awaits proportional sizing — Plan-1 F11.)
/// </summary>
public sealed class ProgressBar : View
{
    readonly double? _value;
    string? _label;

    /// <summary>Determinate progress in 0–1, or null for an indeterminate/animating bar.</summary>
    public ProgressBar(double? value = null) => _value = value;

    public ProgressBar Label(string label) { _label = label; return this; }

    public override View Body =>
        _value is { } v ? new ProgressView(Math.Clamp(v, 0, 1), _label) : new ProgressView(_label);
}
