namespace SwiftDotNet;

/// <summary>
/// The runtime. Owns the root view instance, drives render passes, and routes events coming
/// back from the SwiftUI host to the C# callbacks that produced them.
///
/// A state change re-renders the tree; <see cref="TreeDiffer"/> then reduces what actually
/// crosses the bridge to a minimal patch.
/// </summary>
public static class SwiftApp
{
    static IBridge? _bridge;
    static View? _root;
    static Dictionary<string, Action<string?>> _actions = new();
    static Node? _lastTree;

    public static void Run(View root, IBridge bridge)
    {
        _root = root;
        _bridge = bridge;
        bridge.SetEventHandler(OnEvent);
        Render();
    }

    internal static void RequestRender() => Render();

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
