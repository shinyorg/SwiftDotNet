using SwiftDotNet;

namespace SwiftDotNet.Sample;

/// <summary>
/// A tour of the framework: a bottom TabView whose tabs exercise inputs, layout, a paged carousel,
/// lists/forms, and navigation with sheet + alert — all C#, all rendered as real SwiftUI.
///
/// All state lives here on the root view (per-view child-instance state is a separate milestone),
/// and tabs are built by methods so they share these State fields across renders.
/// </summary>
public sealed class ContentView : View
{
    // Inputs
    readonly State<string> _name = State("");
    readonly State<string> _password = State("");
    readonly State<double> _volume = State(0.4);
    readonly State<int> _quantity = State(1);
    readonly State<int> _size = State(1);
    readonly State<DateTime> _date = State(new DateTime(2026, 7, 17));
    readonly State<string> _color = State("#FF3B30");
    readonly State<bool> _notify = State(true);
    readonly State<int> _rating = State(3);
    readonly State<string> _gesture = State("Try the gestures below");
    readonly State<bool> _panel = State(false);

    // Lists
    readonly State<bool> _expanded = State(false);
    readonly State<string?> _selectedFruit = new(null);

    // Carousel
    readonly State<int> _carouselPage = State(0);

    // Nav
    readonly State<bool> _sheet = State(false);
    readonly State<bool> _alert = State(false);

    // Maps (renders as MapLibre on Web / MapKit on Apple; graceful ⚠️ placeholder where no renderer is registered)
    readonly State<MapCamera> _mapCamera = State(new MapCamera(new MapCoordinate(51.5074, -0.1278), 12));
    readonly State<List<MapCoordinate>> _route = State(new List<MapCoordinate>());

    static readonly string[] Sizes = { "Small", "Medium", "Large" };
    static readonly string[] Fruits = { "🍎 Apple", "🍌 Banana", "🍒 Cherry", "🥝 Kiwi", "🍑 Peach" };

    public override View Body =>
        new TabView(
            new Tab("Inputs", "slider.horizontal.3", InputsTab()),
            new Tab("Layout", "square.grid.2x2", LayoutTab()),
            new Tab("Carousel", "rectangle.stack", CarouselTab()),
            new Tab("Lists", "list.bullet", ListsTab()),
            new Tab("Maps", "map", MapsTab()),
            new Tab("Nav", "arrow.forward.circle", NavTab())
        );

    View MapsTab() =>
        new VStack(
            new Text("Maps").Font(Font.LargeTitle),
            new Text("Tap the map to drop a pin and extend the route.")
                .Font(Font.Caption).ForegroundColor(Color.Secondary),
            new Map(_mapCamera)
                .Pins(_route.Value.Select((c, i) => new MapPin(c, $"Stop {i + 1}", Color.Red, Id: i.ToString())))
                .Polylines(new[] { new MapPolyline(_route.Value, Color.Blue, 4) })
                .OnTap(c => _route.Value = new List<MapCoordinate>(_route.Value) { c })
                .OnCameraChanged(cam => _mapCamera.Value = cam)
                .Frame(height: 420),
            new Button("Clear route", () => _route.Value = new())
        ).Spacing(8).Padding(16);

