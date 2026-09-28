using SwiftDotNet;
using Xunit;

namespace SwiftDotNet.Tests.Car;

public class NavigatorTests
{
    readonly FakeRenderer renderer = new();
    readonly List<string> log = [];

    CarNavigator Create(CarOptions? options = null)
    {
        options ??= new CarOptions();
        options.Log = log.Add;
        return new CarNavigator(renderer, new EmptyServices(), options);
    }

    [Fact]
    public void Start_SetsRoot_AndAttaches()
    {
        var nav = Create();
        var root = new TestScreen("root");
        nav.Start(root);

        Assert.Equal(["root:root"], renderer.Calls);
        Assert.Same(root, nav.Top);
        Assert.Equal(1, root.Attached);
        Assert.Same(nav, root.Navigator);
    }

    [Fact]
    public void PushPop_MirrorsToRenderer_AndDetaches()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        var child = new TestScreen("child");

        nav.Push(child);
        Assert.Equal(2, nav.Stack.Count);
        nav.Pop();

        Assert.Equal(["root:root", "push:child", "pop"], renderer.Calls);
        Assert.Equal(1, child.Detached);
        Assert.False(child.IsAttached);
    }

    [Fact]
    public void Pop_AtRoot_IsNoOp()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        nav.Pop();
        Assert.Single(nav.Stack);
        Assert.DoesNotContain("pop", renderer.Calls);
    }

    [Fact]
    public void PopToRoot_DetachesAllButRoot()
    {
        var nav = Create();
        var root = new TestScreen("root");
        var a = new TestScreen("a");
        var b = new TestScreen("b");
        nav.Start(root);
        nav.Push(a);
        nav.Push(b);

        nav.PopToRoot();

        Assert.Single(nav.Stack);
        Assert.Equal(1, a.Detached);
        Assert.Equal(1, b.Detached);
        Assert.Equal(0, root.Detached);
        Assert.Equal("popToRoot", renderer.Calls[^1]);
    }

    [Fact]
    public void Push_BeyondMaxDepth_Throws()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        for (var i = 1; i < CarLimits.Portable.MaxDepth; i++)
            nav.Push(new TestScreen($"s{i}"));

        Assert.Throws<CarConstraintException>(() => nav.Push(new TestScreen("too-deep")));
        Assert.Equal(CarLimits.Portable.MaxDepth, nav.Stack.Count);
    }

    [Fact]
    public void Push_SameScreenTwice_Throws()
    {
        var nav = Create();
        var root = new TestScreen("root");
        nav.Start(root);
        Assert.Throws<InvalidOperationException>(() => nav.Push(root));
    }

    [Fact]
    public void SetState_InvalidatesAttachedScreen()
    {
        var nav = Create();
        var root = new TestScreen("root");
        nav.Start(root);

        root.Increment();

        Assert.Equal("invalidate:root", renderer.Calls[^1]);
        var list = (CarListTemplate)nav.BuildTemplate(root);
        Assert.Equal("count 1", list.Sections[0].Items[0].Title);
    }

    [Fact]
    public void Invalidate_DetachedScreen_IsIgnored()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        var child = new TestScreen("child");
        nav.Push(child);
        nav.Pop();
        renderer.Calls.Clear();

        child.Increment();

        Assert.Empty(renderer.Calls);
    }

    [Fact]
    public void Present_WrapsActions_ToDismissFirst()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        var confirmed = false;
        var dismissedBeforeHandler = false;

        nav.Present(new CarAlertTemplate
        {
            Title = "Reserve?",
            Actions =
            [
                new CarAction("Yes", () => { dismissedBeforeHandler = !nav.IsPresenting; confirmed = true; }, CarActionStyle.Primary),
                new CarAction("No", style: CarActionStyle.Cancel)
            ]
        });
        Assert.True(nav.IsPresenting);

        var modal = renderer.Presented!;
        modal.Template.Actions[0].OnSelect!();

        Assert.True(confirmed);
        Assert.True(dismissedBeforeHandler);
        Assert.False(nav.IsPresenting);
        Assert.Equal(["root:root", "present:Reserve?", "dismiss:Reserve?"], renderer.Calls);
    }

    [Fact]
    public void Present_ReplacesExistingModal()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        nav.Present(new CarAlertTemplate { Title = "one", Actions = [new CarAction("ok")] });
        nav.Present(new CarAlertTemplate { Title = "two", Actions = [new CarAction("ok")] });

        Assert.Equal(["root:root", "present:one", "dismiss:one", "present:two"], renderer.Calls);
    }

    [Fact]
    public void Present_InvalidModal_ThrowsAtCallSite()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        Assert.Throws<CarTemplateException>(() => nav.Present(new CarAlertTemplate { Title = "no actions" }));
    }

    [Fact]
    public void Present_TruncatesActions()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        nav.Present(new CarActionSheetTemplate
        {
            Title = "Pick",
            Actions = [new CarAction("1"), new CarAction("2"), new CarAction("3")]
        });
        Assert.Equal(2, renderer.Presented!.Template.Actions.Count);
    }

    [Fact]
    public void PlatformPop_RemovesScreenAndAbove_ButNeverRoot()
    {
        var nav = Create();
        var root = new TestScreen("root");
        var a = new TestScreen("a");
        var b = new TestScreen("b");
        nav.Start(root);
        nav.Push(a);
        nav.Push(b);
        var callsBefore = renderer.Calls.Count;

        nav.OnPlatformPopped(a);
        nav.OnPlatformPopped(root);
        nav.OnPlatformPopped(new TestScreen("stranger"));

        Assert.Equal([root], nav.Stack);
        Assert.Equal(1, a.Detached);
        Assert.Equal(1, b.Detached);
        Assert.Equal(callsBefore, renderer.Calls.Count); // platform pops never echo back to the renderer
    }

    [Fact]
    public void PlatformPop_OfModal_ClearsPresented()
    {
        var nav = Create();
        nav.Start(new TestScreen("root"));
        nav.Present(new CarAlertTemplate { Title = "x", Actions = [new CarAction("ok")] });
        nav.OnPlatformPopped(renderer.Presented!);
        Assert.False(nav.IsPresenting);
    }

    [Fact]
    public void BuildTemplateSafe_ReturnsErrorPane_AndLogs()
    {
        var nav = Create();
        var root = new TestScreen("root", () => throw new InvalidOperationException("boom"));
        nav.Start(root);

        var template = nav.BuildTemplateSafe(root);

        var info = Assert.IsType<CarInfoTemplate>(template);
        Assert.Equal("boom", info.Items[0].Detail);
        Assert.Contains(log, l => l.Contains("boom"));
    }

    [Fact]
    public void BuildTemplate_AppliesLimits()
    {
        var nav = Create(new CarOptions { ConstraintMode = CarConstraintMode.Throw });
        var root = new TestScreen("root", () => CarListTemplate.Create("x", Items.Rows(50)));
        nav.Start(root);
        Assert.Throws<CarConstraintException>(() => nav.BuildTemplate(root));
    }

    [Fact]
    public void Dispatch_IsUsedForMutations()
    {
        var dispatched = 0;
        var nav = new CarNavigator(renderer, new EmptyServices(), new CarOptions(), dispatch: a => { dispatched++; a(); });
        var root = new TestScreen("root");
        nav.Start(root);
        nav.Push(new TestScreen("a"));
        root.Increment();
        nav.Pop();
        Assert.Equal(4, dispatched);
    }

    [Fact]
    public void Stop_DetachesEverything()
    {
        var nav = Create();
        var root = new TestScreen("root");
        var a = new TestScreen("a");
        nav.Start(root);
        nav.Push(a);
        nav.Present(new CarAlertTemplate { Title = "x", Actions = [new CarAction("ok")] });

        nav.Stop();

        Assert.Empty(nav.Stack);
        Assert.False(nav.IsPresenting);
        Assert.Equal(1, root.Detached);
        Assert.Equal(1, a.Detached);
    }

    [Fact]
    public void CarSession_StartsApp_AndTracksConnection()
    {
        var connection = new CarConnection();
        var app = new TestApp();
        var services = new DictionaryServices(new()
        {
            [typeof(CarApp)] = app,
            [typeof(ICarConnection)] = connection,
            [typeof(CarOptions)] = new CarOptions()
        });

        var nav = CarSession.Start(CarPlatform.AndroidAuto, services, renderer, null, a => a());

        Assert.True(connection.IsConnected);
        Assert.Equal(CarPlatform.AndroidAuto, connection.Platform);
        Assert.True(app.Connected);
        Assert.Equal("root:app-root", renderer.Calls[0]);

        CarSession.Stop(services, nav);
        Assert.False(connection.IsConnected);
        Assert.True(app.Disconnected);
    }

    sealed class TestApp : CarApp
    {
        public bool Connected { get; private set; }
        public bool Disconnected { get; private set; }
        public override CarScreen CreateRootScreen(IServiceProvider services) => new TestScreen("app-root");
        public override void OnConnected(ICarNavigator navigator) => Connected = true;
        public override void OnDisconnected() => Disconnected = true;
    }

    sealed class DictionaryServices(Dictionary<Type, object> map) : IServiceProvider
    {
        public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
    }
}
