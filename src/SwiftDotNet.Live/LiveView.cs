namespace SwiftDotNet;

/// <summary>
/// Base type for the **live surface vocabulary** — the restricted view language understood by the
/// system-rendered surfaces (iOS Live Activities and widgets, Android notifications and app widgets).
///
/// This is deliberately *not* a <see cref="View"/>. The main DSL assumes our process owns the renderer
/// and can push patches at will; none of these surfaces work that way. A Live Activity is drawn by a
/// WidgetKit extension that contains no .NET, and an Android notification is inflated inside SystemUI.
/// Both are handed a whole tree as data and render it with a *subset* of their toolkit — a subset that
/// excludes most of what <see cref="View"/> can express (no scrolling, no text entry, no pickers, no
/// gestures, no web views, no maps).
///
/// A parallel vocabulary makes that subset a **build-time fact** instead of a silent runtime drop.
/// The alternative — lowering an ordinary <see cref="View"/> tree and discarding whatever doesn't map —
/// was rejected: the failure mode is a lock-screen UI the developer never sees, missing half its content.
///
/// Everything *below* the vocabulary is shared with the main pipeline: the same <see cref="Node"/>, the
/// same value tokens (<see cref="SwiftColor"/>, <see cref="SwiftFont"/>, <see cref="Brush"/>), and the
/// same action dispatch. Only the wire is different — see <see cref="LiveWire"/>.
/// </summary>
public abstract class LiveView
{
    internal readonly List<Dictionary<string, object>> Mods = new();

    /// <summary>Leaf views emit their node here. <paramref name="ctx"/> mints ids and collects actions.</summary>
    internal abstract Node Build(LiveContext ctx, string path);

    internal Node ToNode(LiveContext ctx, string path)
    {
        var node = Build(ctx, path);
        for (var i = 0; i < Mods.Count; i++)
            node.Modifiers.Add(Mods[i]);
        return node;
    }

    /// <summary>Attaches a modifier dictionary in the same shape <see cref="Node.Modifiers"/> uses.</summary>
    internal LiveView AddMod(string type, params (string Key, object Value)[] values)
    {
        var d = new Dictionary<string, object>(values.Length + 1) { ["type"] = type };
        foreach (var (k, v) in values) d[k] = v;
        Mods.Add(d);
        return this;
    }
}

/// <summary>
/// Render context for one live tree: mints structural ids and collects the tap/button actions found
/// along the way so the platform driver can register them before the tree leaves the process.
/// </summary>
public sealed class LiveContext
{
    readonly Dictionary<string, Action<string?>> _actions = new();

    /// <summary>The surface this tree is being built for. Some views degrade per surface.</summary>
    public LiveSurface Surface { get; init; } = LiveSurface.Activity;

    /// <summary>Actions discovered in the tree, keyed by node id — the same shape <c>SwiftApp</c> uses.</summary>
    public IReadOnlyDictionary<string, Action<string?>> Actions => _actions;

    internal Node NewNode(string type, string path)
    {
        var n = new Node { Id = path, Type = type };
        return n;
    }

    internal void Register(string id, Action<string?> handler) => _actions[id] = handler;
}

/// <summary>Which system surface a tree is destined for. Drives per-surface validation and degradation.</summary>
public enum LiveSurface
{
    /// <summary>iOS Live Activity (lock screen / Dynamic Island) or an Android Live Update.</summary>
    Activity,
    /// <summary>An Android custom-content notification.</summary>
    Notification,
    /// <summary>A home-screen / lock-screen widget.</summary>
    Widget,
}
