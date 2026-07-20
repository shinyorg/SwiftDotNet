namespace SwiftDotNet;

/// <summary>
/// The runtime. Owns the root view instance, drives render passes, and routes events coming
/// back from the SwiftUI host to the C# callbacks that produced them.
///
/// A state change re-renders the tree; <see cref="TreeDiffer"/> then reduces what actually
/// crosses the bridge to a minimal patch.
///
/// Render scheduling gives you SwiftUI-like semantics for free:
/// <list type="bullet">
/// <item><b>Coalescing</b> — several <see cref="State{T}"/> mutations in one tick collapse into a
/// single render, so <c>a.Value = x; b.Value = y;</c> builds/diffs the tree once, not twice.</item>
/// <item><b>Main-thread marshaling</b> — the render is posted to the UI thread captured at
/// <see cref="Run(View, IBridge, IServiceProvider?)"/>, so a background event (GPS, timer, socket) can assign <c>State.Value</c>
/// directly without hopping threads itself.</item>
/// </list>
/// Use <see cref="Transaction"/> for an explicit, synchronous batch boundary.
/// </summary>
public static class SwiftApp
{
    static IBridge? _bridge;
    static View? _root;
    static Dictionary<string, Action<string?>> _actions = new();
    static Node? _lastTree;

    /// <summary>UI-thread pump captured at <see cref="Run(View, IBridge, IServiceProvider?)"/>; null on backends without one (render inline).</summary>
    static SynchronizationContext? _uiContext;
    /// <summary>A coalesced render is already posted and waiting to run.</summary>
    static bool _renderQueued;
    /// <summary>Inside a <see cref="Transaction"/>: swallow per-assignment renders and flush once at the end.</summary>
    static bool _batching;

    public static void Run(View root, IBridge bridge) => Run(root, bridge, null);

    /// <summary>
    /// Run <paramref name="root"/>, publishing <paramref name="services"/> as the app's ambient
    /// container so views can reach services via <c>Service&lt;T&gt;()</c> / <c>[Inject]</c>.
    /// Platform host bases pass <c>SwiftDotNetApp.Services</c> here.
    /// </summary>
    public static void Run(View root, IBridge bridge, IServiceProvider? services)
    {
        if (services is not null)
            SwiftHost.Services = services;

        _root = root;
        _bridge = bridge;
        _lastTree = null; // fresh run: emit a full replace rather than diffing against a prior root's tree
        _uiContext = SynchronizationContext.Current;
        bridge.SetEventHandler(OnEvent);
        Render();
    }

    /// <summary>
    /// Apply several state mutations as one atomic batch: per-assignment renders are suppressed and a
    /// single render runs after <paramref name="mutations"/> returns. Synchronous on the current thread
    /// (SwiftUI's <c>withTransaction</c> shape); the natural anchor point for animation transactions later.
    /// </summary>
    public static void Transaction(Action mutations)
    {
        ArgumentNullException.ThrowIfNull(mutations);

        if (_batching)
        {
            // Nested transaction — the outer one owns the flush.
            mutations();
            return;
        }

        _batching = true;
        try
        {
            mutations();
        }
        finally
        {
            _batching = false;
        }
        Render();
    }

    /// <summary>
    /// Schedule a render. Called by <see cref="State{T}"/> when a value actually changes. Coalesces
    /// bursts into one render and marshals to the UI thread when a <see cref="SynchronizationContext"/>
    /// was captured at <see cref="Run(View, IBridge, IServiceProvider?)"/>.
    /// </summary>
    internal static void RequestRender()
    {
        if (_batching)
            return; // Transaction will flush once when the batch closes.

        if (_uiContext is null)
        {
            // No UI pump to post to (e.g. headless/console host): render inline.
            Render();
            return;
        }

        if (_renderQueued)
            return; // Already scheduled; this mutation folds into the pending render.

        _renderQueued = true;
        _uiContext.Post(_ =>
        {
            _renderQueued = false;
            Render();
        }, null);
    }

    static void Render()
    {
        if (_root is null || _bridge is null)
            return;

        var ctx = new RenderContext();
        var tree = _root.ToNode(ctx, "0");
        _actions = ctx.Actions;

        var patch = TreeDiffer.Diff(_lastTree, tree);
        _lastTree = tree;

        if (patch.HasChanges)
            _bridge.Render(patch.ToJson());
    }

    static void OnEvent(string nodeId, string? value)
    {
        if (_actions.TryGetValue(nodeId, out var action))
            action(value);
    }
}
