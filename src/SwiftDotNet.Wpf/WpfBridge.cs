using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
// Core declares `Grid` in this same namespace (SwiftDotNet), so WPF's is reached through a
// distinctly-named alias — see the note in WpfStyle.cs.
using WpfGrid = System.Windows.Controls.Grid;

namespace SwiftDotNet;

/// <summary>
/// The WPF implementation of <see cref="IBridge"/> — a pure-C# retained-mode interpreter, the parallel of
/// the GTK and WinUI backends. WPF controls are fully C#-bindable, so there is no native shim: this
/// maintains a WPF element tree keyed by node id and applies the same replace/updateProps/setChildren
/// diff patches directly to real controls, and WPF events call straight back into C#.
/// </summary>
public sealed class WpfBridge : IBridge
{
    Action<string, string?>? _handler;
    WpfNode? _root;

    internal Stack<WpfNavController> NavStack { get; } = new();

    /// <summary>
    /// The element the app hosts as its window content. It is a <see cref="WpfGrid"/> rather than a
    /// single-child container because WPF has no <c>ContentDialog</c>: presented Sheets/Alerts/ActionSheets
    /// are stacked into this same cell as overlay layers. The alternative — a real modal
    /// <c>Window.ShowDialog()</c> — blocks its caller, and the caller here is <see cref="Render"/>,
    /// running in the middle of applying a patch.
    /// </summary>
    public WpfGrid Host { get; } = new();

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    internal void Emit(string id, string? value) => _handler?.Invoke(id, value);

    /// <summary>Push a modal layer over the content (a presented Sheet / Alert / ActionSheet).</summary>
    internal void ShowOverlay(FrameworkElement layer)
    {
        if (!Host.Children.Contains(layer)) Host.Children.Add(layer);
    }

    /// <summary>Remove a modal layer previously pushed by <see cref="ShowOverlay"/>.</summary>
    internal void HideOverlay(FrameworkElement layer) => Host.Children.Remove(layer);

    public void Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            switch (op.GetProperty("op").GetString())
            {
                case "replace":
                    if (_root is not null) Host.Children.Remove(_root.Element);
                    _root = WpfNode.Build(op.GetProperty("node"), this);
                    // Index 0 keeps the content underneath any overlay layer already presented.
                    Host.Children.Insert(0, _root.Element);
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

    WpfNode? Find(string id)
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
