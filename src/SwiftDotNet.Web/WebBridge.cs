using System.Text.Json;

namespace SwiftDotNet;

/// <summary>
/// The web implementation of <see cref="IBridge"/>. Keeps the render tree as a plain model, applies the
/// same replace/updateProps/setChildren patches to it, and raises <see cref="Changed"/> so the hosting
/// Blazor component re-renders (Blazor's render-tree diff is the DOM-write layer).
/// </summary>
public sealed class WebBridge : IBridge
{
    Action<string, string?>? _handler;

    internal WebNode? Root { get; private set; }
    public event Action? Changed;

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
                    Root = WebNode.Parse(op.GetProperty("node"));
                    break;
                case "updateProps":
                    if (Root?.Find(op.GetProperty("id").GetString()!) is { } up)
                    {
                        up.Props = ReadDict(op.GetProperty("props"));
                        up.Modifiers = ReadDictArray(op.GetProperty("modifiers"));
                    }
                    break;
                case "setChildren":
                    if (Root?.Find(op.GetProperty("id").GetString()!) is { } sc)
                    {
                        sc.Children.Clear();
                        foreach (var c in op.GetProperty("children").EnumerateArray())
                            sc.Children.Add(WebNode.Parse(c));
                    }
                    break;
            }
        }
        Changed?.Invoke();
    }

    static Dictionary<string, object?> ReadDict(JsonElement e)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in e.EnumerateObject())
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString(),
                JsonValueKind.Number => p.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        return d;
    }

    static List<Dictionary<string, object?>> ReadDictArray(JsonElement e)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var item in e.EnumerateArray()) list.Add(ReadDict(item));
        return list;
    }
}
