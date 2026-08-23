using MauiControl = Microsoft.Maui.Controls.View;

namespace SwiftDotNet;

/// <summary>
/// The side channel that carries a <see cref="MauiView"/>'s factory from the render pass to the host.
///
/// <para>It exists because the patch protocol cannot carry it. Props are JSON scalars written by a
/// hand-rolled, reflection-free serializer, so a <c>Func&lt;View&gt;</c> has no representation on the wire;
/// the node carries only a <c>key</c>, and the real delegate is looked up here. That is sound precisely
/// because a MAUI-hosted backend is an <b>in-process</b> interpreter — the same reason this trick does not
/// generalize across the Swift/Kotlin ABI, where a native shim must be handed a view pointer instead.</para>
///
/// <para><b>Threading:</b> written during render and read during paint, both on the host's UI thread. It is
/// not synchronized, and does not need to be, as long as views are built where they are drawn.</para>
/// </summary>
public static class MauiViewRegistry
{
    sealed class Entry
    {
        public Func<MauiControl> Factory = null!;
        public Action<MauiControl>? Update;
        public string NodeId = "";
        /// <summary>Installed by the host once it has created the control, so the control can talk back.</summary>
        public Action<string?>? Emitter;
    }

    static readonly Dictionary<string, Entry> Map = new(StringComparer.Ordinal);

    /// <summary>
    /// Record (or refresh) the delegates for one identity. Called on every render pass: the factory is
    /// only ever used once per identity, but <paramref name="update"/> is re-captured each time so it
    /// closes over the *current* state.
    /// </summary>
    internal static void Bind(string key, string nodeId, Func<MauiControl> factory, Action<MauiControl>? update)
    {
        if (!Map.TryGetValue(key, out var entry)) Map[key] = entry = new Entry();
        entry.Factory = factory;
        entry.Update = update;
        entry.NodeId = nodeId;   // the structural id moves under a stable key; events must follow it
    }

    /// <summary>Build the control for an identity, or null when no <see cref="MauiView"/> declared it.</summary>
    public static MauiControl? Create(string key)
        => Map.TryGetValue(key, out var e) ? e.Factory() : null;

    /// <summary>Push current values into a live control. Safe to call when the view declared no updater.</summary>
    public static void Update(string key, MauiControl control)
    {
        if (Map.TryGetValue(key, out var e)) e.Update?.Invoke(control);
    }

    /// <summary>The structural node id currently backing an identity — what an event must be addressed to.</summary>
    public static string? NodeIdOf(string key)
        => Map.TryGetValue(key, out var e) ? e.NodeId : null;

    /// <summary>Host-side: install the channel an embedded control uses to raise events back into C#.</summary>
    public static void SetEmitter(string key, Action<string?>? emitter)
    {
        if (Map.TryGetValue(key, out var e)) e.Emitter = emitter;
    }

    /// <summary>
    /// Raise an event from inside an embedded control — it lands on the <see cref="MauiView.OnEvent"/>
    /// handler. A no-op when the view is not currently placed by a host.
    /// </summary>
    public static void Emit(string key, string? value = null)
    {
        if (Map.TryGetValue(key, out var e)) e.Emitter?.Invoke(value);
    }

    /// <summary>
    /// Forget an identity. Called by the host when a node leaves the tree for good — the counterpart to
    /// disposing the control itself, and the half that would otherwise leak the closures the factory and
    /// updater capture.
    /// </summary>
    public static void Release(string key) => Map.Remove(key);

    /// <summary>Live identity count. For tests and leak checks.</summary>
    public static int Count => Map.Count;
}
