using System.Globalization;
using System.Text;

namespace SwiftDotNet;

/// <summary>
/// A directory-backed <see cref="ISurfaceChannel"/> — and the base class both platform channels derive
/// from, because on both of them the shared store really is just a directory.
///
/// On Apple, an App Group container is obtained as a file URL
/// (<c>containerURL(forSecurityApplicationGroupIdentifier:)</c>) and the widget extension reads the very
/// same files; on Android the app's own files directory is trivially shared with an
/// <c>AppWidgetProvider</c> in the same process. So the storage half is genuinely common, and the
/// platform subclasses add only the *nudge* — <c>WidgetCenter.reloadTimelines</c> or
/// <c>AppWidgetManager.updateAppWidget</c> — by overriding <see cref="OnPublishedAsync"/>.
///
/// It is also directly useful on its own: a plain net10.0 channel means the whole publish → read →
/// action-drain round trip is testable headlessly on any OS, without a simulator or an emulator.
///
/// The on-disk format is hand-rolled and delimited rather than JSON, for the same reason
/// <see cref="Brush"/>'s is: a Swift widget extension parses these files under memory pressure with no
/// JSON decoder worth setting up, and a line split is the cheapest correct thing on both sides.
/// </summary>
public class FileSurfaceChannel : ISurfaceChannel
{
    const string SnapshotSuffix = ".surface";
    const string MailboxFile = "actions.log";

    readonly string _root;
    readonly SemaphoreSlim _mailboxLock = new(1, 1);

    /// <param name="root">The shared container directory. Created if missing.</param>
    public FileSurfaceChannel(string root)
    {
        _root = root;
        Directory.CreateDirectory(_root);
    }

    /// <summary>The shared container this channel reads and writes.</summary>
    public string Root => _root;

    /// <inheritdoc />
    public async Task PublishAsync(SurfaceSnapshot snapshot, CancellationToken ct = default)
    {
        var path = PathFor(snapshot.Kind);
        // Write-then-move: a widget extension can be launched by the system mid-write, and a half-written
        // snapshot renders as a blank surface. The rename is atomic on both platforms' filesystems.
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, Encode(snapshot), Encoding.UTF8, ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);

        await OnPublishedAsync(snapshot, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WithdrawAsync(string kind, CancellationToken ct = default)
    {
        var path = PathFor(kind);
        if (File.Exists(path)) File.Delete(path);
        await OnWithdrawnAsync(kind, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SurfaceSnapshot?> ReadAsync(string kind, CancellationToken ct = default)
    {
        var path = PathFor(kind);
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
        return TryDecode(text, out var snapshot) ? snapshot : null;
    }

    /// <inheritdoc />
    public virtual Task<IReadOnlyList<SurfacePlacement>> GetPlacementsAsync(CancellationToken ct = default)
    {
        // With no platform host to ask, every published kind counts as placed. The platform subclasses
        // override this with the real query (WidgetCenter / AppWidgetManager), which can and does return
        // fewer — most apps have no widget placed at all.
        var placements = new List<SurfacePlacement>();
        foreach (var file in Directory.EnumerateFiles(_root, "*" + SnapshotSuffix))
            placements.Add(new SurfacePlacement(Path.GetFileNameWithoutExtension(file), null, ""));
        return Task.FromResult<IReadOnlyList<SurfacePlacement>>(placements);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SurfaceAction>> DrainActionsAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(_root, MailboxFile);
        await _mailboxLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path)) return Array.Empty<SurfaceAction>();

            var lines = await File.ReadAllLinesAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
            File.Delete(path);

            var actions = new List<SurfaceAction>(lines.Length);
            foreach (var line in lines)
            {
                if (line.Length == 0) continue;
                // A corrupt line must never take down app startup — the mailbox is appended to by a
                // separate process that the system can kill mid-write.
                if (SurfaceAction.TryParse(line, out var a)) actions.Add(a);
            }
            return actions;
        }
        finally
        {
            _mailboxLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task PostActionAsync(SurfaceAction action, CancellationToken ct = default)
    {
        var path = Path.Combine(_root, MailboxFile);
        await _mailboxLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(path, action.ToLine() + "\n", Encoding.UTF8, ct).ConfigureAwait(false);
        }
        finally
        {
            _mailboxLock.Release();
        }
    }

    /// <summary>The outbound nudge. Overridden by the platform channels; a no-op for the plain file channel.</summary>
    protected virtual Task OnPublishedAsync(SurfaceSnapshot snapshot, CancellationToken ct) => Task.CompletedTask;

    /// <summary>Tells the host to stop showing a surface. Overridden by the platform channels.</summary>
    protected virtual Task OnWithdrawnAsync(string kind, CancellationToken ct) => Task.CompletedTask;

    string PathFor(string kind) => Path.Combine(_root, Sanitize(kind) + SnapshotSuffix);

    /// <summary>A kind is a developer-supplied string and ends up as a filename; keep it to a safe alphabet.</summary>
    static string Sanitize(string kind)
    {
        var sb = new StringBuilder(kind.Length);
        foreach (var c in kind)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        return sb.Length == 0 ? "_" : sb.ToString();
    }

    // ---- Wire format --------------------------------------------------------------------------
    // Line 1:  kind|surface|publishedAt|refreshAfter
    // Line n:  variantKey<TAB>tree-json
    //
    // Tab is a safe separator because the tree is compact JSON and NodeJson escapes any literal tab
    // inside a string as \t, so one can never appear raw in the payload.
    //
    // Public rather than internal because this is a cross-language contract, not an implementation
    // detail: the Swift shim parses exactly this format (SDNSurfaceStore.decode), and the Apple driver
    // uses Encode to hand a snapshot across the C ABI without a second serializer.

    /// <summary>Serializes a snapshot to the shared on-disk / cross-ABI format.</summary>
    public static string Encode(SurfaceSnapshot s)
    {
        var sb = new StringBuilder(512);
        sb.Append(s.Kind).Append('|')
          .Append(s.Surface.ToString()).Append('|')
          .Append(s.PublishedAt.ToString("0.###", CultureInfo.InvariantCulture)).Append('|')
          .Append(s.RefreshAfter?.ToString("0.###", CultureInfo.InvariantCulture) ?? "")
          .Append('\n');

        foreach (var kv in s.Trees)
            sb.Append(kv.Key).Append('\t').Append(kv.Value).Append('\n');

        return sb.ToString();
    }

    /// <summary>Parses <see cref="Encode"/>'s output. False on anything malformed.</summary>
    public static bool TryDecode(string text, out SurfaceSnapshot snapshot)
    {
        snapshot = null!;
        var lines = text.Split('\n');
        if (lines.Length == 0) return false;

        var head = lines[0].Split('|');
        if (head.Length != 4) return false;
        if (!Enum.TryParse<LiveSurface>(head[1], out var surface)) return false;
        if (!double.TryParse(head[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var published))
            return false;

        double? refreshAfter = double.TryParse(head[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
            ? r : null;

        var trees = new Dictionary<string, string>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (lines[i].Length == 0) continue;
            var tab = lines[i].IndexOf('\t');
            if (tab < 0) continue;
            trees[lines[i].Substring(0, tab)] = lines[i].Substring(tab + 1);
        }

        snapshot = new SurfaceSnapshot
        {
            Kind = head[0],
            Surface = surface,
            Trees = trees,
            PublishedAt = published,
            RefreshAfter = refreshAfter,
        };
        return true;
    }
}
