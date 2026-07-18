namespace SwiftDotNet;

/// <summary>The interpolation curve of an <see cref="AnimationSpec"/> (mirrors SwiftUI's <c>Animation</c> family).</summary>
public enum AnimationCurve { Linear, EaseIn, EaseOut, EaseInOut, Spring }

/// <summary>
/// A backend-neutral animation description. Maps to a real native animation per platform
/// (SwiftUI <c>Animation</c>, Compose <c>tween</c>/<c>spring</c>, CSS <c>transition</c>, …).
/// <see cref="AnimationCurve.Spring"/> is native where available and degrades to the nearest
/// supported curve elsewhere — never throws, never silently no-ops.
/// </summary>
public readonly record struct AnimationSpec(
    AnimationCurve Curve,
    double Duration = 0.3,   // seconds; ignored for a pure spring
    double Delay = 0,
    double? SpringStiffness = null,
    double? SpringDamping = null);

/// <summary>Ergonomic factories for <see cref="AnimationSpec"/> — <c>Anim.EaseInOut(0.25)</c>, <c>Anim.Spring()</c>.</summary>
public static class Anim
{
    public static AnimationSpec Linear(double duration = 0.3) => new(AnimationCurve.Linear, duration);
    public static AnimationSpec EaseIn(double duration = 0.3) => new(AnimationCurve.EaseIn, duration);
    public static AnimationSpec EaseOut(double duration = 0.3) => new(AnimationCurve.EaseOut, duration);
    public static AnimationSpec EaseInOut(double duration = 0.3) => new(AnimationCurve.EaseInOut, duration);

    /// <summary>A physical spring (native on SwiftUI/Compose/WinUI; approximated on Web; degrades to ease-in-out on GTK).</summary>
    public static AnimationSpec Spring(double stiffness = 170, double damping = 26) =>
        new(AnimationCurve.Spring, SpringStiffness: stiffness, SpringDamping: damping);
}

internal static class AnimationTokens
{
    public static string Token(this AnimationCurve c) => c switch
    {
        AnimationCurve.Linear => "linear",
        AnimationCurve.EaseIn => "easeIn",
        AnimationCurve.EaseOut => "easeOut",
        AnimationCurve.Spring => "spring",
        _ => "easeInOut",
    };
}
