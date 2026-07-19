using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>Status &amp; progress controls: pills, badges, skeleton, progress bar.</summary>
public sealed class StatusSample : View
{
    public override View Body =>
        new ScrollView(new VStack(
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
                new BadgeView(new Text("🔔 Alerts").Font(Font.Body)).Dot().Color(Color.Hex("#007AFF")),
                new BadgeView(new Text("🛒 Cart").Font(Font.Body)).Count(250).MaxCount(99)
            ).Spacing(24),

            new Text("Skeleton").Font(Font.Headline),
            new VStack(
                new SkeletonView(220, 16),
                new SkeletonView(180, 16),
                new SkeletonView(120, 16)
            ).Spacing(8).Alignment(HorizontalAlignment.Leading),

            new Text("Progress").Font(Font.Headline),
            new ProgressBar(0.65).Label("Uploading")
        ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Status");
}
