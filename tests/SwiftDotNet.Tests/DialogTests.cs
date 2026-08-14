using System.Text.Json;
using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// The multi-button <see cref="Alert"/> / <see cref="ActionSheet"/> contract: the flat
/// <see cref="DialogButtons"/> wire encoding, the props both views ship, and the index-payload binding
/// every backend emits against. Painting and hit-testing are covered by <see cref="SkiaDialogTests"/>.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class DialogWireTests
{
    [Fact]
    public void Encode_WritesLabelAndRolePairs()
    {
        var encoded = DialogButtons.Encode(new[]
        {
            AlertButton.Destructive("Delete"),
            AlertButton.Cancel(),
        });

        Assert.Equal("Delete,destructive;Cancel,cancel", encoded);
    }

    [Theory]
    [InlineData("Save, then quit")]           // a comma in the label
    [InlineData("Yes; really")]               // the entry delimiter
    [InlineData(@"C:\path\to")]               // the escape character itself
    [InlineData(@"all, three; at \once")]
    public void EncodeParse_RoundTripsDelimitersInLabels(string label)
    {
        var encoded = DialogButtons.Encode(new[] { new AlertButton(label), AlertButton.Cancel() });
        var parsed = DialogButtons.Parse(encoded);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(label, parsed[0].Label);
        Assert.Equal(DialogRole.Default, parsed[0].Role);
        Assert.Equal(DialogRole.Cancel, parsed[1].Role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_MissingButtons_FallsBackToASingleOk(string? encoded)
    {
        // A backend must always be able to draw something dismissable, even from a malformed tree.
        var parsed = DialogButtons.Parse(encoded);

        Assert.Single(parsed);
        Assert.Equal("OK", parsed[0].Label);
        Assert.Equal(DialogRole.Cancel, parsed[0].Role);
    }

    [Fact]
    public void Parse_UnknownRoleToken_FallsBackToDefault()
    {
        var parsed = DialogButtons.Parse("Go,warp-speed");

        Assert.Single(parsed);
        Assert.Equal("Go", parsed[0].Label);
        Assert.Equal(DialogRole.Default, parsed[0].Role);
    }

    [Fact]
    public void CancelIndex_FindsTheCancelSlot()
    {
        var parsed = DialogButtons.Parse(DialogButtons.Encode(new[]
        {
            new AlertButton("Copy"), new AlertButton("Move"), AlertButton.Cancel(),
        }));

        Assert.Equal(2, DialogButtons.CancelIndex(parsed));
        Assert.Equal(-1, DialogButtons.CancelIndex(DialogButtons.Parse("Copy,default")));
    }

    [Fact]
    public void Alert_WithoutButtons_ShipsTheDefaultOk()
    {
        var node = Render(new Alert(new State<bool>(true), "Title", "Message", new Text("body")));
        var props = node.GetProperty("props");

        Assert.Equal("Alert", node.GetProperty("type").GetString());
        Assert.True(props.GetProperty("presented").GetBoolean());
        Assert.Equal("Title", props.GetProperty("title").GetString());
        Assert.Equal("Message", props.GetProperty("message").GetString());
        Assert.Equal("OK,cancel", props.GetProperty("buttons").GetString());
        Assert.Equal(1, node.GetProperty("children").GetArrayLength());
    }

    [Fact]
    public void ActionSheet_ShipsTitleMessageAndButtons()
    {
        var node = Render(new ActionSheet(new State<bool>(true), "Pick one",
            new[] { new AlertButton("Copy"), AlertButton.Cancel() }, new Text("body"), message: "Details"));
        var props = node.GetProperty("props");

        Assert.Equal("ActionSheet", node.GetProperty("type").GetString());
        Assert.Equal("Pick one", props.GetProperty("title").GetString());
        Assert.Equal("Details", props.GetProperty("message").GetString());
        Assert.Equal("Copy,default;Cancel,cancel", props.GetProperty("buttons").GetString());
    }

    [Fact]
    public void ButtonIndexPayload_RunsThatButtonAndDismisses()
    {
        var presented = new State<bool>(true);
        var chosen = "";
        var emit = Host(new Alert(presented, "Delete?", "", new[]
        {
            AlertButton.Destructive("Delete", () => chosen = "delete"),
            AlertButton.Cancel("Keep", () => chosen = "keep"),
        }, new Text("body")));

        emit("1");

        Assert.Equal("keep", chosen);
        Assert.False(presented.Value);
    }

    [Fact]
    public void FalsePayload_DismissesWithoutRunningAnyButton()
    {
        // Scrim tap / Esc / system back: the dialog closes but no choice was made.
        var presented = new State<bool>(true);
        var ran = false;
        var emit = Host(new Alert(presented, "T", "M",
            new[] { new AlertButton("Go", DialogRole.Default, () => ran = true) }, new Text("body")));

        emit("false");

        Assert.False(ran);
        Assert.False(presented.Value);
    }

    [Fact]
    public void OutOfRangeIndex_IsIgnoredButStillDismisses()
    {
        var presented = new State<bool>(true);
        var emit = Host(new Alert(presented, "T", "M", new Text("body")));

        emit("7");   // a stale index from a tree that has since re-rendered

        Assert.False(presented.Value);
    }

    [Fact]
    public void ActionRunsAfterDismissal_SoItCanPresentAgain()
    {
        // The flag must already be false when the action runs, otherwise an action that re-presents the
        // same dialog would be immediately clobbered by the dismissal that triggered it.
        var presented = new State<bool>(true);
        bool? flagDuringAction = null;
        var emit = Host(new Alert(presented, "T", "M",
            new[] { new AlertButton("Again", DialogRole.Default, () => flagDuringAction = presented.Value) },
            new Text("body")));

        emit("0");

        Assert.False(flagDuringAction);
    }

    [Fact]
    public void FluentModifiers_BuildTheSameNodesAsTheConstructors()
    {
        var alert = Render(new Text("body").Alert(new State<bool>(false), "T", "M",
            new AlertButton("Yes"), AlertButton.Cancel()));
        Assert.Equal("Alert", alert.GetProperty("type").GetString());
        Assert.Equal("Yes,default;Cancel,cancel", alert.GetProperty("props").GetProperty("buttons").GetString());

        var sheet = Render(new Text("body").ConfirmationDialog(new State<bool>(false), "T",
            new AlertButton("Copy"), AlertButton.Cancel()));
        Assert.Equal("ActionSheet", sheet.GetProperty("type").GetString());
        Assert.Equal("Copy,default;Cancel,cancel", sheet.GetProperty("props").GetProperty("buttons").GetString());

        // The alias exists so UIKit-shaped call sites read naturally; it must build the same node.
        Assert.Equal("ActionSheet", Render(new Text("b")
            .ActionSheet(new State<bool>(false), "T", new AlertButton("Copy"))).GetProperty("type").GetString());
    }

    // ---- helpers -------------------------------------------------------------

    static JsonElement Render(View view)
    {
        var bridge = new CapturingBridge();
        SwiftApp.Run(view, bridge);
        using var doc = JsonDocument.Parse(bridge.LastJson!);
        return doc.RootElement.GetProperty("ops")[0].GetProperty("node").Clone();
    }

    /// <summary>
    /// Runs <paramref name="root"/> and returns a "backend emitted this payload for the root node"
    /// function — the same path a real backend's button click takes.
    /// </summary>
    static Action<string> Host(View root)
    {
        var bridge = new CapturingBridge();
        SwiftApp.Run(root, bridge);
        return payload => bridge.Emit("0", payload);
    }
}

file sealed class CapturingBridge : IBridge
{
    Action<string, string?>? _handler;
    public string? LastJson { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Render(string json) => LastJson = json;
    public void Emit(string id, string? value) => _handler?.Invoke(id, value);
}
