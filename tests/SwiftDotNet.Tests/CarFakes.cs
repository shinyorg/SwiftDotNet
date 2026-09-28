using SwiftDotNet;

namespace SwiftDotNet.Tests.Car;

sealed class FakeRenderer : ICarRenderer
{
    public List<string> Calls { get; } = [];
    public CarModalScreen? Presented { get; private set; }

    public CarNavigator? Navigator { get; private set; }
    public void Attach(CarNavigator navigator) => Navigator = navigator;
    public void SetRoot(CarScreen root) => Calls.Add($"root:{Name(root)}");
    public void Push(CarScreen screen) => Calls.Add($"push:{Name(screen)}");
    public void Pop() => Calls.Add("pop");
    public void PopToRoot() => Calls.Add("popToRoot");
    public void Present(CarModalScreen modal)
    {
        Presented = modal;
        Calls.Add($"present:{modal.Template.Title}");
    }
    public void Dismiss(CarModalScreen modal)
    {
        Presented = null;
        Calls.Add($"dismiss:{modal.Template.Title}");
    }
    public void Invalidate(CarScreen screen) => Calls.Add($"invalidate:{Name(screen)}");

    static string Name(CarScreen s) => s is TestScreen t ? t.Name : s.GetType().Name;
}

sealed class TestScreen(string name, Func<CarTemplate>? build = null) : CarScreen
{
    public string Name { get; } = name;
    public int Attached { get; private set; }
    public int Detached { get; private set; }
    public int Counter { get; private set; }

    public override CarTemplate Build() =>
        build?.Invoke() ?? CarListTemplate.Create(Name, [new CarListItem { Title = $"count {Counter}" }]);

    public void Increment() => SetState(() => Counter++);

    protected override void OnAttached() => Attached++;
    protected override void OnDetached() => Detached++;
}

sealed class EmptyServices : IServiceProvider
{
    public object? GetService(Type serviceType) => null;
}

static class Items
{
    public static IReadOnlyList<CarListItemBase> Rows(int count) =>
        Enumerable.Range(0, count).Select(i => (CarListItemBase)new CarListItem { Title = $"Row {i}" }).ToList();

    public static CarImage Icon => CarImage.Symbol("star");
}
