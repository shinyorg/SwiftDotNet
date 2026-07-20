using Microsoft.Extensions.DependencyInjection;
using SwiftDotNet;
using SwiftDotNet.Hosting;
using Xunit;

namespace SwiftDotNet.Tests;

// Services used by the fixtures below.
public interface IGreeter { string Greet(); }
public sealed class Greeter : IGreeter { public string Greet() => "hello"; }
public interface IAudit { List<string> Entries { get; } }
public sealed class Audit : IAudit { public List<string> Entries { get; } = new(); }

/// <summary>A scoped service, to prove each ViewScope gets its own instance and disposes it.</summary>
public sealed class ScopedProbe : IDisposable
{
    public static int Created;
    public static int Disposed;
    public ScopedProbe() => Created++;
    public void Dispose() => Disposed++;
}

/// <summary>Root view: [Inject] members filled by the generator, plus its own lifecycle overrides.</summary>
public sealed partial class InjectedRootView : View
{
    [Inject] public IGreeter Greeter { get; set; } = default!;
    [Inject] public IAudit? Audit { get; set; }          // nullable ⇒ optional resolve

    public List<string> Calls { get; } = new();

    // Proves ordering: the [Inject] fill (itself an IViewLifecycle observer) runs before this.
    protected override void OnCreated() => Calls.Add($"OnCreated:{Greeter is not null}");
    protected override void OnAppearing() => Calls.Add(nameof(OnAppearing));
    protected override void OnDisappearing() => Calls.Add(nameof(OnDisappearing));
    protected override void OnDestroyed() => Calls.Add(nameof(OnDestroyed));

    public override View Body => new Text(Greeter.Greet());
}

/// <summary>A user-registered observer — receives events for every retained view.</summary>
public sealed class RecordingViewLifecycle : IViewLifecycle
{
    public static readonly List<string> Events = new();
    public void OnCreated(View view) => Events.Add($"created:{view.GetType().Name}");
    public void OnAppearing(View view) => Events.Add($"appearing:{view.GetType().Name}");
    public void OnDisappearing(View view) => Events.Add($"disappearing:{view.GetType().Name}");
    public void OnDestroyed(View view) => Events.Add($"destroyed:{view.GetType().Name}");
}

/// <summary>The partial-property form: read-only to callers, implemented by the generator.</summary>
public sealed partial class PartialInjectView : View
{
    [Inject] public partial IGreeter Greeter { get; }
    [Inject] public partial IAudit? Audit { get; }

    public override View Body => new Text(Greeter.Greet());
}

/// <summary>A page-style view holding a scoped service, to exercise per-page scoping.</summary>
public sealed partial class ScopedPageView : View
{
    [Inject] public ScopedProbe Probe { get; set; } = default!;
    public override View Body => new Text("page");
}

/// <summary>Leaf built inline in a Body — must use Service&lt;T&gt;(), not [Inject].</summary>
public sealed class LocatorLeafView : View
{
    public override View Body => new Text(Service<IGreeter>().Greet());
}

public sealed class RecordingInitializer : ISwiftInitializer
{
    public static readonly List<bool> ScopedFlags = new();
    public void Initialize(IServiceProvider services, bool scoped) => ScopedFlags.Add(scoped);
}

public class HostingTests : IDisposable
{
    public void Dispose() => SwiftHost.Services = null;

    static SwiftDotNetAppBuilder Builder()
    {
        var builder = SwiftDotNetApp.CreateBuilder();
        builder.Services.AddSingleton<IGreeter, Greeter>();
        return builder;
    }

    [Fact]
    public void Generator_fills_required_and_optional_inject_members()
    {
        var builder = Builder();
        builder.Services.AddSingleton<IAudit, Audit>();
        builder.UseSwiftApp(_ => new InjectedRootView());

        var view = (InjectedRootView)builder.Build().CreateRoot();

        Assert.IsType<Greeter>(view.Greeter);
        Assert.IsType<Audit>(view.Audit);
    }

    [Fact]
    public void Partial_properties_are_implemented_by_the_generator()
    {
        var builder = Builder();
        builder.Services.AddSingleton<IAudit, Audit>();
        builder.UseSwiftApp(_ => new PartialInjectView());

        var view = (PartialInjectView)builder.Build().CreateRoot();

        Assert.IsType<Greeter>(view.Greeter);
        Assert.IsType<Audit>(view.Audit);
    }

    [Fact]
    public void Partial_property_read_before_injection_explains_itself()
    {
        var view = new PartialInjectView();          // never went through the container

        var ex = Assert.Throws<InvalidOperationException>(() => view.Greeter);
        Assert.Contains("before it was injected", ex.Message);
        Assert.Null(view.Audit);                     // optional stays null rather than throwing
    }

    [Fact]
    public void Optional_inject_member_is_null_when_unregistered()
    {
        var app = Builder().UseSwiftApp(_ => new InjectedRootView()).Build();
        var view = (InjectedRootView)app.CreateRoot();

        Assert.Null(view.Audit);            // IAudit was never registered
        Assert.NotNull(view.Greeter);
    }

    [Fact]
    public void Required_inject_member_throws_with_a_registration_hint()
    {
        var app = SwiftDotNetApp.CreateBuilder().UseSwiftApp(_ => new InjectedRootView()).Build();

        var ex = Assert.Throws<InvalidOperationException>(() => app.CreateRoot());
        Assert.Contains("IGreeter", ex.Message);
        Assert.Contains("SwiftProgram.CreateSwiftApp()", ex.Message);
    }

