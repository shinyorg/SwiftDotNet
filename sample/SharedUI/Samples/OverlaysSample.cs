using System;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>Buttons, overlay services, and media overlays: FABs, toast/dialog/loading, floating panel, image viewer, frosted glass.</summary>
public sealed class OverlaysSample : View
{
    public override View Body =>
        new ScrollView(new VStack(
            new Text("Overlay services (tap)").Font(Font.Headline),
            new HStack(
                new Button("Toast", () => Toast.Show("Saved ✓", style: PillType.Success)),
                new Button("Alert", () => Dialog.Alert("Heads up", "This is an in-app dialog.")),
                new Button("Confirm", () => Dialog.Confirm("Delete?", "This cannot be undone.",
                    ok => Toast.Show(ok ? "Deleted" : "Cancelled"), confirm: "Delete", destructive: true))
            ).Spacing(12),
            new HStack(
                new Button("Loading (2s)", () => { var id = Loading.Show("Working…"); _ = HideAfter(id, 2); }),
                new Button("Panel", () => FloatingPanel.Present(
                    new VStack(new Text("Bottom sheet").Font(Font.Headline),
                        new Text("Drag the handle down to dismiss.")).Spacing(8)))
            ).Spacing(12),

            new Text("FAB").Font(Font.Headline),
            new HStack(
                new Fab("plus", () => Toast.Show("FAB tapped")),
                new FabMenu(
                    new FabMenuItem("camera", "Photo", () => Toast.Show("Photo")),
                    new FabMenuItem("mic", "Record", () => Toast.Show("Record")),
                    new FabMenuItem("doc", "File", () => Toast.Show("File")))
            ).Spacing(40).Alignment(VerticalAlignment.Bottom),

            new Text("Image Viewer (tap)").Font(Font.Headline),
            ImageViewer.FromUrl("https://picsum.photos/400/300").ThumbnailSize(120),

            new Text("Frosted glass").Font(Font.Headline),
            new Text("Glass only reads over content — this one sits on a gradient backdrop.")
                .Font(Font.Caption).ForegroundColor(Color.Secondary),
            new FrostedGlassView(
                    new VStack(
                        new Text("Frosted panel").Font(Font.Headline),
                        new Text(".Material(.Thin) over a gradient").Font(Font.Caption))
                    .Spacing(4))
                .Over(new ZStack()
                    .Frame(320, 170)
                    .Background(new LinearGradient(35,
                        new GradientStop(Color.Hex("#FF5252"), 0),
                        new GradientStop(Color.Hex("#7C4DFF"), 0.5),
                        new GradientStop(Color.Hex("#00BCD4"), 1)))
                    .CornerRadius(18))
                .Style(MaterialStyle.Thin)
        ).Spacing(16).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Overlays & Media");

    static async System.Threading.Tasks.Task HideAfter(string id, double seconds)
    {
        await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(seconds));
        Loading.Hide(id);
    }
}
