using System.Text.Json;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace SwiftDotNet;

/// <summary>
/// The terminal implementation of <see cref="IBridge"/> — a pure-C# retained-mode interpreter, the same
/// shape as the GTK backend. XenoAtom.Terminal.UI is a retained widget toolkit (Measure/Arrange, routed
/// events, themes), not a declarative host, so this keeps a visual tree keyed by node id and applies the
/// wire's <c>replace</c> / <c>updateProps</c> / <c>setChildren</c> patches straight onto live
/// <c>Visual</c>s. Terminal input events call back into C# through <see cref="Emit"/>.
/// </summary>
public sealed class TuiBridge : IBridge
{
    Action<string, string?>? _handler;
    TuiNode? _root;

    /// <summary>Active navigation controllers during a build pass (innermost on top).</summary>
    internal Stack<TuiNavController> NavStack { get; } = new();

    /// <summary>
    /// The visual the app hosts as its root; the render tree lives inside it. A <c>WindowLayer</c> rather
    /// than a plain panel because <c>Sheet</c> and <c>Alert</c> present real <c>Dialog</c> windows, and
    /// those need a layer to be hosted in — see <see cref="Windows"/>.
    /// </summary>
    public WindowLayer Host { get; } = new();

    /// <summary>Where <c>Sheet</c>/<c>Alert</c> dialogs are pushed and popped.</summary>
    internal WindowLayer Windows => Host;

    /// <summary>
    /// The running app, once <c>SwiftDotNetHost</c> has one. Patches mutate plain (non-bindable)
    /// properties on retained visuals, which the framework cannot observe, so every applied patch ends
    /// with an explicit <c>RequestFullRender()</c>.
    /// </summary>
    public TerminalApp? App { get; set; }

    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;

    /// <summary>Raise an event as if it came from a control (what Terminal.UI handlers call).</summary>
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);

    public void Render(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var op in doc.RootElement.GetProperty("ops").EnumerateArray())
        {
            switch (op.GetProperty("op").GetString())
            {
                case "replace":
                    _root = TuiNode.Build(op.GetProperty("node"), this);
                    _root.Visual.HorizontalAlignment = Align.Stretch;
                    _root.Visual.VerticalAlignment = Align.Stretch;
                    Host.Content = _root.Visual;
                    break;
                case "updateProps":
                    Find(op.GetProperty("id").GetString()!)?.UpdateProps(op.GetProperty("props"), op.GetProperty("modifiers"));
                    break;
                case "setChildren":
                    Find(op.GetProperty("id").GetString()!)?.SetChildren(op.GetProperty("children"));
                    break;
            }
        }
        App?.RequestFullRender();
    }

    /// <summary>
    /// The <see cref="TuiSurface"/> wrapper for the node at <paramref name="id"/>, or null when the path
    /// does not resolve. This and <see cref="FindControl"/> are the inspection seam tests and embedders
    /// use to reach into the retained tree.
    /// </summary>
    public Visual? FindVisual(string id) => Find(id)?.Visual;

    /// <summary>The Terminal.UI control a node built, unwrapped from its surface.</summary>
    public Visual? FindControl(string id) => Find(id)?.Content;

    /// <summary>
    /// The structural id the node at <paramref name="id"/> currently believes it has. Normally identical
    /// to what was asked for; it differs only if keyed recycling failed to re-stamp a moved row, which is
    /// exactly the bug that would misroute that row's events.
    /// </summary>
    public string? StoredId(string id) => Find(id)?.Id;

    /// <summary>
    /// Resolves a structural node path ("0.2.1" = root → child 2 → child 1) to its retained node.
    /// Returns null rather than throwing when the path has drifted — a patch for a node the host has
    /// already rebuilt is a no-op, not a crash.
    /// </summary>
    internal TuiNode? Find(string id)
    {
        var node = _root;
        if (node is null) return null;
        var parts = id.Split('.');
        if (parts[0] != node.Id) return null;
        for (var i = 1; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var idx) || idx < 0 || idx >= node.Children.Count) return null;
            node = node.Children[idx];
        }
        return node;
    }
}
