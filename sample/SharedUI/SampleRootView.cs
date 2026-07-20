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

    public override View Body => new OverlayHost(new ContentView());
}
