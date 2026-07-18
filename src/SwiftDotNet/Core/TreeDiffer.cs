using System.Text;

namespace SwiftDotNet;

/// <summary>
/// Computes the minimal set of patch ops to turn the previously rendered tree into the new one.
/// Because node ids are structural paths, an unchanged node keeps its id and we can target it by id.
///
/// Op vocabulary (shared with the Swift host):
/// <list type="bullet">
/// <item><c>replace</c> — swap the whole root (first render, or the root's type changed).</item>
/// <item><c>updateProps</c> — a node kept its type &amp; children shape; only props/modifiers changed.</item>
/// <item><c>setChildren</c> — a node's child list changed shape (count or a child's type); replace that subtree.</item>
/// </list>
/// </summary>
public static class TreeDiffer
{
    public static Patch Diff(Node? previous, Node next)
    {
        var ops = new List<PatchOp>();
        if (previous is null || previous.Id != next.Id || previous.Type != next.Type)
        {
            ops.Add(new ReplaceOp(next));
            return new Patch(ops);
        }
        DiffNode(previous, next, ops);
        return new Patch(ops);
    }

    static void DiffNode(Node previous, Node next, List<PatchOp> ops)
    {
        if (!DictEqual(previous.Props, next.Props) || !ModifiersEqual(previous.Modifiers, next.Modifiers))
            ops.Add(new UpdatePropsOp(next));

        var structural = previous.Children.Count != next.Children.Count;
        if (!structural)
        {
            for (var i = 0; i < previous.Children.Count; i++)
            {
                if (previous.Children[i].Type != next.Children[i].Type ||
                    previous.Children[i].Id != next.Children[i].Id)
                {
                    structural = true;
                    break;
                }
            }
        }

        if (structural)
        {
            ops.Add(new SetChildrenOp(next));
        }
        else
        {
            for (var i = 0; i < previous.Children.Count; i++)
                DiffNode(previous.Children[i], next.Children[i], ops);
        }
    }

    static bool DictEqual(Dictionary<string, object> a, Dictionary<string, object> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var kv in a)
            if (!b.TryGetValue(kv.Key, out var v) || !Equals(kv.Value, v))
                return false;
        return true;
    }

    static bool ModifiersEqual(List<Dictionary<string, object>> a, List<Dictionary<string, object>> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!DictEqual(a[i], b[i]))
                return false;
        return true;
    }
}

/// <summary>A batch of ops that migrates the Swift host's tree to match the latest C# render.</summary>
public sealed class Patch
{
    readonly List<PatchOp> _ops;

    internal Patch(List<PatchOp> ops) => _ops = ops;

    public bool HasChanges => _ops.Count > 0;

    public string ToJson()
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"ops\":[");
        for (var i = 0; i < _ops.Count; i++)
        {
            if (i > 0) sb.Append(',');
            _ops[i].Append(sb);
        }
        sb.Append("]}");
        return sb.ToString();
    }
}

abstract class PatchOp
{
    public abstract void Append(StringBuilder sb);
}

sealed class ReplaceOp : PatchOp
{
    readonly Node _node;
    public ReplaceOp(Node node) => _node = node;
    public override void Append(StringBuilder sb)
    {
        sb.Append("{\"op\":\"replace\",\"node\":");
        NodeJson.AppendNode(sb, _node);
        sb.Append('}');
    }
}

sealed class UpdatePropsOp : PatchOp
{
    readonly Node _node;
    public UpdatePropsOp(Node node) => _node = node;
    public override void Append(StringBuilder sb)
    {
        sb.Append("{\"op\":\"updateProps\",\"id\":");
        NodeJson.AppendString(sb, _node.Id);
        sb.Append(",\"props\":");
        NodeJson.AppendDict(sb, _node.Props);
        sb.Append(",\"modifiers\":");
        NodeJson.AppendDictArray(sb, _node.Modifiers);
        sb.Append('}');
    }
}

sealed class SetChildrenOp : PatchOp
{
    readonly Node _node;
    public SetChildrenOp(Node node) => _node = node;
    public override void Append(StringBuilder sb)
    {
        sb.Append("{\"op\":\"setChildren\",\"id\":");
        NodeJson.AppendString(sb, _node.Id);
        sb.Append(",\"children\":[");
        for (var i = 0; i < _node.Children.Count; i++)
        {
            if (i > 0) sb.Append(',');
            NodeJson.AppendNode(sb, _node.Children[i]);
        }
        sb.Append("]}");
    }
}
