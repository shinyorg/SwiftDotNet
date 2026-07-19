namespace SwiftDotNet;

/// <summary>Where a presented overlay sits within the window.</summary>
public enum OverlayPosition { Center, Bottom, Top }

/// <summary>Presentation options for <see cref="Overlay.Present"/> (F2).</summary>
public sealed class OverlayOptions
{
    /// <summary>Dim the content behind the overlay with a scrim. Default true.</summary>
    public bool DimBackground { get; init; } = true;

    /// <summary>Tapping the scrim dismisses the overlay. Default true.</summary>
    public bool TapOutsideToDismiss { get; init; } = true;

    /// <summary>Where the overlay sits. Default <see cref="OverlayPosition.Center"/>.</summary>
    public OverlayPosition Position { get; init; } = OverlayPosition.Center;
}

sealed record OverlayEntry(string Id, View Content, OverlayOptions Options);

/// <summary>
/// F2 — the imperative overlay layer. <see cref="Present"/> pushes a view above the whole app from code
/// (the shape Toast/Dialog/LoadingOverlay/FloatingPanel need); <see cref="Dismiss"/> removes it. It stays
/// declarative underneath: the layer is state, and <see cref="OverlayHost"/> lowers it to a <c>ZStack</c>
/// over the root — so it renders on every backend with no per-backend code. Wrap your app root once:
/// <code>new OverlayHost(new ContentView())</code>
/// </summary>
public static class Overlay
{
    static readonly List<OverlayEntry> _entries = new();
    // Bumping this State invalidates the tree so OverlayHost re-reads the entries on the next render.
    static readonly State<int> _version = new(0);
    static int _seq;

    internal static IReadOnlyList<OverlayEntry> Entries => _entries;
    internal static int Version => _version.Value;   // read by OverlayHost so it subscribes to changes

    /// <summary>Present <paramref name="content"/> above the app. Returns an id for <see cref="Dismiss"/>.</summary>
    public static string Present(View content, OverlayOptions? options = null)
    {
        var id = "ovl" + (++_seq);
        _entries.Add(new OverlayEntry(id, content, options ?? new OverlayOptions()));
        _version.Value++;
        return id;
    }

    /// <summary>Dismiss a previously presented overlay by id (no-op if already gone).</summary>
    public static void Dismiss(string id)
    {
        if (_entries.RemoveAll(e => e.Id == id) > 0) _version.Value++;
    }

    /// <summary>Dismiss every active overlay.</summary>
    public static void DismissAll()
    {
        if (_entries.Count == 0) return;
        _entries.Clear();
        _version.Value++;
    }

    // Test/host reset so a new app run starts with a clean layer.
    internal static void Reset() { _entries.Clear(); _seq = 0; _version.Value = 0; }
}

/// <summary>
/// Wraps the app root and renders the <see cref="Overlay"/> layer on top. With no active overlays it is
/// transparent (renders the root as-is); otherwise it stacks scrim + positioned content above the root.
/// </summary>
public sealed class OverlayHost : View
{
    readonly View _root;
    public OverlayHost(View root) => _root = root;

    public override View Body
    {
        get
        {
            _ = Overlay.Version;                 // subscribe: re-render whenever the layer changes
            var entries = Overlay.Entries;
            if (entries.Count == 0) return _root;

            var layers = new List<View> { _root };
            foreach (var e in entries)
            {
                if (e.Options.DimBackground)
                {
                    var scrim = new Rectangle().Background(Color.Hex("#000000")).Opacity(0.4);
                    if (e.Options.TapOutsideToDismiss)
                    {
                        var id = e.Id;             // capture
                        scrim = scrim.OnTapGesture(() => Overlay.Dismiss(id));
                    }
                    layers.Add(scrim);
                }
                // Position the content by aligning it within a full-bleed ZStack layer.
                var aligned = new ZStack(e.Content).Alignment(e.Options.Position switch
                {
                    OverlayPosition.Bottom => Alignment.Bottom,
                    OverlayPosition.Top => Alignment.Top,
                    _ => Alignment.Center,
                });
                layers.Add(aligned);
            }
            return new ZStack(layers.ToArray());
        }
    }
}
