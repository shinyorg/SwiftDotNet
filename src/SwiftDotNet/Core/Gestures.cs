using System.Globalization;

namespace SwiftDotNet;

/// <summary>The lifecycle phase of a continuous gesture (mirrors UIKit/Compose gesture phases).</summary>
public enum GesturePhase { Began, Changed, Ended }

/// <summary>
/// A snapshot of a continuous drag/pan gesture (F1). <see cref="Translation"/> is the cumulative movement
/// since the gesture began; <see cref="Location"/> is the current pointer position within the view;
/// <see cref="Velocity"/> is the release speed (meaningful on <see cref="GesturePhase.Ended"/>) for
/// inertial/fling handling. All values are in points.
/// </summary>
public readonly record struct DragInfo(
    GesturePhase Phase,
    double TranslationX, double TranslationY,
    double LocationX, double LocationY,
    double VelocityX, double VelocityY)
{
    public (double X, double Y) Translation => (TranslationX, TranslationY);
    public (double X, double Y) Location => (LocationX, LocationY);
    public (double X, double Y) Velocity => (VelocityX, VelocityY);

    // Wire grammar (single event-channel string; owned by this modifier, no protocol change):
    //   "<phase>;<tx>,<ty>;<lx>,<ly>;<vx>,<vy>"   phase ∈ b|c|e
    internal static bool TryParse(string? value, out DragInfo info)
    {
        info = default;
        if (value is null) return false;
        var parts = value.Split(';');
        if (parts.Length != 4) return false;
        var phase = parts[0] switch { "b" => GesturePhase.Began, "e" => GesturePhase.Ended, _ => GesturePhase.Changed };
        if (!Pair(parts[1], out var tx, out var ty)) return false;
        if (!Pair(parts[2], out var lx, out var ly)) return false;
        if (!Pair(parts[3], out var vx, out var vy)) return false;
        info = new DragInfo(phase, tx, ty, lx, ly, vx, vy);
        return true;
    }

    static bool Pair(string s, out double a, out double b)
    {
        a = b = 0;
        var comma = s.IndexOf(',');
        if (comma < 0) return false;
        return D(s[..comma], out a) && D(s[(comma + 1)..], out b);
    }

    static bool D(string s, out double v) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}

sealed class OnDragModifier : Modifier
{
    readonly Action<DragInfo> _handler;
    readonly double _minimumDistance;
    public OnDragModifier(Action<DragInfo> handler, double minimumDistance) { _handler = handler; _minimumDistance = minimumDistance; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        ctx.RegisterAction(path, v => { if (DragInfo.TryParse(v, out var info)) _handler(info); });
        return new() { ["type"] = "onDrag", ["event"] = path, ["amount"] = _minimumDistance };
    }
}

sealed class OnMagnifyModifier : Modifier
{
    readonly Action<double> _handler;
    public OnMagnifyModifier(Action<double> handler) { _handler = handler; }
    internal override Dictionary<string, object> Serialize(RenderContext ctx, string path)
    {
        // Value = the cumulative scale factor (1.0 = unchanged) as an invariant-culture string.
        ctx.RegisterAction(path, v =>
        {
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale)) _handler(scale);
        });
        return new() { ["type"] = "onMagnify", ["event"] = path };
    }
}
