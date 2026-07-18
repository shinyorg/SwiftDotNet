namespace SwiftDotNet;

/// <summary>
/// The serializable render-tree node — the wire contract between the C# DSL and the
/// Swift/SwiftUI host. Prop/modifier values are boxed <see cref="string"/>, <see cref="double"/>,
/// or <see cref="bool"/>; the Swift interpreter walks the same shape.
/// </summary>
public sealed class Node
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public Dictionary<string, object> Props { get; } = new();
    public List<Dictionary<string, object>> Modifiers { get; } = new();
    public List<Node> Children { get; } = new();
}
