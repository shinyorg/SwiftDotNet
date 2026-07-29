namespace SwiftDotNet.Sample;

/// <summary>
/// The app's retained root. It wraps the tour's <see cref="ContentView"/> in an <see cref="OverlayHost"/>
/// (as every backend did before DI landed) and, being container-created, demonstrates the three things
/// only a retained view can do: <c>[Inject]</c> partial properties, constructor-free service access, and
/// the <see cref="IViewLifecycle"/> hooks.
/// </summary>
public sealed partial class SampleRootView : View
{
    /// <summary>Filled by the generated <c>IInjectable.Inject</c> — no reflection, no setter.</summary>
    [Inject] public partial IGreetingService Greeting { get; }

    protected override void OnCreated() =>
        Console.WriteLine($"[root] created; service says: {Greeting.Greet()}");

    protected override void OnAppearing() => Console.WriteLine("[root] appearing");

    // Held, not rebuilt: `Body` runs on every render pass, so `new ContentView()` here would hand back a
    // fresh instance each time — and with it a fresh set of `State<T>` fields reset to their initial values.
    // The diff would then never see a change and no state-driven update would ever reach the screen. Until
    // view-instance reconciliation lands (see plans/README.md), a view that owns state must be retained by
    // whatever builds it. `OverlayHost` itself is stateless (the overlay layer is static), so it is fine to
    // rebuild.
    readonly ContentView _content = new();

    public override View Body => new OverlayHost(_content);
}
