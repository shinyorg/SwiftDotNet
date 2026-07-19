using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>
/// A tour of the ported <c>SwiftDotNet.Controls</c> library (Plan 2 Waves 0–2 + Wave-4 slice): pills,
/// badges, skeletons, FABs, and the overlay-backed Toast/Dialog/Loading services, plus the ColorPicker.
/// Overlays require the app root to be wrapped in an <see cref="OverlayHost"/> (the sample hosts do this).
/// </summary>
public sealed class ControlsDemo : View
{
    readonly State<double> _hue = State(200.0);
    readonly State<string> _picked = State("#00AAFF");
    readonly State<double> _slider = State(0.4);
    readonly State<double> _rangeLo = State(0.25);
    readonly State<double> _rangeHi = State(0.75);
    readonly State<string> _pin = State("12");
    readonly State<string> _entry = State("");

    public override View Body =>
        new ScrollView(
            new VStack(
            new Text("Shiny Controls").Font(Font.LargeTitle),

            new Text("Pills").Font(Font.Headline),
            new HStack(
                new PillView("None"),
                new PillView("Success", PillType.Success),
                new PillView("Warning", PillType.Warning),
                new PillView("Critical", PillType.Critical)
            ).Spacing(8),

            new Text("Badges").Font(Font.Headline),
            new HStack(
                new BadgeView(new Text("📥 Inbox").Font(Font.Body)).Count(12),
                new BadgeView(new Text("🔔 Alerts").Font(Font.Body)).Dot().Color(ControlPalette_AccentInfo),
                new BadgeView(new Text("🛒 Cart").Font(Font.Body)).Count(250).MaxCount(99)
            ).Spacing(24),

            new Text("Skeleton").Font(Font.Headline),
            new VStack(
                new SkeletonView(220, 16),
                new SkeletonView(180, 16),
                new SkeletonView(120, 16)
            ).Spacing(8).Alignment(HorizontalAlignment.Leading),

            new Text("Progress").Font(Font.Headline),
            new ProgressBar(0.65).Label("Uploading"),

            new Text("Sliders").Font(Font.Headline),
            new SwiftDotNet.Controls.Slider(_slider),
            new RangeSlider(_rangeLo, _rangeHi),

            new Text("PIN & Entry").Font(Font.Headline),
            new SecurityPin(_pin, length: 4),
            new TextEntry("Email", _entry).Keyboard(KeyboardType.Email),

            new Text("Tree").Font(Font.Headline),
            new TreeView(
                new TreeNode("Documents", "folder",
                    new TreeNode("Resume.pdf", "doc"),
                    new TreeNode("Photos", "folder",
                        new TreeNode("beach.jpg", "photo"),
                        new TreeNode("hike.jpg", "photo"))),
                new TreeNode("Music", "folder",
                    new TreeNode("song.mp3", "music"))
            ).OnSelect(n => Toast.Show($"Selected {n.Label}")),

            new Text("Swipe row (drag left)").Font(Font.Headline),
            new SwipeContainer(
                new HStack(new Text("Swipe me left"), new Spacer()).Padding(Edge.Horizontal, 12),
                new SwipeAction("Delete", Color.Red, () => Toast.Show("Deleted")))
                .RowHeight(48),

            new Text("Frosted glass").Font(Font.Headline),
            new FrostedGlassView(new Text("Blurred backdrop panel").ForegroundColor(ControlPalette_AccentInfo))
                .Style(MaterialStyle.Thin),

            new Text("Color Picker").Font(Font.Headline),
            new SwiftDotNet.Controls.ColorPicker(_hue).OnColorChanged(hex => _picked.Value = hex),
            new Text(_picked.Value).Font(Font.Caption),

            new Text("Overlays (tap)").Font(Font.Headline),
            new HStack(
                new Button("Toast", () => Toast.Show("Saved ✓", style: PillType.Success)),
                new Button("Alert", () => Dialog.Alert("Heads up", "This is an in-app dialog.")),
                new Button("Confirm", () => Dialog.Confirm("Delete?", "This cannot be undone.",
                    ok => Toast.Show(ok ? "Deleted" : "Cancelled"), confirm: "Delete", destructive: true))
            ).Spacing(12),
            new Button("Loading (2s)", () =>
            {
                var id = Loading.Show("Working…");
                _ = HideAfter(id, 2);
            }),

            new Text("FAB").Font(Font.Headline),
            new Fab("plus", () => Toast.Show("FAB tapped")),

            new Button("Floating panel", () => FloatingPanel.Present(
                new VStack(
                    new Text("Bottom sheet").Font(Font.Headline),
                    new Text("Drag the handle down to dismiss."))
                .Spacing(8))),

            new Text("FAB menu").Font(Font.Headline),
            new FabMenu(
                new FabMenuItem("camera", "Photo", () => Toast.Show("Photo")),
                new FabMenuItem("mic", "Record", () => Toast.Show("Record")),
                new FabMenuItem("doc", "File", () => Toast.Show("File")))
            ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Shiny Controls");

    static readonly SwiftColor ControlPalette_AccentInfo = Color.Hex("#007AFF");

    static async System.Threading.Tasks.Task HideAfter(string id, double seconds)
    {
        await System.Threading.Tasks.Task.Delay(System.TimeSpan.FromSeconds(seconds));
        Loading.Hide(id);
    }
}
