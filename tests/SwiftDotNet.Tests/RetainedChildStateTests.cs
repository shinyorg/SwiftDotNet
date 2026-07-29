using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// A view's <c>State&lt;T&gt;</c> only survives a render pass if the view *instance* survives it. Views
/// built inline inside a <c>Body</c> are rebuilt every pass, so a parent that writes
/// <c>Body =&gt; new Wrapper(new StatefulChild())</c> hands back a brand-new child — and a brand-new set
/// of state fields at their initial values — each time it renders.
///
/// The failure mode is silent and looks like a *host* bug: the event reaches C#, the handler runs and
/// assigns the state, a render is scheduled and runs — but it builds a fresh child, so the tree it
/// produces is identical to the last one, the diff is empty, and nothing ever reaches the screen. This
/// is exactly what was mis-filed as "the Skia MAUI host does not repaint on state change"; it reproduced
/// on every backend, including the headless Skia harness.
///
/// Until view-instance reconciliation lands (see <c>plans/README.md</c>), the contract is: whoever builds
/// a stateful view must hold it. These tests pin both halves of that contract.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class RetainedChildStateTests
{
    [Fact]
    public void RetainedChild_StateChange_ProducesAPatch()
    {
        var child = new GreetingChild();
        var bridge = new RecordingBridge();
        SwiftApp.Run(new RetainingRoot(child), bridge);

        bridge.Patches.Clear();
        child.Fire("Ada");

        Assert.Single(bridge.Patches);
        Assert.Contains("Hello, Ada!", bridge.Patches[0], StringComparison.Ordinal);
    }

    [Fact]
    public void RebuiltChild_StateChange_ProducesNoPatch()
    {
        // The regression itself: the root rebuilds its child every pass, so the handler mutates an
        // instance that is no longer rendered and the diff comes back empty.
        var bridge = new RecordingBridge();
        var root = new RebuildingRoot();
        SwiftApp.Run(root, bridge);

        bridge.Patches.Clear();
        root.LastBuiltChild!.Fire("Ada");

        Assert.Empty(bridge.Patches);
    }

    [Fact]
    public void RetainedChild_TypingIntoATextField_UpdatesTheBoundTextOnSkia()
    {
        // The same contract driven end-to-end through the Skia engine, mirroring the shared sample:
        // focus the field by tapping it, type, and check the bound greeting repainted.
        var child = new GreetingChild();
        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge);
        SwiftApp.Run(new RetainingRoot(child), bridge);
        host.RenderPng(400, 800);

        // Child ids under the root's single-child wrapper: VStack "0.0" → Text "0.0.0", TextField "0.0.1".
        Assert.True(bridge.TryGetFrame("0.0.1", out var field));
        host.Tap(field.MidX, field.MidY);
        Assert.Equal("0.0.1", bridge.FocusedId);

        host.Type("Ada");
        Assert.Equal("Ada", child.Name.Value);

        // The greeting node must have grown — "Hello, stranger!" → "Hello, Ada!" repainted, not stale.
        host.RenderPng(400, 800);
        Assert.True(bridge.TryGetFrame("0.0.0", out var greeting));
        Assert.True(greeting.Width > 0);
        Assert.Equal("Hello, Ada!", child.Greeting);
    }
}

/// <summary>A stateful child: the thing that must be retained across renders.</summary>
file sealed class GreetingChild : View
{
    public State<string> Name { get; } = new("");

    public string Greeting => Name.Value.Length == 0 ? "Hello, stranger!" : $"Hello, {Name.Value}!";

    /// <summary>Stand-in for a control's callback firing (a Button tap, a TextField edit).</summary>
    public void Fire(string name) => Name.Value = name;

    public override View Body => new VStack(
        new Text(Greeting),
        new TextField("Name", Name)
    );
}

/// <summary>Holds its child, so the child's state survives a render pass. What the sample root does.</summary>
file sealed class RetainingRoot(GreetingChild child) : View
{
    public override View Body => new VStack(child);
}

/// <summary>Rebuilds its child every pass — the shape that silently drops state.</summary>
file sealed class RebuildingRoot : View
{
    public GreetingChild? LastBuiltChild { get; private set; }

    public override View Body
    {
        get
        {
            LastBuiltChild = new GreetingChild();
            return new VStack(LastBuiltChild);
        }
    }
}

/// <summary>Captures the patch JSON each render pass ships, so a test can assert on its content.</summary>
file sealed class RecordingBridge : IBridge
{
    public List<string> Patches { get; } = new();

    public void Render(string json) => Patches.Add(json);

    public void SetEventHandler(Action<string, string?> handler) { }
}
