using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input.Touch;

namespace SwiftDotNet;

/// <summary>
/// A <see cref="Game"/> whose whole window is a SwiftDotNet UI — the "MonoGame as an app framework" entry
/// point, for tools, launchers and settings screens rather than a scene with a HUD.
/// </summary>
/// <remarks>
/// <para>For a UI over an existing game, add a <see cref="SwiftDotNetComponent"/> to that game's
/// <c>Components</c> instead; this class is only the wiring you would otherwise write by hand.</para>
/// <code>
/// using var game = new SwiftDotNetGame(() => new ContentView()) { Title = "My tool" };
/// game.Run();
/// </code>
/// </remarks>
public class SwiftDotNetGame : Game
{
    readonly Func<View>? _factory;

    public SwiftDotNetGame(Func<View>? root = null)
    {
        _factory = root;
        Graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 440,
            PreferredBackBufferHeight = 820,
            SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Ui = new SwiftDotNetComponent(this);
    }

    /// <summary>The device manager, so a head can set the window size before <c>Run</c>.</summary>
    public GraphicsDeviceManager Graphics { get; }

    /// <summary>The hosted UI. Set <see cref="SwiftDotNetComponent.Dark"/>, services and so on here.</summary>
    public SwiftDotNetComponent Ui { get; }

    /// <summary>The window title.</summary>
    public string Title
    {
        get => Window.Title;
        set => Window.Title = value;
    }

    /// <summary>Override instead of passing a factory, if the root view needs the game.</summary>
    protected virtual View BuildRoot() =>
        _factory?.Invoke() ?? throw new InvalidOperationException(
            $"{nameof(SwiftDotNetGame)} needs a root view: pass a factory to the constructor or override {nameof(BuildRoot)}.");

    protected override void Initialize()
    {
        Ui.Root ??= BuildRoot();

        // A resized window must re-lay-out. The component rebuilds its surface on its own (it compares
        // the back-buffer rect each Draw); this only makes sure a frame is actually painted.
        Window.ClientSizeChanged += (_, _) => Ui.Invalidate();

        // Touch and mouse are both polled; enabling gestures costs nothing and lets a phone head work.
        TouchPanel.EnabledGestures = GestureType.None;

        Components.Add(Ui);
        base.Initialize();
    }
}
