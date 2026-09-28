namespace SwiftDotNet;

/// <summary>
/// One entry on the car navigation stack. MVU style: <see cref="Build"/> returns an immutable template
/// from the screen's current state; call <see cref="Invalidate"/> (or <see cref="SetState"/>) after state
/// changes and the renderer rebuilds and updates the native template in place where possible.
/// </summary>
public abstract class CarScreen
{
    CarNavigator? navigator;

    /// <summary>The navigator this screen is attached to. Available from <see cref="OnAttached"/> on.</summary>
    public ICarNavigator Navigator => navigator ?? throw new InvalidOperationException("Screen is not attached to a car session yet.");

    public IServiceProvider Services => Navigator.Services;

    public bool IsAttached => navigator is not null;

    /// <summary>Builds the template for the current state. Called on the platform UI thread.</summary>
    public abstract CarTemplate Build();

    /// <summary>Requests a re-render. Safe to call from any thread and when detached (no-op).</summary>
    public void Invalidate() => navigator?.Invalidate(this);

    /// <summary>Mutates state then invalidates.</summary>
    protected void SetState(Action mutate)
    {
        mutate();
        Invalidate();
    }

    /// <summary>Called when the screen is placed on the stack (subscribe to services here).</summary>
    protected virtual void OnAttached() { }

    /// <summary>Called when the screen leaves the stack for good (unsubscribe here).</summary>
    protected virtual void OnDetached() { }

    internal void Attach(CarNavigator nav)
    {
        navigator = nav;
        OnAttached();
    }

    internal void Detach()
    {
        OnDetached();
        navigator = null;
    }
}

/// <summary>A screen that always shows the same template (useful for simple drill-downs and map overlays).</summary>
public sealed class CarTemplateScreen(CarTemplate template) : CarScreen
{
    public CarTemplate Template { get; } = template;
    public override CarTemplate Build() => Template;
}

/// <summary>Wraps a presented alert/action sheet. Renderers receive this from <see cref="ICarRenderer.Present"/>.</summary>
public sealed class CarModalScreen : CarScreen
{
    internal CarModalScreen(CarModalTemplate template) => Template = template;
    public CarModalTemplate Template { get; }
    public override CarTemplate Build() => Template;
}
