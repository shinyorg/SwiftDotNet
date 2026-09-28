using SwiftDotNet.Watch;

namespace SwiftDotNet.Sample.Watch;

/// <summary>
/// A watch-sized tour: a navigation list whose rows push a counter (persisted, with haptics), a Digital
/// Crown-driven gauge, a plain list, the device info the watch reports, and an HTTPS fetch over URLSession
/// (<see cref="WatchHttp"/>). Ordinary SwiftDotNet views —
/// the same DSL as the phone heads — rendered by real watchOS SwiftUI.
/// </summary>
public sealed class WatchRootView : View
{
    readonly State<int> _count = State(0);
    readonly State<double> _crown = State(40.0);
    readonly State<bool> _haptics = State(true);
    readonly State<string> _device = State("Tap Refresh");
    readonly State<string> _http = State("Tap Fetch");

    static readonly string[] Planets = ["Mercury", "Venus", "Earth", "Mars", "Jupiter", "Saturn", "Uranus", "Neptune"];

    protected override void OnCreated()
    {
        // Persisted across launches (UserDefaults on the watch).
        _count.Value = Preferences.Get("count", 0);

        // Headless smoke test for CI-style verification without driving the simulator's UI:
        //   SIMCTL_CHILD_SWIFTDOTNET_WATCH_SMOKE=1 xcrun simctl launch --console-pty <udid> com.swiftdotnet.sample.watch
        if (Environment.GetEnvironmentVariable("SWIFTDOTNET_WATCH_SMOKE") == "1")
            Smoke();
    }

    async void Smoke()
    {
        var d = WatchDevice.Current;
        WatchHost.Log(WatchLogLevel.Info, $"smoke: device {d.Name} / {d.SystemName} {d.SystemVersion} (simulator={d.IsSimulator})");
        Preferences.Set("smoke", "ok");
        WatchHost.Log(WatchLogLevel.Info, $"smoke: prefs {Preferences.Get("smoke")}");
        await FetchAsync();
        WatchHost.Log(WatchLogLevel.Info, $"smoke: http {_http.Value.Replace('\n', ' ')} (main thread: {WatchHost.IsMainThread})");
    }

    public override View Body =>
        new NavigationStack(
            new List(
                new NavigationLink(new Label("Counter", "plus.circle"), CounterPage()),
                new NavigationLink(new Label("Crown", "digitalcrown.arrow.clockwise"), CrownPage()),
                new NavigationLink(new Label("Planets", "globe"), PlanetsPage()),
                new NavigationLink(new Label("Device", "applewatch"), DevicePage()),
                new NavigationLink(new Label("Network", "network"), NetworkPage()))
            .NavigationTitle("SwiftDotNet"));

    View CounterPage() =>
        new VStack(
            new Text($"{_count.Value}").Font(Font.LargeTitle),
            new HStack(
                new Button("−", () => Change(-1)),
                new Button("+", () => Change(+1))),
            new Toggle("Haptics", _haptics))
        .Spacing(8)
        .NavigationTitle("Counter");

    View CrownPage() =>
        new VStack(
            new Gauge(_crown.Value, 0, 100, "Crown"),
            new Text($"{_crown.Value:0}").Font(Font.Title),
            new Text("Turn the Digital Crown").Font(Font.Caption))
        .Spacing(6)
        .DigitalCrownRotation(_crown.Value, v => _crown.Value = v, from: 0, through: 100, by: 1)
        .NavigationTitle("Crown");

    View PlanetsPage() =>
        new List(Array.ConvertAll(Planets, p => (View)new Text(p)))
        .NavigationTitle("Planets");

    View DevicePage() =>
        new VStack(
            new Text(_device.Value).Font(Font.Caption),
            new Button("Refresh", RefreshDevice))
        .Spacing(8)
        .NavigationTitle("Device");

    View NetworkPage() =>
        new VStack(
            new Text(_http.Value).Font(Font.Caption),
            new Button("Fetch", Fetch))
        .Spacing(8)
        .NavigationTitle("Network");

    // URLSession underneath (WatchHttp), and the await resumes on the watch's main thread, so the State
    // assignment after it needs no marshaling.
    async void Fetch() => await FetchAsync();

    async Task FetchAsync()
    {
        _http.Value = "Loading…";
        try
        {
            using var client = WatchHttp.CreateClient();
            using var response = await client.GetAsync("https://example.com/");
            var body = await response.Content.ReadAsStringAsync();
            _http.Value = $"HTTP {(int)response.StatusCode}\n{body.Length} bytes";
        }
        catch (Exception ex)
        {
            _http.Value = ex.Message;
        }
    }

    void Change(int delta)
    {
        _count.Value += delta;
        Preferences.Set("count", _count.Value);
        if (_haptics.Value)
            Haptics.Play(delta > 0 ? HapticType.DirectionUp : HapticType.DirectionDown);
    }

    void RefreshDevice()
    {
        var d = WatchDevice.Current;
        _device.Value = $"{d.Name}\n{d.SystemName} {d.SystemVersion}\n{d.ScreenWidth:0}×{d.ScreenHeight:0} @{d.ScreenScale:0}x";
    }
}
