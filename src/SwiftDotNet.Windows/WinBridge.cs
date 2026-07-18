using System.Text.Json;
using Microsoft.UI.Xaml.Controls;

namespace SwiftDotNet;

/// <summary>
/// The Windows/WinUI implementation of <see cref="IBridge"/> — a pure-C# retained-mode interpreter
/// (the parallel of the GTK backend; WinUI 3 controls are fully C#-bindable, so no native shim).
/// Maintains a WinUI element tree keyed by node id and applies the same replace/updateProps/setChildren
/// diff patches directly to real controls. WinUI events call straight back into C#.
/// </summary>
public sealed class WinBridge : IBridge
{
    Action<string, string?>? _handler;
    WinNode? _root;

    internal Stack<WinNavController> NavStack { get; } = new();

    /// <summary>The element the app hosts as its window content; the render tree lives inside it.</summary>
    public Border Host { get; } = new();

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    internal void Emit(string id, string? value) => _handler?.Invoke(id, value);

    public void Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            switch (op.GetProperty("op").GetString())
            {
                case "replace":
                    _root = WinNode.Build(op.GetProperty("node"), this);
                    Host.Child = _root.Element;
                    break;
                case "updateProps":
                    Find(op.GetProperty("id").GetString()!)?.UpdateProps(op.GetProperty("props"), op.GetProperty("modifiers"));
                    break;
                case "setChildren":
                    Find(op.GetProperty("id").GetString()!)?.SetChildren(op.GetProperty("children"));
                    break;
            }
        }
    }

    WinNode? Find(string id)
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
