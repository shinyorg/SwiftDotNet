using System.Reflection;
using SwiftDotNet;

namespace SwiftDotNet.Preview;

/// <summary>
/// One loaded-and-running preview: the load context, the view, the Skia bridge, and the headless host
/// that turns them into PNG frames. Reloading means throwing one of these away and building another —
/// which is the whole point of keeping them in a single disposable object.
/// </summary>
sealed class PreviewSession : IDisposable
{
    readonly PreviewLoadContext _context;
    readonly SkiaBridge _bridge;
    readonly SkiaImageHost _host;

    bool _dirty = true;

    PreviewSession(PreviewLoadContext context, SkiaBridge bridge, SkiaImageHost host, string description)
    {
        _context = context;
        _bridge = bridge;
        _host = host;
        Description = description;
    }

    /// <summary>What is being previewed, for the IDE's status line.</summary>
    public string Description { get; }

    public bool Dark
    {
        get => _host.Dark;
        set
        {
            if (_host.Dark == value)
                return;
            _host.Dark = value;
            _dirty = true;
        }
    }

    public static PreviewSession Load(PreviewOptions options)
    {
        var context = new PreviewLoadContext(options.AssemblyPath);
        var assembly = context.LoadMain();

        // Renderer registration is a *startup* step the real head performs (SkiaSampleRenderers.RegisterAll
        // in the Silk sample), and the preview loads a view assembly rather than a head — so without this
        // hook every custom control previews as the ⚠️ placeholder. See docs/custom-controls.md.
        RunInitializer(assembly, options.Initializer);

        var resolved = PreviewRoot.Resolve(assembly, options.ViewTypeName);

        var bridge = new SkiaBridge();
        var host = new SkiaImageHost(bridge) { Dark = options.Dark };
        var session = new PreviewSession(context, bridge, host, resolved.Description);

        bridge.Invalidate += session.MarkDirty;
        SwiftApp.Run(resolved.Root, bridge, resolved.Services);

        return session;
    }

    static void RunInitializer(Assembly assembly, string? initializer)
    {
        if (string.IsNullOrWhiteSpace(initializer))
            return;

        var split = initializer.LastIndexOf('.');
        if (split <= 0)
            throw new PreviewException($"--init expects Type.Method, got '{initializer}'.");

        var typeName = initializer[..split];
        var methodName = initializer[(split + 1)..];

        var type = assembly.GetType(typeName, throwOnError: false)
                   ?? throw new PreviewException($"--init: no type '{typeName}' in the preview assembly.");
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)
                     ?? throw new PreviewException($"--init: no public static '{methodName}' on '{typeName}'.");

        method.Invoke(null, method.GetParameters().Length == 0 ? [] : new object?[method.GetParameters().Length]);
    }

    void MarkDirty() => _dirty = true;

    /// <summary>
    /// Advance animations and report whether a new frame is owed. Returning "nothing changed" is what
    /// keeps an idle preview at roughly zero CPU — a preview that pegs a core while you read code is a
    /// preview people turn off.
    /// </summary>
    public bool Step(double dt)
    {
        var animating = _host.Advance(dt);
        return _dirty || animating;
    }

    public byte[] RenderPng(int width, int height)
    {
        _dirty = false;
        return _host.RenderPng(width, height);
    }

    /// <summary>
    /// Apply one input command from the IDE. Runs on the render-loop thread, never on the socket thread,
    /// because everything it touches ends in a tree rebuild.
    /// </summary>
    public void HandleInput(string payload)
    {
        var parts = payload.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return;

        switch (parts[0])
        {
            case "tap" when parts.Length >= 3:
                _host.Tap(F(parts[1]), F(parts[2]));
                break;
            case "scroll" when parts.Length >= 4:
                _host.Scroll(F(parts[1]), F(parts[2]), F(parts[3]));
                break;
            case "longpress" when parts.Length >= 3:
                _host.LongPress(F(parts[1]), F(parts[2]));
                break;
            case "swipe" when parts.Length >= 4:
                _host.Swipe(F(parts[1]), F(parts[2]), parts[3]);
                break;
            case "drag" when parts.Length >= 6:
                _host.Drag(F(parts[1]), F(parts[2]), ParsePhase(parts[3]), F(parts[4]), F(parts[5]),
                    parts.Length > 6 ? F(parts[6]) : 0, parts.Length > 7 ? F(parts[7]) : 0);
                break;
            case "magnify" when parts.Length >= 5:
                _host.Magnify(F(parts[1]), F(parts[2]), ParsePhase(parts[3]), F(parts[4]));
                break;
            case "backspace":
                _host.Backspace();
                break;
            case "text" when parts.Length >= 2:
                // Text is the rest of the line verbatim — it can contain spaces, so it cannot be split.
                _host.Type(payload[(payload.IndexOf(' ') + 1)..]);
                break;
        }

        _dirty = true;
    }

    static GesturePhase ParsePhase(string value) => value switch
    {
        "began" => GesturePhase.Began,
        "ended" => GesturePhase.Ended,
        _ => GesturePhase.Changed,
    };

    static float F(string value) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

    public void Dispose()
    {
        // Unsubscribe before unloading: the bridge outlives nothing here, but a live delegate rooted in
        // the old context is precisely what keeps a collectible context from actually collecting.
        _bridge.Invalidate -= MarkDirty;
        _context.Unload();
    }
}