    [Fact]
    public void Root_lifecycle_fires_created_then_resume_with_services_already_injected()
    {
        var app = Builder().UseSwiftApp(_ => new InjectedRootView()).Build();
        var view = (InjectedRootView)app.CreateRoot();

        // "True" ⇒ [Inject] ran before the view's own OnCreated, which is the ordering guarantee.
        Assert.Equal(new[] { "OnCreated:True", "OnAppearing" }, view.Calls);
    }

    [Fact]
    public void Root_is_created_once()
    {
        var app = Builder().UseSwiftApp(_ => new InjectedRootView()).Build();

        var first = app.CreateRoot();
        var second = app.CreateRoot();

        Assert.Same(first, second);
        Assert.Equal(new[] { "OnCreated:True", "OnAppearing" }, ((InjectedRootView)first).Calls);
    }

    [Fact]
    public void Registered_observers_receive_events_for_every_retained_view()
    {
        RecordingViewLifecycle.Events.Clear();

        var builder = Builder();
        builder.Services.AddSingleton<IViewLifecycle, RecordingViewLifecycle>();
        builder.UseSwiftApp(_ => new InjectedRootView());
        var app = builder.Build();

        app.CreateRoot();

        Assert.Equal(
            new[] { "created:InjectedRootView", "appearing:InjectedRootView" },
            RecordingViewLifecycle.Events);
    }

    [Fact]
    public void Scoped_view_raises_the_full_lifecycle_in_order()
    {
        RecordingViewLifecycle.Events.Clear();

        var builder = Builder();
        builder.Services.AddSingleton<IViewLifecycle, RecordingViewLifecycle>();
        builder.Services.AddScoped<ScopedProbe>();
        builder.UseSwiftApp(_ => new InjectedRootView());
        var app = builder.Build();

        var page = ViewScope.Create(app.Services, _ => new ScopedPageView());
        page.Appearing();
        page.Dispose();

        Assert.Equal(
            new[] { "created:ScopedPageView", "appearing:ScopedPageView", "disappearing:ScopedPageView", "destroyed:ScopedPageView" },
            RecordingViewLifecycle.Events);
    }

    [Fact]
    public void Each_view_scope_gets_its_own_scoped_service_and_disposes_it()
    {
        ScopedProbe.Created = ScopedProbe.Disposed = 0;

        var builder = Builder();
        builder.Services.AddScoped<ScopedProbe>();
        builder.UseSwiftApp(_ => new InjectedRootView());
        var app = builder.Build();

        var pageA = ViewScope.Create(app.Services, _ => new ScopedPageView());
        var pageB = ViewScope.Create(app.Services, _ => new ScopedPageView());

        var a = ((ScopedPageView)pageA.View).Probe;
        var b = ((ScopedPageView)pageB.View).Probe;

        Assert.NotSame(a, b);                       // per-page instance, not app-wide
        Assert.Equal(2, ScopedProbe.Created);

        pageA.Dispose();
        Assert.Equal(1, ScopedProbe.Disposed);      // popping one page disposes only its scope
        pageB.Dispose();
        Assert.Equal(2, ScopedProbe.Disposed);
    }

    [Fact]
    public void Service_locator_resolves_from_the_active_scope_then_falls_back_to_the_app()
    {
        var builder = Builder();
        builder.Services.AddScoped<ScopedProbe>();
        builder.UseSwiftApp(_ => new InjectedRootView());
        var app = builder.Build();
        SwiftHost.Services = app.Services;

        var page = ViewScope.Create(app.Services, _ => new ScopedPageView());
        var scoped = ((ScopedPageView)page.View).Probe;

        page.Use(() => Assert.Same(scoped, SwiftHost.Require<ScopedProbe>()));   // inside ⇒ page's instance
        Assert.NotSame(scoped, SwiftHost.Require<ScopedProbe>());                // outside ⇒ app's own scope

        page.Dispose();
    }

    [Fact]
    public void Inline_children_resolve_services_through_the_locator()
    {
        var app = Builder().UseSwiftApp(_ => new InjectedRootView()).Build();
        var bridge = new CaptureJsonBridge();

        // Run publishes the provider, so a leaf built inline in a Body can reach IGreeter.
        SwiftApp.Run(new LocatorLeafView(), bridge, app.Services);

        Assert.Contains("\"hello\"", bridge.Json);
    }

    sealed class CaptureJsonBridge : IBridge
    {
        public string Json { get; private set; } = "";
        public void Render(string json) => Json = json;
        public void SetEventHandler(Action<string, string?> handler) { }
    }

    [Fact]
    public void Initializers_run_once_app_wide_and_once_per_scope()
    {
        RecordingInitializer.ScopedFlags.Clear();

        var builder = Builder();
        builder.Services.AddSingleton<ISwiftInitializer, RecordingInitializer>();
        builder.Services.AddScoped<ScopedProbe>();
        builder.UseSwiftApp(_ => new InjectedRootView());
        var app = builder.Build();

        Assert.Equal(new[] { false }, RecordingInitializer.ScopedFlags);   // Build() ⇒ scoped: false

        ViewScope.Create(app.Services, _ => new ScopedPageView()).Dispose();
        Assert.Equal(new[] { false, true }, RecordingInitializer.ScopedFlags);
    }

    [Fact]
    public void Build_without_a_root_view_fails_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SwiftDotNetApp.CreateBuilder().Build());
        Assert.Contains("UseSwiftApp", ex.Message);
    }
}
