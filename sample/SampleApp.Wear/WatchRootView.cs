using SwiftDotNet;
using Button = SwiftDotNet.Button;

namespace SampleApp.Wear;

/// <summary>
/// A watch-sized demo: plain SwiftDotNet views, no Wear-specific API except the rotary modifier. The
/// SwiftDotNet.Wear skin turns the list into a ScalingLazyColumn, the links and toggle into chips, and
/// wires system back (swipe right) to pop.
/// </summary>
public sealed class WatchRootView : View
{
    static readonly string[] Cities = ["Toronto", "London", "Tokyo", "Sydney", "Nairobi", "Lima", "Oslo"];

    readonly State<int> _count = State(0);
    readonly State<double> _crown = State(50.0);
    readonly State<bool> _metric = State(true);
    readonly State<double> _level = State(5.0);

    public override View Body => new NavigationStack(
        new List(
            new NavigationLink(new Label("Counter", "hand.tap"), CounterPage),
            new NavigationLink(new Label("Crown", "gauge"), CrownPage),
            new NavigationLink(new Label("Cities", "globe"), CitiesPage),
            new NavigationLink(new Label("Controls", "gauge"), ControlsPage),
            new NavigationLink(new Label("Pages", "rectangle.stack"), PagesPage),
            new Toggle("Metric units", _metric),
            new Section("About",
                new Text($"Count {_count.Value} · Crown {_crown.Value:0}").ForegroundColor(Color.Secondary))
        ).NavigationTitle("SwiftDotNet"));

    View CounterPage => new VStack(
            new Text($"{_count.Value}").Font(Font.LargeTitle),
            new HStack(
                new Button("−", () => _count.Value--),
                new Button("+", () => _count.Value++)
            ).Spacing(16)
        ).Spacing(8)
        .NavigationTitle("Counter");

    // The whole page takes rotary focus: turn the crown (or the emulator's rotary control) to move the value.
    View CrownPage => new VStack(
            new Gauge(_crown.Value, 0, 100, $"{_crown.Value:0}"),
            new Text("Turn the crown").Font(Font.Caption).ForegroundColor(Color.Secondary)
        ).Spacing(6)
        .DigitalCrownRotation(_crown.Value, v => _crown.Value = v, from: 0, through: 100, by: 1)
        .NavigationTitle("Crown");

    View ControlsPage => new List(
            new Stepper("Count", _count, 0, 99),
            new Slider(_level, 0, 10),
            new Text($"Level {_level.Value:0.0}").ForegroundColor(Color.Secondary))
        .NavigationTitle("Controls");

    // watchOS 10 pages vertically; the Wear skin maps every TabView to a VerticalPager.
    View PagesPage => new TabView(
            new VStack(new Text("Page 1").Font(Font.Title), new Text("Swipe up").Font(Font.Caption)),
            new VStack(new Text("Page 2").Font(Font.Title), new Text($"Count {_count.Value}").Font(Font.Caption)),
            new VStack(new Text("Page 3").Font(Font.Title)))
        .Paged();

    View CitiesPage =>List.ForEach(Cities, c => (object)c, c =>
            new Button(c, () => _count.Value++))
        .NavigationTitle("Cities");
}
