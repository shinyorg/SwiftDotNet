namespace SwiftDotNet;

/// <summary>Navigation for car screens. Obtain it from <see cref="CarScreen.Navigator"/>.</summary>
public interface ICarNavigator
{
    IServiceProvider Services { get; }
    CarLimits Limits { get; }
    /// <summary>The connected platform, when known.</summary>
    CarPlatform? Platform { get; }
    /// <summary>Whether the app's <see cref="CarOptions.Category"/> may use a template type on this platform.</summary>
    bool Supports<TTemplate>() where TTemplate : CarTemplate;
    IReadOnlyList<CarScreen> Stack { get; }
    CarScreen? Top { get; }
    bool IsPresenting { get; }

    /// <summary>Pushes a screen. Throws <see cref="CarConstraintException"/> beyond <see cref="CarLimits.MaxDepth"/>.</summary>
    void Push(CarScreen screen);
    void Pop();
    void PopToRoot();
    /// <summary>
    /// Presents an alert or action sheet. Tapping any action dismisses it before the action's handler runs,
    /// on both platforms.
    /// </summary>
    void Present(CarModalTemplate modal);
    void Dismiss();
}

/// <summary>Implemented by the platform renderers (CarPlay / Android Auto).</summary>
public interface ICarRenderer
{
    /// <summary>Called once from the <see cref="CarNavigator"/> constructor.</summary>
    void Attach(CarNavigator navigator);
    void SetRoot(CarScreen root);
    void Push(CarScreen screen);
    void Pop();
    void PopToRoot();
    void Present(CarModalScreen modal);
    void Dismiss(CarModalScreen modal);
    /// <summary>The screen's state changed; rebuild via <see cref="CarNavigator.BuildTemplate"/> and update.</summary>
    void Invalidate(CarScreen screen);
}

/// <summary>
/// Platform-agnostic navigation stack. It is the source of truth for which screens exist; renderers mirror it
/// and report platform-initiated pops (back button) through <see cref="OnPlatformPopped"/>.
/// </summary>
public sealed class CarNavigator : ICarNavigator
{
    readonly List<CarScreen> stack = [];
    readonly ICarRenderer renderer;
    readonly Action<Action> dispatch;
    readonly Action<string>? log;
    CarModalScreen? presented;

    public CarNavigator(
        ICarRenderer renderer,
        IServiceProvider services,
        CarOptions options,
        CarLimits? platformLimits = null,
        Action<Action>? dispatch = null,
        CarPlatform? platform = null)
    {
        Platform = platform;
        this.renderer = renderer;
        this.dispatch = dispatch ?? (a => a());
        Services = services;
        Options = options;
        log = options.Log;
        Limits = options.ResolveLimits(platformLimits);
        Validator = new CarTemplateValidator(Limits, options.ConstraintMode, log, platform, options.Category);
        renderer.Attach(this);
    }

    public IServiceProvider Services { get; }
    public CarOptions Options { get; }
    public CarLimits Limits { get; }
    public CarTemplateValidator Validator { get; }
    public CarPlatform? Platform { get; }
    public IReadOnlyList<CarScreen> Stack => stack;

    public bool Supports<TTemplate>() where TTemplate : CarTemplate => Validator.Supports(typeof(TTemplate));
    public CarScreen? Top => stack.Count == 0 ? null : stack[^1];
    public bool IsPresenting => presented is not null;
    public CarModalScreen? Presented => presented;

    public event EventHandler? StackChanged;

    public void Start(CarScreen root) => dispatch(() =>
    {
        if (stack.Count > 0)
            throw new InvalidOperationException("Navigator already started.");
        stack.Add(root);
        root.Attach(this);
        renderer.SetRoot(root);
        StackChanged?.Invoke(this, EventArgs.Empty);
    });