    View InputsTab() =>
        new ScrollView(
            new Text("Inputs").Font(Font.LargeTitle),
            new Text(_name.Value.Length == 0 ? "Hello, stranger!" : $"Hello, {_name.Value}!")
                .Font(Font.Headline).ForegroundColor(Color.Blue),
            new TextField("Name", _name),
            new SecureField("Password", _password),
            new Text($"Volume: {_volume.Value:F2}").Font(Font.Caption),
            new Slider(_volume),
            new Stepper("Quantity:", _quantity, 0, 10),
            new Picker("Size", _size, Sizes),
            new DatePicker("Date", _date),
            new ColorPicker("Accent color", _color),
            new Toggle("Enable notifications", _notify),
            new Text(_notify.Value ? "🔔 On" : "🔕 Off").Font(Font.Caption).ForegroundColor(Color.Secondary),

            // .Disabled() dims + blocks interaction on any control; here it's bound live to the toggle.
            new Button("Configure…", () => _sheet.Value = true).Disabled(!_notify.Value),

            // .ScaleEffect() applies a native transform to any view — grows the star with the rating.
            new Text("★").Font(Font.Title).ForegroundColor(Color.Accent).ScaleEffect(1 + _rating.Value * 0.15),

            // A user-authored composite custom control (see Rating.cs) — works on every backend.
            new Text($"Rating: {_rating.Value}/5").Font(Font.Caption),
            new Rating(_rating),

            // One-shot gestures on the existing event channel — double-tap, long-press, and a directional swipe.
            new Divider(),
            new Text("Gestures").Font(Font.Headline),
            new Text(_gesture.Value).Font(Font.Caption).ForegroundColor(Color.Secondary),
            new Text("👆 Double-tap me")
                .Padding(12).Background(Color.Hex("#EEF0FF")).CornerRadius(10)
                .OnTapGesture(() => _gesture.Value = "Double-tapped 👆", count: 2),
            new Text("👇 Long-press me")
                .Padding(12).Background(Color.Hex("#EAF7EE")).CornerRadius(10)
                .OnLongPress(() => _gesture.Value = "Long-pressed 👇"),
            new Text("👈 Swipe me left")
                .Padding(12).Background(Color.Hex("#FDECEC")).CornerRadius(10)
                .OnSwipe(SwipeDirection.Left, () => _gesture.Value = "Swiped left 👈"),

            // Implicit animation: height + opacity interpolate when _panel flips, instead of snapping.
            new Divider(),
            new Text("Animation").Font(Font.Headline),
            new Button(_panel.Value ? "Collapse panel" : "Expand panel", () => _panel.Value = !_panel.Value),
            new VStack(
                new Text("Animated with .Animation(Anim.Spring(), on: _panel) — a real native spring on iOS/Compose/WinUI, a cubic-bezier on Web.")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary).Padding(12)
            ).Frame(height: _panel.Value ? 110 : 0)
             .Opacity(_panel.Value ? 1 : 0)
             .Background(Color.Hex("#EEF0FF")).CornerRadius(10)
             .Animation(Anim.Spring(), on: _panel.Value)
        ).Padding(20);

    View LayoutTab() =>
        new ScrollView(
            new Text("Layout").Font(Font.LargeTitle),
            new Grid(3,
                new Circle().Frame(70, 70).ForegroundColor(Color.Red),
                new Rectangle().Frame(70, 70).ForegroundColor(Color.Green),
                new Capsule().Frame(70, 44).ForegroundColor(Color.Blue),
                new RoundedRectangle(14).Frame(70, 70).ForegroundColor(Color.Accent),
                new Circle().Frame(70, 70).ForegroundColor(Color.Secondary),
                new Rectangle().Frame(70, 70).ForegroundColor(Color.Red).Opacity(0.4)
            ).Spacing(12),
            new Divider(),
            new ZStack(
                new RoundedRectangle(20).Frame(200, 100).ForegroundColor(Color.Blue),
                new Text("ZStack").Font(Font.Title).ForegroundColor(Color.Primary)
            ),
            new HStack(
                Image.System("star.fill").ForegroundColor(Color.Accent),
                new Label("Starred", "star"),
                new Spacer(),
                Image.System("heart.fill").ForegroundColor(Color.Red)
            ).Padding(),
            new ProgressView(0.6, "Downloading"),
            new Gauge(0.7, 0, 1, "Speed"),

            // Embedded web content — WKWebView (Apple), WebView2 (Windows), android.webkit.WebView,
            // and an <iframe> on the Web backend. HTML string renders with no network round-trip.
            new Divider(),
            new Text("WebView").Font(Font.Headline),
            WebView.FromHtml(
                "<div style='font-family:sans-serif;padding:16px;text-align:center'>" +
                "<h2>Hello from HTML 👋</h2><p>Rendered by the native web engine.</p></div>")
                .Frame(height: 160),
            new WebView("https://example.com").Frame(height: 220),

            new Divider(),
            new Text("Alignment & borders").Font(Font.Headline),
            new Text("← leading").Align(Alignment.Leading).Border(Color.Secondary),
            new Text("centered").Align(Alignment.Center).Border(Color.Secondary),
            new Text("trailing →").Align(Alignment.Trailing).Border(Color.Secondary),

            // A card: per-edge padding + background + corner radius + border + shadow
            new VStack(
                new Text("Card").Font(Font.Headline),
                new Text("padding · background · corner radius · border · shadow")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary)
            ).Alignment(HorizontalAlignment.Leading)
             .Spacing(4)
             .Padding(16)
             .Background(Color.Hex("#EEF0FF"))
             .CornerRadius(14)
             .Border(Color.Blue, 2, cornerRadius: 14)
             .Shadow(10, Color.Blue, y: 4)
        ).Padding(20);

    static readonly string[] CardLabels = { "① Swipe me", "② Real SwiftUI", "③ Paged TabView", "④ From C#" };

    View CarouselTab() =>
        new VStack(
            new Text($"Page {_carouselPage.Value + 1} of {CardLabels.Length}")
                .Font(Font.Headline),
            new TabView(
                Card(CardLabels[0], Color.Red),
                Card(CardLabels[1], Color.Green),
                Card(CardLabels[2], Color.Blue),
                Card(CardLabels[3], Color.Accent)
            ).Paged().SelectedIndex(_carouselPage),
            new HStack(
                new Button("◀ Prev", () => _carouselPage.Value = Math.Max(0, _carouselPage.Value - 1)),
                new Button("Next ▶", () => _carouselPage.Value = Math.Min(CardLabels.Length - 1, _carouselPage.Value + 1))
            ).Spacing(24)
        ).Spacing(12).Padding(16);

    static View Card(string title, SwiftColor color) =>
        new ZStack(
            new RoundedRectangle(24).ForegroundColor(color).Opacity(0.85).Padding(30),
            new Text(title).Font(Font.LargeTitle).ForegroundColor(Color.Primary)
        );

    View ListsTab() =>
        new Form(
            new Section($"Fruits — selected: {_selectedFruit.Value ?? "none"}",
                List.ForEach(Fruits, f => f, f => new Text(f)).Selection(_selectedFruit)),
            new Section("Disclosure",
                new DisclosureGroup("Show details", _expanded,
                    new Text("Hidden row 1"),
                    new Text("Hidden row 2"),
                    new Text($"Notifications: {(_notify.Value ? "on" : "off")}"))),
            new Section("Menu",
                new Menu("Actions",
                    new Button("Increment quantity", () => _quantity.Value++),
                    new Button("Reset", () => _quantity.Value = 0)))
        );

    View NavTab() =>
        new Alert(_alert, "Hello 👋", "This alert came from C# state.",
            new Sheet(_sheet,
                new NavigationStack(
                    new Form(
                        new NavigationLink("Go to details ›",
                            new VStack(
                                new Text("Details").Font(Font.LargeTitle),
                                new Text("Pushed onto the navigation stack.").ForegroundColor(Color.Secondary)
                            ).Padding().NavigationTitle("Details")),
                        new Button("Present a sheet", () => _sheet.Value = true),
                        new Button("Show an alert", () => _alert.Value = true),
                        new Link("Visit apple.com", "https://apple.com")
                    ).NavigationTitle("Navigation")
                ),
                new VStack(
                    new Text("Sheet content").Font(Font.Title),
                    new Text("Rendered from C#, presented by SwiftUI.").ForegroundColor(Color.Secondary),
                    new Button("Close", () => _sheet.Value = false)
                ).Padding(24)
            ));
}
