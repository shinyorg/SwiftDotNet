using Android.Appwidget;
using Android.Content;

namespace SwiftDotNet;

/// <summary>
/// The Android <see cref="ISurfaceChannel"/>: a <see cref="FileSurfaceChannel"/> under the app's files
/// directory, plus the <c>AppWidgetManager</c> nudge.
///
/// The storage half needs nothing platform-specific because there is no process boundary to cross. An
/// <c>AppWidgetProvider</c> is a <c>BroadcastReceiver</c> in our own process and can read our own files.
/// The snapshots are still written, though, and deliberately: a provider can be woken by a broadcast long
/// after the app process died, and re-reading a published snapshot is both cheaper and more faithful than
/// re-running the app's timeline code from a cold start.
/// </summary>
public sealed class AndroidSurfaceChannel : FileSurfaceChannel
{
    readonly Context _context;

    public AndroidSurfaceChannel(Context context)
        : base(Path.Combine(context.FilesDir?.AbsolutePath ?? Path.GetTempPath(), "swiftdotnet-surfaces"))
        => _context = context;

    /// <inheritdoc />
    protected override Task OnPublishedAsync(SurfaceSnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.Surface == LiveSurface.Widget)
        {
            // Ask every provider in this app to re-run onUpdate. Broadcasting rather than calling
            // updateAppWidget directly keeps this class from having to know which provider owns the kind.
            var intent = new Intent(AppWidgetManager.ActionAppwidgetUpdate)
                .SetPackage(_context.PackageName);
            _context.SendBroadcast(intent);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<SurfacePlacement>> GetPlacementsAsync(CancellationToken ct = default)
    {
        var manager = AppWidgetManager.GetInstance(_context);
        if (manager is null) return base.GetPlacementsAsync(ct);

        var placements = new List<SurfacePlacement>();

        // AppWidgetManager reports instances per provider component, and a provider's own class name is
        // the only stable link back to a kind, so a provider is expected to name itself after its kind.
        var providers = manager.InstalledProviders;
        if (providers is null) return base.GetPlacementsAsync(ct);

        foreach (var info in providers)
        {
            var component = info?.Provider;
            if (component is null || component.PackageName != _context.PackageName) continue;

            var ids = manager.GetAppWidgetIds(component);
            if (ids is null) continue;

            foreach (var id in ids)
            {
                var options = manager.GetAppWidgetOptions(id);
                var family = WidgetFamilies.FromOptions(
                    options?.GetInt(AppWidgetManager.OptionAppwidgetMinWidth) ?? 0,
                    options?.GetInt(AppWidgetManager.OptionAppwidgetMinHeight) ?? 0);

                placements.Add(new SurfacePlacement(
                    component.ShortClassName?.TrimStart('.') ?? "", family, id.ToString()));
            }
        }

        return Task.FromResult<IReadOnlyList<SurfacePlacement>>(placements);
    }
}

/// <summary>Maps Android's continuous widget size grid onto the family enum the DSL switches on.</summary>
public static class WidgetFamilies
{
    /// <summary>
    /// Buckets a widget's minimum size (in dp, as reported by <c>OPTION_APPWIDGET_MIN_WIDTH/HEIGHT</c>)
    /// onto the nearest Apple-shaped family.
    ///
    /// The thresholds follow Android's own launcher grid, where a cell is roughly 70 dp: a 2x2 is about
    /// 110 dp square, a 4x2 about 250x110, a 4x4 about 250x250. The lock-screen accessories are never
    /// produced here. Android has no lock-screen widget host, so those families are Apple-only and the
    /// validator says so rather than letting an app render into nothing.
    /// </summary>
    public static WidgetFamily FromOptions(int minWidthDp, int minHeightDp)
    {
        if (minWidthDp >= 500) return WidgetFamily.ExtraLarge;
        if (minWidthDp >= 180 && minHeightDp >= 180) return WidgetFamily.Large;
        if (minWidthDp >= 180) return WidgetFamily.Medium;
        return WidgetFamily.Small;
    }
}