    /// <summary>Tears the stack down (car disconnected).</summary>
    public void Stop() => dispatch(() =>
    {
        presented?.Detach();
        presented = null;
        for (var i = stack.Count - 1; i >= 0; i--)
            stack[i].Detach();
        stack.Clear();
        StackChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Push(CarScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (stack.Count >= Limits.MaxDepth)
            throw new CarConstraintException($"SwiftDotNet.Car constraint exceeded: navigation depth ({stack.Count + 1} > {Limits.MaxDepth}).");
        if (screen.IsAttached || stack.Contains(screen))
            throw new InvalidOperationException("Screen is already on a navigation stack.");

        dispatch(() =>
        {
            stack.Add(screen);
            screen.Attach(this);
            renderer.Push(screen);
            StackChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public void Pop() => dispatch(() =>
    {
        if (stack.Count <= 1)
            return;
        var top = stack[^1];
        stack.RemoveAt(stack.Count - 1);
        renderer.Pop();
        top.Detach();
        StackChanged?.Invoke(this, EventArgs.Empty);
    });

    public void PopToRoot() => dispatch(() =>
    {
        if (stack.Count <= 1)
            return;
        var removed = stack.Skip(1).Reverse().ToList();
        stack.RemoveRange(1, stack.Count - 1);
        renderer.PopToRoot();
        foreach (var s in removed)
            s.Detach();
        StackChanged?.Invoke(this, EventArgs.Empty);
    });

    public void Present(CarModalTemplate modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        // validate eagerly so errors surface at the call site
        var normalized = (CarModalTemplate)Validator.Normalize(modal, CarTemplatePlacement.Modal);
        var wrapped = normalized with
        {
            Actions = normalized.Actions
                .Select(a => a with
                {
                    OnSelect = () =>
                    {
                        Dismiss();
                        a.OnSelect?.Invoke();
                    }
                })
                .ToList()
        };

        dispatch(() =>
        {
            if (presented is not null)
                DismissCore();
            presented = new CarModalScreen(wrapped);
            presented.Attach(this);
            renderer.Present(presented);
        });
    }

    public void Dismiss() => dispatch(DismissCore);

    void DismissCore()
    {
        if (presented is null)
            return;
        var modal = presented;
        presented = null;
        renderer.Dismiss(modal);
        modal.Detach();
    }

    /// <summary>Builds and normalizes the template for a screen. Renderers call this whenever they need content.</summary>
    public CarTemplate BuildTemplate(CarScreen screen)
    {
        var placement = screen is CarModalScreen ? CarTemplatePlacement.Modal : CarTemplatePlacement.Screen;
        return Validator.Normalize(screen.Build(), placement);
    }

    /// <summary>Builds a template, falling back to an error pane (and logging) if the screen throws.</summary>
    public CarTemplate BuildTemplateSafe(CarScreen screen)
    {
        try
        {
            return BuildTemplate(screen);
        }
        catch (Exception ex)
        {
            log?.Invoke($"SwiftDotNet.Car: {screen.GetType().Name}.Build failed: {ex}");
            if (screen is CarModalScreen)
                return new CarAlertTemplate { Title = "Something went wrong", Actions = [new CarAction("OK", Dismiss)] };
            return new CarInfoTemplate { Title = "Error", Items = [new CarInfoItem("Something went wrong", ex.Message)] };
        }
    }

    internal void Invalidate(CarScreen screen) => dispatch(() =>
    {
        if (stack.Contains(screen) || ReferenceEquals(screen, presented))
            renderer.Invalidate(screen);
    });

    /// <summary>
    /// The platform removed a screen itself (back button / swipe). Removes it and anything above it.
    /// No-op for screens the navigator already removed.
    /// </summary>
    public void OnPlatformPopped(CarScreen screen) => dispatch(() =>
    {
        if (ReferenceEquals(screen, presented))
        {
            presented = null;
            screen.Detach();
            return;
        }
        var index = stack.IndexOf(screen);
        if (index <= 0) // unknown, or the root (root is never popped)
            return;
        var removed = stack.Skip(index).Reverse().ToList();
        stack.RemoveRange(index, stack.Count - index);
        foreach (var s in removed)
            s.Detach();
        StackChanged?.Invoke(this, EventArgs.Empty);
    });
}
