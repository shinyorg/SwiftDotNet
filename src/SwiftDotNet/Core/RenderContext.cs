namespace SwiftDotNet;

/// <summary>
/// Threads state through a single render pass. Node ids are <b>structural paths</b> (e.g. "0.2.1"
/// = root → child 2 → child 1) so a node keeps its id across renders as long as its position is
/// stable — which is what makes the <see cref="TreeDiffer"/> able to target updates by id.
/// Callbacks receive an optional value payload (TextField text, Toggle bool, null for a Button).
/// </summary>
internal sealed class RenderContext
{
    public Dictionary<string, Action<string?>> Actions { get; } = new();

    public Node NewNode(string type, string path) => new() { Id = path, Type = type };

    public void RegisterAction(string path, Action<string?> action) => Actions[path] = action;
}
