using System.Text.Json;

namespace SwiftDotNet;

/// <summary>
/// The Linux/GTK implementation of <see cref="IBridge"/> — a pure-C# retained-mode interpreter.
/// Unlike the SwiftUI/Compose backends (which push a tree to a declarative host), GTK is imperative,
/// so this maintains a widget tree keyed by node id and applies the same replace/updateProps/setChildren
/// patches directly to real <c>Gtk.Widget</c>s. GTK signals call straight back into C#.
/// </summary>
public sealed class GtkBridge : IBridge
{
    Action<string, string?>? _handler;
    GtkNode? _root;

    /// <summary>Active navigation controllers during a build pass (innermost on top).</summary>
    internal Stack<NavController> NavStack { get; } = new();

    /// <summary>The container the app hosts as the window's child; the render tree lives inside it.</summary>
    public Gtk.Box Host { get; }

    public GtkBridge()
    {
        Host = Gtk.Box.New(Gtk.Orientation.Vertical, 0);
        Host.Hexpand = true;
        Host.Vexpand = true;
    }

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    /// <summary>Raise an event as if it came from a widget (what GTK signal handlers call).</summary>
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);

    public void Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            switch (op.GetProperty("op").GetString())
            {
                case "replace":
                    if (_root is not null) Host.Remove(_root.Widget);
                    _root = GtkNode.Build(op.GetProperty("node"), this);
                    _root.Widget.Hexpand = true;
                    _root.Widget.Vexpand = true;
                    Host.Append(_root.Widget);
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

    GtkNode? Find(string id)
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
