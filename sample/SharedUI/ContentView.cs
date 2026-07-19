using SwiftDotNet;

namespace SwiftDotNet.Sample;

/// <summary>
/// A tour of the framework, organized MAUI-Shell-style: a flyout menu (a grouped <see cref="Form"/> inside a
/// <see cref="NavigationStack"/>) whose rows push detail pages. Each menu row is an icon + title
/// (<see cref="Label"/>); each <see cref="Section"/> is a flyout group. All C#, all rendered as real SwiftUI.
///
/// All state lives here on the root view (per-view child-instance state is a separate milestone), and pages
/// are built by methods so they share these State fields across renders and across push/pop.
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

    // Animation / gestures (transforms + continuous drag/pinch)
    readonly State<bool> _transformed = State(false);
    readonly State<bool> _pulsing = State(false);
    readonly State<double> _dragX = State(0.0);
    readonly State<double> _dragY = State(0.0);
    readonly State<double> _dragBaseX = State(0.0);
    readonly State<double> _dragBaseY = State(0.0);
    readonly State<double> _zoom = State(1.0);

    // Maps (renders as MapLibre on Web / MapKit on Apple; graceful ⚠️ placeholder where no renderer is registered)
    readonly State<MapCamera> _mapCamera = State(new MapCamera(new MapCoordinate(51.5074, -0.1278), 12));
    readonly State<List<MapCoordinate>> _route = State(new List<MapCoordinate>());

    static readonly string[] Sizes = { "Small", "Medium", "Large" };
    static readonly string[] Fruits = { "🍎 Apple", "🍌 Banana", "🍒 Cherry", "🥝 Kiwi", "🍑 Peach" };

    // The flyout: a grouped menu that pushes detail pages. Sections are the flyout groups; each row carries an
    // SF-Symbol icon and pushes onto the stack. This replaces the old bottom TabView tour.
    public override View Body =>
        new NavigationStack(
            new Form(
                new Section("Controls",
                    Row("Text & Input", "textformat", TextInputPage()),
                    Row("Values & Steppers", "slider.horizontal.3", ValuesPage()),
                    Row("Rating", "star", RatingPage())),

                new Section("Interaction",
                    Row("Gestures", "hand.tap", GesturesPage()),
                    Row("Animation", "wand.and.stars", AnimationPage())),

                new Section("Layout",
                    Row("Shapes & Grid", "square.grid.2x2", ShapesPage()),
                    Row("Stacks & Alignment", "rectangle.3.offgrid", AlignmentPage()),
                    Row("Cards & Borders", "rectangle.portrait", CardsPage())),

                new Section("Media",
                    Row("Carousel", "rectangle.stack", CarouselPage()),
                    Row("Indicators", "gauge", IndicatorsPage()),
                    Row("WebView", "globe", WebPage()),
                    Row("Maps", "map", MapsPage())),

                new Section("Data",
                    Row("Lists & Selection", "list.bullet", ListsPage()),
                    Row("Disclosure & Menus", "chevron.down.circle", DisclosurePage())),

                new Section("Styling",
                    Row("Global Styles", "paintbrush", StylesPage())),

                new Section("Shiny Controls",
                    Row("Status & Progress", "gauge", new StatusSample()),
                    Row("Inputs & Pickers", "slider.horizontal.3", new InputsSample()),
                    Row("Overlays & Media", "square.stack", new OverlaysSample()),
                    Row("Collections", "list.bullet", new CollectionsSample()),
                    Row("Scheduler", "calendar", new SchedulerSample()),
                    Row("Chat", "bubble.left.and.bubble.right", new ChatSample()),
                    Row("Camera", "camera", new CameraSample())),

                new Section("Navigation",
                    Row("Sheets & Alerts", "arrow.forward.circle", NavPage()))
            ).NavigationTitle("SwiftDotNet")
        );

    // A single flyout menu row: SF-Symbol icon + title that pushes a page.
    static NavigationLink Row(string title, string symbol, View destination) =>
        new NavigationLink(new Label(title, symbol), destination);

    // ── Controls ────────────────────────────────────────────────────────────

    View TextInputPage() =>
        new ScrollView(
            new Text("Text & Input").Font(Font.LargeTitle),
            new Text(_name.Value.Length == 0 ? "Hello, stranger!" : $"Hello, {_name.Value}!")
                .Font(Font.Headline).ForegroundColor(Color.Blue),
            new TextField("Name", _name),
            new SecureField("Password", _password),
            new Toggle("Enable notifications", _notify),
            new Text(_notify.Value ? "🔔 On" : "🔕 Off").Font(Font.Caption).ForegroundColor(Color.Secondary),

            // .Disabled() dims + blocks interaction on any control; here it's bound live to the toggle.
            new Button("Configure…", () => _sheet.Value = true).Disabled(!_notify.Value)
        ).Padding(20).NavigationTitle("Text & Input");

    View ValuesPage() =>
        new ScrollView(
            new Text("Values & Steppers").Font(Font.LargeTitle),
            new Text($"Volume: {_volume.Value:F2}").Font(Font.Caption),
            new Slider(_volume),
            new Stepper("Quantity:", _quantity, 0, 10),
            new Picker("Size", _size, Sizes),
            new DatePicker("Date", _date),
            new ColorPicker("Accent color", _color)
        ).Padding(20).NavigationTitle("Values & Steppers");

    View RatingPage() =>
        new ScrollView(
            new Text("Rating").Font(Font.LargeTitle),

            // .ScaleEffect() applies a native transform to any view — grows the star with the rating.
            new Text("★").Font(Font.Title).ForegroundColor(Color.Accent).ScaleEffect(1 + _rating.Value * 0.15),

            // A user-authored composite custom control (see Rating.cs) — works on every backend.
            new Text($"Rating: {_rating.Value}/5").Font(Font.Caption),
            new Rating(_rating)
        ).Padding(20).NavigationTitle("Rating");

    // ── Interaction ─────────────────────────────────────────────────────────

    View GesturesPage() =>
        new ScrollView(
            new Text("Gestures").Font(Font.LargeTitle),
            new Text(_gesture.Value).Font(Font.Headline),

            new Text("One-shot").Font(Font.Headline),
            new Text("👆 Double-tap me")
                .Padding(12).Background(Color.Hex("#EEF0FF")).CornerRadius(10)
                .OnTapGesture(() => _gesture.Value = "Double-tapped 👆", count: 2),
            new Text("👇 Long-press me")
                .Padding(12).Background(Color.Hex("#EAF7EE")).CornerRadius(10)
                .OnLongPress(() => _gesture.Value = "Long-pressed 👇"),
            new Text("👈 Swipe me left")
                .Padding(12).Background(Color.Hex("#FDECEC")).CornerRadius(10)
                .OnSwipe(SwipeDirection.Left, () => _gesture.Value = "Swiped left 👈"),

            new Divider(),

            // Continuous drag (F1) — began/changed/ended with cumulative translation.
            new Text("Continuous drag & pinch").Font(Font.Headline),
            new ZStack(
                new ZStack(new Text("✋").Font(Font.Title))
                    .Frame(70, 70).Background(Color.Hex("#FFE0B2")).CornerRadius(14)
                    .Offset(_dragX.Value, _dragY.Value)
                    .OnDrag(info =>
                    {
                        _dragX.Value = _dragBaseX.Value + info.TranslationX;
                        _dragY.Value = _dragBaseY.Value + info.TranslationY;
                        if (info.Phase == GesturePhase.Ended) { _dragBaseX.Value = _dragX.Value; _dragBaseY.Value = _dragY.Value; }
                        _gesture.Value = $"Drag: ({info.TranslationX:0}, {info.TranslationY:0})";
                    })
            ).Frame(height: 180).Alignment(Alignment.Center),

            // Continuous pinch (F1) — cumulative scale factor.
            new ZStack(new Text("🔍 Pinch me").Font(Font.Headline).ForegroundColor(Color.Hex("#FFFFFF")))
                .Frame(220, 90).Background(Color.Blue).CornerRadius(14)
                .ScaleEffect(_zoom.Value)
                .OnMagnify(scale => { _zoom.Value = Math.Clamp(scale, 0.5, 3); _gesture.Value = $"Zoom: {scale:0.0}×"; })
        ).Padding(20).NavigationTitle("Gestures");

    View AnimationPage() =>
        new ScrollView(
            new Text("Animation").Font(Font.LargeTitle),

            // 1) Implicit height + opacity spring.
            new Text("Height & opacity").Font(Font.Headline),
            new Button(_panel.Value ? "Collapse panel" : "Expand panel", () => _panel.Value = !_panel.Value),
            new VStack(
                new Text(".Animation(Anim.Spring(), on: _panel) — a real native spring on iOS/Compose/WinUI, a cubic-bezier on Web.")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary).Padding(12)
            ).Frame(height: _panel.Value ? 110 : 0)
             .Opacity(_panel.Value ? 1 : 0)
             .Background(Color.Hex("#EEF0FF")).CornerRadius(10)
             .Animation(Anim.Spring(), on: _panel.Value),

            new Divider(),

            // 2) Transform modifiers (F4): offset + rotation + scale, animated together on toggle.
            new Text("Transforms — offset · rotation · scale").Font(Font.Headline),
            new Button(_transformed.Value ? "Reset" : "Transform", () => _transformed.Value = !_transformed.Value),
            new ZStack(
                new ZStack(new Text("↗︎").Font(Font.LargeTitle).ForegroundColor(Color.Hex("#FFFFFF")))
                    .Frame(80, 80)
                    .Background(new LinearGradient(45, new GradientStop(Color.Hex("#7C4DFF"), 0), new GradientStop(Color.Hex("#00BCD4"), 1)))
                    .CornerRadius(16)
                    .Offset(_transformed.Value ? 90 : 0, 0)
                    .Rotation(_transformed.Value ? 25 : 0)
                    .ScaleEffect(_transformed.Value ? 1.3 : 1.0)
                    .Animation(Anim.Spring(), on: _transformed.Value)
            ).Frame(height: 120).Alignment(Alignment.Leading),

            new Divider(),

            // 3) Looping animation (F4): a self-reversing repeat drives a pulsing scale forever.
            new Text("Looping pulse").Font(Font.Headline),
            new Button(_pulsing.Value ? "Stop" : "Start pulsing", () => _pulsing.Value = !_pulsing.Value),
            new ZStack(
                new Circle().Frame(60, 60).ForegroundColor(Color.Red)
                    .ScaleEffect(_pulsing.Value ? 1.4 : 1.0)
                    .Opacity(_pulsing.Value ? 0.6 : 1.0)
                    .Animation(Anim.EaseInOut(0.7).Repeating(autoreverse: true), on: _pulsing.Value)
            ).Frame(height: 100)
        ).Padding(20).NavigationTitle("Animation");

    // ── Layout ──────────────────────────────────────────────────────────────

    View ShapesPage() =>
        new ScrollView(
            new Text("Shapes & Grid").Font(Font.LargeTitle),
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
            )
        ).Padding(20).NavigationTitle("Shapes & Grid");

    View AlignmentPage() =>
        new ScrollView(
            new Text("Stacks & Alignment").Font(Font.LargeTitle),
            new HStack(
                Image.System("star.fill").ForegroundColor(Color.Accent),
                new Label("Starred", "star"),
                new Spacer(),
                Image.System("heart.fill").ForegroundColor(Color.Red)
            ).Padding(),
            new Divider(),
            new Text("← leading").Align(Alignment.Leading).Border(Color.Secondary),
            new Text("centered").Align(Alignment.Center).Border(Color.Secondary),
            new Text("trailing →").Align(Alignment.Trailing).Border(Color.Secondary)
        ).Padding(20).NavigationTitle("Stacks & Alignment");

    View CardsPage() =>
        new ScrollView(
            new Text("Cards & Borders").Font(Font.LargeTitle),

            // A solid card: per-edge padding + background + corner radius + border + shadow
            new VStack(
                new Text("Solid card").Font(Font.Headline),
                new Text("padding · background · corner radius · border · shadow")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary)
            ).Alignment(HorizontalAlignment.Leading)
             .Spacing(4).Padding(16)
             .Background(Color.Hex("#EEF0FF")).CornerRadius(14)
             .Border(Color.Blue, 2, cornerRadius: 14).Shadow(10, Color.Blue, y: 4),

            // A linear-gradient card (F5) — a real gradient shader/CSS/SwiftUI LinearGradient.
            new VStack(
                new Text("Linear gradient").Font(Font.Headline).ForegroundColor(Color.Hex("#FFFFFF")),
                new Text(".Background(new LinearGradient(45, …))").Font(Font.Caption).ForegroundColor(Color.Hex("#F0F0FF"))
            ).Alignment(HorizontalAlignment.Leading).Spacing(4).Padding(16)
             .Background(new LinearGradient(45,
                 new GradientStop(Color.Hex("#7C4DFF"), 0), new GradientStop(Color.Hex("#00BCD4"), 1)))
             .CornerRadius(14).Shadow(8, y: 4),

            // A radial-gradient card (F5).
            new VStack(
                new Text("Radial gradient").Font(Font.Headline).ForegroundColor(Color.Hex("#FFFFFF"))
            ).Alignment(HorizontalAlignment.Leading).Padding(16)
             .Background(new RadialGradient(Color.Hex("#FF5252"), Color.Hex("#7B1FA2")))
             .CornerRadius(14),

            // A frosted-glass material card (F6) over a gradient backdrop — real backdrop blur on Web/SwiftUI.
            new ZStack(
                new RoundedRectangle(16).Frame(300, 120).ForegroundColor(Color.Hex("#FF9500")),
                new VStack(
                    new Text("Material").Font(Font.Headline),
                    new Text(".Material(MaterialStyle.Thin)").Font(Font.Caption)
                ).Spacing(4).Padding(16).Material(MaterialStyle.Thin).CornerRadius(12)
            ),

            // Shadow & corner variations.
            new HStack(
                new RoundedRectangle(4).Frame(80, 80).ForegroundColor(Color.Hex("#EEF0FF")).Shadow(2, y: 1),
                new RoundedRectangle(16).Frame(80, 80).ForegroundColor(Color.Hex("#EEF0FF")).Shadow(8, y: 4),
                new RoundedRectangle(40).Frame(80, 80).ForegroundColor(Color.Hex("#EEF0FF")).Shadow(16, Color.Blue, y: 8)
            ).Spacing(12)
        ).Padding(20).NavigationTitle("Cards & Borders");

    // ── Media ───────────────────────────────────────────────────────────────

    static readonly string[] CardLabels = { "① Swipe me", "② Real SwiftUI", "③ Paged TabView", "④ From C#" };

    View CarouselPage() =>
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
        ).Spacing(12).Padding(16).NavigationTitle("Carousel");

    static View Card(string title, SwiftColor color) =>
        new ZStack(
            new RoundedRectangle(24).ForegroundColor(color).Opacity(0.85).Padding(30),
            new Text(title).Font(Font.LargeTitle).ForegroundColor(Color.Primary)
        );

    View IndicatorsPage() =>
        new ScrollView(
            new Text("Indicators").Font(Font.LargeTitle),
            new ProgressView(0.6, "Downloading"),
            new Gauge(0.7, 0, 1, "Speed")
        ).Padding(20).NavigationTitle("Indicators");

    View WebPage() =>
        new ScrollView(
            new Text("WebView").Font(Font.LargeTitle),

            // Embedded web content — WKWebView (Apple), WebView2 (Windows), android.webkit.WebView,
            // and an <iframe> on the Web backend. HTML string renders with no network round-trip.
            WebView.FromHtml(
                "<div style='font-family:sans-serif;padding:16px;text-align:center'>" +
                "<h2>Hello from HTML 👋</h2><p>Rendered by the native web engine.</p></div>")
                .Frame(height: 160),
            new WebView("https://example.com").Frame(height: 220)
        ).Padding(20).NavigationTitle("WebView");

    View MapsPage() =>
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
        ).Spacing(8).Padding(16).NavigationTitle("Maps");

    // ── Data ────────────────────────────────────────────────────────────────

    View ListsPage() =>
        new Form(
            new Section($"Fruits — selected: {_selectedFruit.Value ?? "none"}",
                List.ForEach(Fruits, f => f, f => new Text(f)).Selection(_selectedFruit))
        ).NavigationTitle("Lists & Selection");

    View DisclosurePage() =>
        new Form(
            new Section("Disclosure",
                new DisclosureGroup("Show details", _expanded,
                    new Text("Hidden row 1"),
                    new Text("Hidden row 2"),
                    new Text($"Notifications: {(_notify.Value ? "on" : "off")}"))),
            new Section("Menu",
                new Menu("Actions",
                    new Button("Increment quantity", () => _quantity.Value++),
                    new Button("Reset", () => _quantity.Value = 0)))
        ).NavigationTitle("Disclosure & Menus");

    // ── Styling ─────────────────────────────────────────────────────────────

    // Global styles, SwiftUI-style but resolved in C#: values set on a container cascade to descendants
    // that don't set their own. Everything below is wrapped once in a Theme + a default ButtonStyle; the
    // inner VStack adds an ambient font/color. None of the leaf Texts or Buttons style themselves.
    View StylesPage() =>
        new ScrollView(
            new VStack(
                new Text("Global styles").Font(Font.LargeTitle),
                new Text("Set a font, color, control style, or theme once — descendants inherit it.")
                    .Font(Font.Caption).ForegroundColor(Color.Secondary),

                // B — cascade: these three Texts declare no font/color; they inherit the ambient environment.
                new VStack(
                    new Text("Inherited heading"),
                    new Text("Inherited line one"),
                    new Text("Inherited line two")
                ).Spacing(6).Align(Alignment.Leading)
                 .Environment(e => e.Font(Font.Headline).ForegroundColor(Color.Accent)),

                // A — reusable bundle: a themed card look, its padding/surface/radius read from the Theme.
                new VStack(
                    new Text("Reusable bundle").Font(Font.Headline),
                    new Text(".CardStyle() pulls padding, surface, radius and shadow from the Theme.")
                        .Font(Font.Caption)
                ).Spacing(4).Align(Alignment.Leading).CardStyle(),

                // C — control style: neither button sets a style; both adopt the ambient FilledButtonStyle.
                new Text("Ambient button style").Font(Font.Headline).Align(Alignment.Leading),
                new HStack(
                    new Button("Primary", () => _quantity.Value++),
                    new Button("Secondary", () => _quantity.Value = 0)
                ).Spacing(12)
            ).Spacing(16).Padding(20)
        ).ButtonStyle(new FilledButtonStyle())
         .Theme(new Theme { Accent = Color.Hex("#7C4DFF"), Surface = Color.Hex("#F0EEFF"), CornerRadius = 16 })
         .NavigationTitle("Global Styles");

    // ── Navigation ──────────────────────────────────────────────────────────

    // Sheets + alerts. This page already lives inside the flyout's NavigationStack, so it presents a nested
    // Form of push/sheet/alert/link demos directly — no second NavigationStack needed.
    View NavPage() =>
        new Alert(_alert, "Hello 👋", "This alert came from C# state.",
            new Sheet(_sheet,
                new Form(
                    new NavigationLink("Go to details ›",
                        new VStack(
                            new Text("Details").Font(Font.LargeTitle),
                            new Text("Pushed onto the navigation stack.").ForegroundColor(Color.Secondary)
                        ).Padding().NavigationTitle("Details")),
                    new Button("Present a sheet", () => _sheet.Value = true),
                    new Button("Show an alert", () => _alert.Value = true),
                    new Link("Visit apple.com", "https://apple.com")
                ).NavigationTitle("Sheets & Alerts"),
                new VStack(
                    new Text("Sheet content").Font(Font.Title),
                    new Text("Rendered from C#, presented by SwiftUI.").ForegroundColor(Color.Secondary),
                    new Button("Close", () => _sheet.Value = false)
                ).Padding(24)
            ));
}
