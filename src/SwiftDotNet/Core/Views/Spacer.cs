namespace SwiftDotNet;

public sealed class Spacer : View
{
    internal override Node BuildNode(RenderContext ctx, string path) => ctx.NewNode("Spacer", path);
}
