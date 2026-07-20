using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// Turns a host's raw pointer stream into the engine's gesture grammar — tap, long-press, swipe, and the
/// F1 continuous drag/pinch that <see cref="SkiaBridge.Drag"/> / <see cref="SkiaBridge.Magnify"/> expect.
///
/// Every other backend gets this for free from its toolkit's recognizers (UIKit, Compose, GTK, DOM). The
/// Skia backend draws its own UI, so nothing supplies it — which is why <c>.OnDrag</c>/<c>.OnMagnify</c>
/// used to be inert on Skia even though the engine implemented them: hosts only ever forwarded taps.
/// Rather than each host re-deriving the state machine, they feed this and it calls the bridge.
///
/// Wiring a host is four calls: <see cref="Down"/>, <see cref="Move"/>, <see cref="Up"/> on the pointer
/// events, and <see cref="Poll"/> once per frame (the long-press timer needs a clock). Hosts with a real
/// system pinch recognizer forward it to <see cref="Pinch"/>; hosts without one get trackpad/ctrl-wheel
/// zoom by calling <see cref="PinchDelta"/>.
///
/// All timestamps are seconds from any fixed origin — the host's frame clock is ideal. Nothing here reads
/// a wall clock, so the whole state machine is deterministic and testable.
/// </summary>
public sealed class SkiaPointerRouter
{
    readonly SkiaBridge _bridge;

    /// <summary>Movement (px) tolerated before a press stops counting as a tap/long-press.</summary>
    public float TapSlop { get; set; } = 8;

    /// <summary>Hold duration (s) with no movement that fires a long-press.</summary>
    public double LongPressSeconds { get; set; } = 0.5;

    /// <summary>Minimum travel (px) for a release to register as a directional swipe.</summary>
    public float SwipeDistance { get; set; } = 40;

    /// <summary>Minimum speed (px/s) for a release to register as a directional swipe rather than a drag.</summary>
    public float SwipeVelocity { get; set; } = 300;

    public SkiaPointerRouter(SkiaBridge bridge) => _bridge = bridge;

    bool _down;
    bool _dragging;         // a node with .OnDrag captured the press
    bool _longPressed;      // fired for this press; suppresses the tap on release
    bool _moved;            // travelled past TapSlop
    SKPoint _start;
    SKPoint _last;          // position at _lastTime; release velocity is measured from it
    double _downTime;
    double _lastTime;

    /// <summary>Pointer pressed. Captures a draggable under the point if there is one.</summary>
    public void Down(SKPoint p, double time)
    {
        _down = true;
        _dragging = false;
        _longPressed = false;
        _moved = false;
        _start = _last = p;
        _downTime = _lastTime = time;

        // Begin the drag eagerly so a press directly on a slider track sets it without waiting for movement
        // (SwiftUI's DragGesture(minimumDistance: 0) semantics, which is what the Controls sliders assume).
        // Returns false when nothing under the point handles .OnDrag — then this press is a tap candidate.
        _dragging = _bridge.Drag(p, GesturePhase.Began, 0, 0, 0, 0);
    }

    /// <summary>Pointer moved. Feeds the live drag, or disqualifies the press from being a tap.</summary>
    public void Move(SKPoint p, double time)
    {
        if (!_down) return;
        if (time > _lastTime) { _last = p; _lastTime = time; }
        if (Distance(p, _start) > TapSlop) _moved = true;

        if (_dragging)
            _bridge.Drag(p, GesturePhase.Changed, p.X - _start.X, p.Y - _start.Y, 0, 0);
    }

    /// <summary>
    /// Pointer released. Ends a live drag (with release velocity for flings), or resolves the press into a
    /// swipe or a tap. A press that already fired a long-press produces neither.
    /// </summary>
    public void Up(SKPoint p, double time)
    {
        if (!_down) return;
        _down = false;

        var (vx, vy) = Velocity(p, time);
        _last = p;
        _lastTime = time;

        if (_dragging)
        {
            _bridge.Drag(p, GesturePhase.Ended, p.X - _start.X, p.Y - _start.Y, vx, vy);
            _dragging = false;
            return;
        }

        if (_longPressed) return;

        // A fast, far-enough flick is a swipe; anything else that stayed put is a tap.
        var dx = p.X - _start.X;
        var dy = p.Y - _start.Y;
        var speed = MathF.Sqrt(vx * vx + vy * vy);
        if (_moved && Distance(p, _start) >= SwipeDistance && speed >= SwipeVelocity)
        {
            var direction = MathF.Abs(dx) > MathF.Abs(dy)
                ? (dx > 0 ? "right" : "left")
                : (dy > 0 ? "down" : "up");
            _bridge.Swipe(_start, direction);
            return;
        }

        if (!_moved) _bridge.DispatchPointer(_start);
    }

    /// <summary>Pointer capture lost (window deactivated, gesture cancelled). Ends any live drag in place.</summary>
    public void Cancel()
    {
        if (_down && _dragging) _bridge.Drag(_last, GesturePhase.Ended, _last.X - _start.X, _last.Y - _start.Y, 0, 0);
        _down = _dragging = false;
    }

    /// <summary>
    /// Call once per frame with the host's clock. Fires the long-press once the pointer has been held past
    /// <see cref="LongPressSeconds"/> without moving. Cheap no-op when no pointer is down.
    /// </summary>
    public void Poll(double time)
    {
        if (!_down || _dragging || _moved || _longPressed) return;
        if (time - _downTime < LongPressSeconds) return;
        _longPressed = true;
        _bridge.LongPress(_start);
    }

    /// <summary>Forward a system pinch recognizer's cumulative scale (1.0 = unchanged).</summary>
    public void Pinch(SKPoint p, GesturePhase phase, float scale) => _bridge.Magnify(p, phase, scale);

    // ---- trackpad / ctrl+wheel zoom for hosts with no pinch recognizer -----
    bool _zooming;
    float _zoomScale = 1;

    /// <summary>
    /// Apply an incremental zoom factor at a point for hosts without a pinch recognizer (ctrl+wheel, or a
    /// trackpad magnification delta). Accumulates into the cumulative scale the engine expects, and
    /// auto-opens the gesture on the first delta — call <see cref="EndPinch"/> when the stream stops.
    /// </summary>
    public void PinchDelta(SKPoint p, float factor)
    {
        if (!_zooming) { _zooming = true; _zoomScale = 1; _bridge.Magnify(p, GesturePhase.Began, 1); }
        _zoomScale = Math.Max(0.05f, _zoomScale * factor);
        _bridge.Magnify(p, GesturePhase.Changed, _zoomScale);
    }

    /// <summary>Close a <see cref="PinchDelta"/> stream.</summary>
    public void EndPinch(SKPoint p)
    {
        if (!_zooming) return;
        _zooming = false;
        _bridge.Magnify(p, GesturePhase.Ended, _zoomScale);
    }

    // Release speed over the final pointer interval. Numerator and denominator must span the same window —
    // measuring displacement from an older sample than the elapsed time overstates the fling.
    (float vx, float vy) Velocity(SKPoint p, double time)
    {
        var dt = time - _lastTime;
        if (dt <= 0.0001) return (0, 0);
        return ((float)((p.X - _last.X) / dt), (float)((p.Y - _last.Y) / dt));
    }

    static float Distance(SKPoint a, SKPoint b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
