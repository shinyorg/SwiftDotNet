using System.Text.Json;
using SwiftDotNet;
using SwiftDotNet.Controls;
using Xunit;

namespace SwiftDotNet.Tests;

/// <summary>
/// Tests for the ported control library (`SwiftDotNet.Controls`, Plan 2 Waves 0–2 + the Wave-4 slice).
/// These prove each composite lowers to known primitive nodes (so it renders on every backend with no
/// native code) and that the overlay-backed services push/pop the F2 layer.
/// </summary>
[Collection(nameof(SwiftAppSerial))]
public class ControlsTests
{
    // ---- Wave 1: static composites ------------------------------------------

    [Fact]
    public void PillView_LowersToTintedBorderedText()
    {
        var node = Render(new PillView("Live", PillType.Success));
        Assert.Equal("Text", node.GetProperty("type").GetString());
        Assert.Equal("Live", node.GetProperty("props").GetProperty("text").GetString());
        var mods = ModifierTypes(node);
        Assert.Contains("background", mods);
        Assert.Contains("border", mods);
        Assert.Contains("cornerRadius", mods);
    }

    [Fact]
    public void BadgeView_StacksBadgeOverContent()
    {
        var node = Render(new BadgeView(new Text("inbox")).Count(5));
        Assert.Equal("ZStack", node.GetProperty("type").GetString());
        Assert.Contains(Walk(node), n => Text(n) == "inbox");
        Assert.Contains(Walk(node), n => Text(n) == "5");
    }

    [Fact]
    public void BadgeView_CountOverflow_RendersNPlus()
    {
        var node = Render(new BadgeView(new Text("x")).Count(250).MaxCount(99));
        Assert.Contains(Walk(node), n => Text(n) == "99+");
    }

    [Fact]
    public void Fab_LowersToSizedCircleWithIcon()
    {
        var node = Render(new Fab("plus", () => { }));
        Assert.Equal("ZStack", node.GetProperty("type").GetString());
        Assert.Contains(Walk(node), n => n.GetProperty("type").GetString() == "Image");
        Assert.Contains("cornerRadius", ModifierTypes(node));
    }

    [Fact]
    public void Skeleton_LowersToAGradientFilledContainer()
    {
        var node = Render(new SkeletonView(100, 20));
        // A plain container (not a shape) so the gradient fills via the background decoration.
        var bg = node.GetProperty("modifiers").EnumerateArray().First(m => m.GetProperty("type").GetString() == "background");
        Assert.StartsWith("linear:", bg.GetProperty("gradient").GetString());
        Assert.Contains("cornerRadius", ModifierTypes(node));
    }

    // ---- Wave 2: overlay-backed services ------------------------------------

