using Foundation;

namespace SwiftDotNet;

/// <summary>
/// The Apple <see cref="ISurfaceChannel"/>: a <see cref="FileSurfaceChannel"/> rooted in the App Group
/// container, plus the <c>WidgetCenter</c> nudge.
///
/// <para>The storage half needs no platform code at all, which is a pleasant surprise given how much of
/// the rest of Apple support does. An App Group container is literally a directory - obtained through
/// <c>NSFileManager.GetContainerUrl</c> - and the widget extension reads the very same files from Swift.
/// So the shared base class is not an abstraction over two different stores; on Apple it IS the store.</para>
///
/// <para><b>The failure mode to know about:</b> a wrong or unentitled App Group id does not throw. The
/// container URL comes back null, or the extension silently gets a different directory, and the widget
/// renders a placeholder forever with no error anywhere. That is why the constructor throws on a missing
/// container rather than falling back to a temp directory that would appear to work in the app and never
/// work in the extension.</para>
/// </summary>
public sealed class AppleSurfaceChannel : FileSurfaceChannel
{
    /// <param name="appGroup">
    /// The App Group identifier, which must be entitled on <b>both</b> the app and the widget extension.
    /// </param>
    public AppleSurfaceChannel(string appGroup)
        : base(ContainerFor(appGroup))
        => AppGroup = appGroup;

    /// <summary>The App Group this channel is rooted in.</summary>
    public string AppGroup { get; }

    /// <inheritdoc />
    protected override Task OnPublishedAsync(SurfaceSnapshot snapshot, CancellationToken ct)
    {
        // A request, not a command: WidgetKit decides when to actually ask the provider, and spends the
        // app's daily refresh budget when it does. An activity needs no nudge - its state travels inside
        // the ActivityKit payload rather than through this container.
        if (snapshot.Surface == LiveSurface.Widget)
            AppleLiveBridge.ReloadWidgets(snapshot.Kind);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnWithdrawnAsync(string kind, CancellationToken ct)
    {
        AppleLiveBridge.ReloadWidgets(kind);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task<IReadOnlyList<SurfacePlacement>> GetPlacementsAsync(CancellationToken ct = default)
    {
        // WidgetCenter.getCurrentConfigurations is the only honest source: most apps have no widget
        // placed at all, and rendering every family for a widget nobody added is pure waste.
        var raw = AppleLiveBridge.TakeString(AppleLiveBridge.Placements());
        var placements = new List<SurfacePlacement>();

        if (!string.IsNullOrEmpty(raw))
        {
            foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var colon = entry.LastIndexOf(':');
                if (colon <= 0) continue;

                var kind = entry.Substring(0, colon);
                placements.Add(new SurfacePlacement(
                    kind,
                    Enum.TryParse<WidgetFamily>(entry.Substring(colon + 1), out var family) ? family : null,
                    ""));
            }
        }

        return Task.FromResult<IReadOnlyList<SurfacePlacement>>(placements);
    }

    static string ContainerFor(string appGroup)
    {
        var url = new NSFileManager().GetContainerUrl(appGroup);
        if (url?.Path is not { } path)
        {
            throw new InvalidOperationException(
                $"No container for App Group '{appGroup}'. Add it to the app's entitlements (and the " +
                "widget extension's), and check the identifier matches exactly. This cannot be worked " +
                "around with a local directory: the widget extension is a separate process and reads " +
                "the container directly.");
        }

        return Path.Combine(path, "swiftdotnet-surfaces");
    }
}
