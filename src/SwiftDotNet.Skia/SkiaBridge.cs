using System.Globalization;
using System.Text.Json;
using SkiaSharp;

namespace SwiftDotNet;

/// <summary>
/// The SkiaSharp implementation of <see cref="IBridge"/> — a pure-C# retained-mode interpreter that
/// draws the UI itself. Like the GTK backend it keeps a scene tree keyed by structural node id and
/// applies <c>replace</c>/<c>updateProps</c>/<c>setChildren</c> patches in place; unlike GTK there is
/// no native widget layer, so this owns layout, paint, and hit-testing against an <see cref="SKCanvas"/>.
///
/// A host (a windowed <c>SKCanvasView</c>, or the headless <see cref="SkiaImageHost"/>) supplies the
/// canvas + size + pointer events and listens on <see cref="Invalidate"/> to redraw after a patch.
/// </summary>
public sealed class SkiaBridge : IBridge
{
    Action<string, string?>? _handler;
    SkiaNode? _root;
    SKSize _lastSize;

    /// <summary>Active NavigationStacks during a build pass (innermost on top) so links can capture their owner.</summary>
    internal Stack<SkiaNode> NavStack { get; } = new();

    /// <summary>Node id of the control with keyboard focus (a text field/editor), or null.</summary>
    public string? FocusedId { get; internal set; }