    [Fact]
    public void Dialog_Alert_PushesACenteredCard()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();
        var bridge = new CaptureBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);

        Dialog.Alert("Title", "Message body");
        pump.Drain();

        var root = Root(bridge.Json);
        Assert.Equal("ZStack", root.GetProperty("type").GetString());   // overlay layer engaged
        Assert.Contains(Walk(root), n => Text(n) == "Title");
        Assert.Contains(Walk(root), n => Text(n) == "Message body");
        Overlay.DismissAll();
        pump.Drain();
    }

    [Fact]
    public void Dialog_Confirm_ButtonRunsCallbackAndDismisses()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();
        Overlay.DismissAll();
        pump.Drain();
        var bridge = new CaptureBridge();
        SwiftApp.Run(new OverlayHost(new Text("root")), bridge);

        bool? result = null;
        Dialog.Confirm("Delete?", "Sure?", r => result = r, confirm: "Delete", cancel: "Cancel");
        pump.Drain();

        // Fire the "Delete" button's tap via its registered event id.
        var confirmBtn = FindByText(Root(bridge.Json), "Delete");
        var ev = TapEvent(confirmBtn);
        bridge.Fire(ev, null);
        pump.Drain();

        Assert.True(result);
        Assert.DoesNotContain(Walk(Root(bridge.Json)), n => Text(n) == "Delete");   // dismissed
        Overlay.DismissAll();
        pump.Drain();
    }

    // ---- Wave 4 slice: ImageViewer ------------------------------------------

    [Fact]
    public void ImageViewer_ThumbnailIsATappableRasterImage()
    {
        var node = Render(ImageViewer.FromUrl("https://x/y.png"));
        Assert.Equal("Image", node.GetProperty("type").GetString());
        Assert.Equal("https://x/y.png", node.GetProperty("props").GetProperty("url").GetString());
        Assert.Contains("onTapGesture", ModifierTypes(node));
    }

    // ---- Wave 4 slice: ColorPicker HSB math ---------------------------------

    [Theory]
    [InlineData(0, "#FF0000")]
    [InlineData(120, "#00FF00")]
    [InlineData(240, "#0000FF")]
    public void ColorPicker_HsbToHex_PrimariesAreCorrect(double hue, string expected)
        => Assert.Equal(expected, SwiftDotNet.Controls.ColorPicker.HsbToHex(hue, 1, 1));

    // ---- Wave B features: F6 material, F9 text input ------------------------

    [Fact]
    public void Material_SerializesStyleAndDark()
    {
        var m = Render(new Text("x").Material(MaterialStyle.Thin, dark: true))
            .GetProperty("modifiers").EnumerateArray().First(x => x.GetProperty("type").GetString() == "material");
        Assert.Equal("thin", m.GetProperty("value").GetString());
        Assert.Equal("true", m.GetProperty("dark").GetString());
    }

    [Fact]
    public void FrostedGlass_LowersToAMaterialGroup()
    {
        var node = Render(new FrostedGlassView(new Text("hi")));
        Assert.Contains("material", ModifierTypes(node));
        Assert.Contains(Walk(node), n => Text(n) == "hi");
    }

    [Fact]
    public void TextField_KeyboardAndMaxLength_EmitProps()
    {
        var s = new State<string>("");
        var node = Render(new TextField("PIN", s).Keyboard(KeyboardType.Number).MaxLength(4));
        var props = node.GetProperty("props");
        Assert.Equal("number", props.GetProperty("keyboard").GetString());
        Assert.Equal(4, props.GetProperty("maxLength").GetDouble());
    }

    [Fact]
    public void TextField_MaxLength_TruncatesInBinding()
    {
        var s = new State<string>("");
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(new TextField("x", s).MaxLength(3)), bridge);
        bridge.Fire("0", "abcdef");
        Assert.Equal("abc", s.Value);
    }

    // ---- Wave 3 controls -----------------------------------------------------

    [Fact]
    public void Slider_DragMapsLocationToValue()
    {
        var v = new State<double>(0);
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(new SwiftDotNet.Controls.Slider(v).Width(200)), bridge);
        var ev = DragEvent(Root(bridge.Json));
        bridge.Fire(ev, "c;0,0;100,0;0,0");   // location x=100 of width 200 → 0.5
        Assert.Equal(0.5, v.Value, 3);
    }

    [Fact]
    public void SecurityPin_BoxesReflectEnteredDigits()
    {
        var pin = new State<string>("12");
        var node = Render(new SecurityPin(pin, length: 4));
        // Two filled dots for the two entered digits.
        Assert.Equal(2, Walk(node).Count(n => Text(n) == "●"));
    }

    // ---- Wave 3 data controls -----------------------------------------------

    [Fact]
    public void TableView_LowersToFormOfSections()
    {
        var node = Render(new TableView(
            new TableSection("Account", Cell.Label("Name", "Allan")),
            new TableSection("Danger", Cell.Button("Delete", () => { }, destructive: true))));
        Assert.Equal("Form", node.GetProperty("type").GetString());
        Assert.Equal(2, Walk(node).Count(n => n.GetProperty("type").GetString() == "Section"));
        Assert.Contains(Walk(node), n => Text(n) == "Allan");
    }

    [Fact]
    public void TreeView_BranchLowersToDisclosureGroup_WithChildren()
    {
        var node = Render(new TreeView(
            new TreeNode("Fruit", new TreeNode("Apple"), new TreeNode("Banana"))));
        var dg = Walk(node).First(n => n.GetProperty("type").GetString() == "DisclosureGroup");
        Assert.False(dg.GetProperty("props").GetProperty("expanded").GetBoolean());  // collapsed by default
        Assert.Contains(Walk(dg), n => Text(n) == "Apple");                          // children are in the tree
    }

    [Fact]
    public void TreeView_ExpandState_TogglesViaBoundDisclosure()
    {
        var pump = new ManualPump();
        using var _ = pump.Install();
        var tree = new TreeView(new TreeNode("Fruit", new TreeNode("Apple")));
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(tree), bridge);
        var dg = Walk(Root(bridge.Json)).First(n => n.GetProperty("type").GetString() == "DisclosureGroup");
        bridge.Fire(dg.GetProperty("id").GetString()!, "true");   // the DisclosureGroup emits its own id
        pump.Drain();
        // The prop-only change emits an updateProps patch; assert some op now sets expanded = true.
        using var doc = JsonDocument.Parse(bridge.Json!);
        var ops = doc.RootElement.GetProperty("ops").EnumerateArray().ToList();
        Assert.Contains(ops, op => op.TryGetProperty("props", out var p)
            && p.TryGetProperty("expanded", out var e) && e.ValueKind == JsonValueKind.True);
    }

    [Fact]
    public void SwitchCell_IsAToggle()
        => Assert.Equal("Toggle", Render(Cell.Switch("Notifications", new State<bool>(true))).GetProperty("type").GetString());

    [Fact]
    public void SwipeContainer_HoldsContentAndActionButtons()
    {
        var node = Render(new SwipeContainer(new Text("row"),
            new SwipeAction("Delete", Color.Red, () => { })));
        Assert.Contains(Walk(node), n => Text(n) == "row");
        Assert.Contains(Walk(node), n => Text(n) == "Delete");
    }

    // ---- pickers / grid / scheduler -----------------------------------------

    [Fact]
    public void AutoCompleteEntry_ShowsMatchingSuggestions()
    {
        var text = new State<string>("un");
        var node = Render(new AutoCompleteEntry(text, new[] { "United States", "United Kingdom", "Canada" }));
        Assert.Contains(Walk(node), n => Text(n) == "United States");
        Assert.Contains(Walk(node), n => Text(n) == "United Kingdom");
        Assert.DoesNotContain(Walk(node), n => Text(n) == "Canada");
    }

    [Fact]
    public void DataGrid_RendersHeadersAndAllRows()
    {
        var node = Render(new DataGrid<(string Name, int Age)>(
            new[] { ("Alice", 30), ("Bob", 25) },
            new DataGridColumn<(string Name, int Age)>("Name", x => x.Name),
            new DataGridColumn<(string Name, int Age)>("Age", x => x.Age.ToString())));
        Assert.Contains(Walk(node), n => Text(n) == "Name");
        Assert.Contains(Walk(node), n => Text(n) == "Alice");
        Assert.Contains(Walk(node), n => Text(n) == "Bob");
    }

    [Fact]
    public void SchedulerCalendar_RendersMonthAndDays()
    {
        var month = new State<DateTime>(new DateTime(2026, 7, 1));
        var sel = new State<DateTime>(new DateTime(2026, 7, 15));
        var events = new[] { new CalendarEvent(new DateTime(2026, 7, 15), "Launch", Color.Red) };
        var node = Render(new SchedulerCalendarView(month, sel, events));
        Assert.Contains(Walk(node), n => Text(n) == "July 2026");
        Assert.Contains(Walk(node), n => Text(n) == "15");
        Assert.Contains(Walk(node), n => Text(n) == "31");
    }

    // ---- ChatView / DurationPicker ------------------------------------------

    [Fact]
    public void ChatView_RendersBubblesInputAndTyping()
    {
        var draft = new State<string>("");
        var node = Render(new ChatView(new[]
        {
            new ChatMessage("Hi there", IsMine: false, Sender: "Alex"),
            new ChatMessage("Hello!", IsMine: true),
        }, draft).Typing().OnLoadMore(() => { }));

        Assert.Contains(Walk(node), n => Text(n) == "Hi there");
        Assert.Contains(Walk(node), n => Text(n) == "Hello!");
        Assert.Contains(Walk(node), n => Text(n) == "Alex");        // sender label on incoming
        Assert.Contains(Walk(node), n => Text(n) == "Load earlier");
        Assert.Contains(Walk(node), n => Text(n) == "Send");         // styled send affordance
        // The typing indicator is three animated dots (Circles), not a text glyph.
        Assert.True(Walk(node).Count(n => n.GetProperty("type").GetString() == "Circle") >= 3);
    }

    [Fact]
    public void ChatView_SendButton_InvokesHandlerWithDraft()
    {
        var draft = new State<string>("ping");
        string? sent = null;
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(new ChatView(Array.Empty<ChatMessage>(), draft).OnSend(t => sent = t)), bridge);
        // Send is a styled tappable Text; fire its onTapGesture event id.
        var send = Walk(Root(bridge.Json)).First(n => Text(n) == "Send");
        bridge.Fire(TapEvent(send), null);
        Assert.Equal("ping", sent);
    }

    [Fact]
    public void DurationPicker_ShowsColumnsAndValue()
    {
        var span = new State<TimeSpan>(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5));
        var node = Render(new DurationPicker(span));
        Assert.Contains(Walk(node), n => Text(n) == "Hours");
        Assert.Contains(Walk(node), n => Text(n) == "01");   // 1 hour, zero-padded
        Assert.Contains(Walk(node), n => Text(n) == "05");   // 5 minutes
    }

    // ---- collections: staggered / carousel / reorder ------------------------

    [Fact]
    public void StaggeredGrid_DistributesItemsAcrossColumns()
    {
        var node = Render(new StaggeredGrid(2,
            new Text("a"), new Text("b"), new Text("c"), new Text("d")));
        Assert.Equal("HStack", node.GetProperty("type").GetString());
        Assert.Equal(2, node.GetProperty("children").GetArrayLength());     // two column stacks
        foreach (var t in new[] { "a", "b", "c", "d" })
            Assert.Contains(Walk(node), n => Text(n) == t);
    }

    [Fact]
    public void CarouselGallery_LowersToPagedTabView()
    {
        var node = Render(new CarouselGallery(new State<int>(0), new Text("p1"), new Text("p2")));
        Assert.Equal("TabView", node.GetProperty("type").GetString());
        Assert.Equal("page", node.GetProperty("props").GetProperty("style").GetString());   // paged carousel
    }

    [Fact]
    public void ReorderableList_DragMovesItem()
    {
        var items = new State<List<string>>(new List<string> { "A", "B", "C" });
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(new ReorderableList<string>(items, s => new Text(s)).RowHeight(50)), bridge);

        var firstRow = Root(bridge.Json).GetProperty("children")[0];
        bridge.Fire(DragEvent(firstRow), "e;0,100;0,0;0,0");   // drag row 0 down 100pt / 50 = 2 slots

        Assert.Equal(new[] { "B", "C", "A" }, items.Value);
    }

    // ---- helpers -------------------------------------------------------------

    static string DragEvent(JsonElement node) =>
        node.GetProperty("modifiers").EnumerateArray()
            .First(m => m.GetProperty("type").GetString() == "onDrag").GetProperty("event").GetString()!;

    static JsonElement Render(View view)
    {
        var bridge = new CaptureBridge();
        SwiftApp.Run(new ControlHost(view), bridge);
        return Root(bridge.Json);
    }

    static JsonElement Root(string? json)
    {
        var op = JsonDocument.Parse(json!).RootElement.GetProperty("ops").EnumerateArray().First();
        return (op.GetProperty("op").GetString() == "replace" ? op.GetProperty("node") : op).Clone();
    }

    static IEnumerable<JsonElement> Walk(JsonElement node)
    {
        yield return node;
        if (node.TryGetProperty("children", out var kids))
            foreach (var c in kids.EnumerateArray())
                foreach (var d in Walk(c)) yield return d;
    }

    static string Text(JsonElement n) =>
        n.TryGetProperty("props", out var p) && p.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

    static List<string?> ModifierTypes(JsonElement n) =>
        n.GetProperty("modifiers").EnumerateArray().Select(m => m.GetProperty("type").GetString()).ToList();

    static JsonElement FindByText(JsonElement root, string text) => Walk(root).First(n => Text(n) == text);

    static string TapEvent(JsonElement node) =>
        node.GetProperty("modifiers").EnumerateArray()
            .First(m => m.GetProperty("type").GetString() == "onTapGesture")
            .GetProperty("event").GetString()!;
}

file sealed class ControlHost(View child) : View { public override View Body => child; }

file sealed class CaptureBridge : IBridge
{
    Action<string, string?>? _handler;
    public string? Json { get; private set; }
    public void SetEventHandler(Action<string, string?> handler) => _handler = handler;
    public void Render(string json) => Json = json;
    public void Fire(string id, string? value) => _handler!(id, value);
}

file sealed class ManualPump : SynchronizationContext
{
    readonly Queue<(SendOrPostCallback cb, object? state)> _q = new();
    public override void Post(SendOrPostCallback d, object? state) => _q.Enqueue((d, state));
    public void Drain() { while (_q.Count > 0) { var (cb, s) = _q.Dequeue(); cb(s); } }
    public IDisposable Install() { var prev = Current; SetSynchronizationContext(this); return new R(prev); }
    sealed class R(SynchronizationContext? p) : IDisposable { public void Dispose() => SetSynchronizationContext(p); }
}