    /// <summary>Raised whenever the scene changes (a patch was applied); the host should repaint.</summary>
    public event Action? Invalidate;

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    /// <summary>Raise an event as if it came from a control (what hit-testing / recognizers call).</summary>
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);

    public void Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            switch (op.GetProperty("op").GetString())
            {
                case "replace":
                    _root = SkiaNode.Build(op.GetProperty("node"), this);
                    break;
                case "updateProps":
                    Find(op.GetProperty("id").GetString()!)?.UpdateProps(op.GetProperty("props"), op.GetProperty("modifiers"));
                    break;
                case "setChildren":
                    Find(op.GetProperty("id").GetString()!)?.SetChildren(op.GetProperty("children"));
                    break;
            }
        }
        Invalidate?.Invoke();
    }

    /// <summary>Lay out then paint the current scene into <paramref name="canvas"/> filling <paramref name="size"/>.</summary>
    public void Paint(SKCanvas canvas, SKSize size, bool dark)
    {
        canvas.Clear(SkiaTheme.Background(dark));
        if (_root is null) return;
        _lastSize = size;
        _root.Measure(size);
        _root.Arrange(new SKRect(0, 0, size.Width, size.Height));
        _root.Paint(canvas, dark);

        // Overlays (Sheet/Alert/Menu/pushed nav) paint full-window on top, post-order so an outer
        // presentation lands above an inner one.
        var window = new SKRect(0, 0, size.Width, size.Height);
        foreach (var n in Overlays(_root)) n.PaintOverlay(canvas, window, dark);
    }

    /// <summary>Dispatch a pointer tap; returns true if a node handled it (host should then repaint).</summary>
    public bool DispatchPointer(SKPoint point)
    {
        if (_root is null) return false;
        var window = new SKRect(0, 0, _lastSize.Width, _lastSize.Height);
        // Topmost overlay first (reverse of paint order).
        foreach (var n in OverlaysTopFirst(_root))
            if (n.HitTestOverlay(point, window)) { Invalidate?.Invoke(); return true; }

        var handled = _root.HitTest(point);
        if (handled) Invalidate?.Invoke(); // engine-local changes (tab/menu/nav) need a repaint too
        return handled;
    }

    static IEnumerable<SkiaNode> Overlays(SkiaNode n)
    {
        // Only descend into what's actually on screen (e.g. the selected tab) so a presentation in a
        // hidden tab doesn't bleed over the visible one.
        foreach (var c in n.VisibleOverlayChildren())
            foreach (var o in Overlays(c))
                yield return o;
        if (n.HasActiveOverlay) yield return n;
    }

    static IEnumerable<SkiaNode> OverlaysTopFirst(SkiaNode n) => Overlays(n).Reverse();

    /// <summary>Scroll the innermost scrollable under <paramref name="point"/> by <paramref name="dy"/> px.</summary>
    public bool Scroll(SKPoint point, float dy)
    {
        if (_root?.ScrollableAt(point) is not { } node) return false;

        // Pull-to-refresh: dragging down while already at the top of a refreshable list.
        if (node.ScrollOffset <= 0 && dy < 0 && node.Props.GetValueOrDefault("refreshable") as bool? == true)
            Emit(node.Id, List.RefreshValue);

        node.ScrollOffset = Math.Max(0, node.ScrollOffset + dy); // clamped to content on next layout

        // Incremental load: scrolled within the threshold of the end of a list that asked for it.
        if (node.Props.GetValueOrDefault("reachEndThreshold") is double threshold
            && node.ScrollMax > 0 && node.ScrollOffset >= node.ScrollMax - threshold)
            Emit(node.Id, List.LoadMoreValue);

        Invalidate?.Invoke();
        return true;
    }

    /// <summary>The laid-out frame of a node by id (valid after a <see cref="Paint"/> pass). For tests/tooling.</summary>
    public bool TryGetFrame(string id, out SKRect frame)
    {
        var node = Find(id);
        frame = node?.Frame ?? default;
        return node is not null;
    }

    /// <summary>Advance all active implicit animations by <paramref name="dt"/>s. Returns true while animating
    /// (the host should keep repainting); false when everything has settled.</summary>
    public bool Tick(double dt)
    {
        var active = _root?.Tick(dt) ?? false;
        if (active) Invalidate?.Invoke();
        return active;
    }

    /// <summary>Fire a long-press at a point (host resolves the hold from a pointer stream).</summary>
    public bool LongPress(SKPoint point)
    {
        var handled = _root?.DispatchGesture(point, "onLongPress", null) ?? false;
        if (handled) Invalidate?.Invoke();
        return handled;
    }

    /// <summary>Fire a directional swipe at a point (host resolves direction from a drag).</summary>
    public bool Swipe(SKPoint point, string direction)
    {
        var handled = _root?.DispatchGesture(point, "onSwipe", direction) ?? false;
        if (handled) Invalidate?.Invoke();
        return handled;
    }

    // F1 continuous drag/pinch. The host feeds a raw pointer stream; the engine captures the target node
    // at Began and routes subsequent Changed/Ended to it, emitting the shared drag grammar.
    SkiaNode? _dragTarget;
    SkiaNode? _magnifyTarget;

    /// <summary>Feed a continuous drag. <paramref name="phase"/>: begin captures the target; change/end reuse it.</summary>
    public bool Drag(SKPoint point, GesturePhase phase, float tx, float ty, float vx, float vy)
    {
        if (phase == GesturePhase.Began) _dragTarget = _root?.NodeWithModAt(point, "onDrag");
        if (_dragTarget?.ModEvent("onDrag") is not { } ev) return false;
        var ph = phase switch { GesturePhase.Began => "b", GesturePhase.Ended => "e", _ => "c" };
        Emit(ev, string.Format(CultureInfo.InvariantCulture, "{0};{1},{2};{3},{4};{5},{6}",
            ph, tx, ty, point.X, point.Y, vx, vy));
        if (phase == GesturePhase.Ended) _dragTarget = null;
        Invalidate?.Invoke();
        return true;
    }

    /// <summary>Feed a continuous pinch. <paramref name="phase"/>: begin captures the target; scale is cumulative.</summary>
    public bool Magnify(SKPoint point, GesturePhase phase, float scale)
    {
        if (phase == GesturePhase.Began) _magnifyTarget = _root?.NodeWithModAt(point, "onMagnify");
        if (_magnifyTarget?.ModEvent("onMagnify") is not { } ev) return false;
        Emit(ev, scale.ToString(CultureInfo.InvariantCulture));
        if (phase == GesturePhase.Ended) _magnifyTarget = null;
        Invalidate?.Invoke();
        return true;
    }

    /// <summary>Append typed text to the focused text control (routes through its C# binding).</summary>
    public void InsertText(string s)
    {
        if (FocusedId is null || Find(FocusedId) is not { } node) return;
        Emit(FocusedId, node.TextProp() + s);
    }

    /// <summary>Delete the last character of the focused text control.</summary>
    public void DeleteBackward()
    {
        if (FocusedId is null || Find(FocusedId) is not { } node) return;
        var cur = node.TextProp();
        if (cur.Length > 0) Emit(FocusedId, cur[..^1]);
    }

    SkiaNode? Find(string id)
    {
        var node = _root;
        if (node is null) return null;
        var parts = id.Split('.');
        if (parts[0] != node.Id) return null;
        for (var i = 1; i < parts.Length; i++)
        {
            var idx = int.Parse(parts[i]);
            if (idx < 0 || idx >= node.Children.Count) return null;
            node = node.Children[idx];
        }
        return node;
    }
}
