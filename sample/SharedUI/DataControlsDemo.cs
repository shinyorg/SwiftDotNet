using System;
using System.Collections.Generic;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>The data-heavy ported controls: calendar scheduler, sortable DataGrid, country picker, autocomplete.</summary>
public sealed class DataControlsDemo : View
{
    readonly State<DateTime> _month = State(new DateTime(2026, 7, 1));
    readonly State<DateTime> _selected = State(new DateTime(2026, 7, 15));
    readonly State<Country?> _country = State<Country?>(null);
    readonly State<string> _fruit = State("");
    readonly State<TimeSpan> _duration = State(TimeSpan.FromMinutes(90));
    readonly State<int> _carousel = State(0);
    readonly State<List<string>> _tasks = State(new List<string> { "Buy milk", "Walk the dog", "Write code", "Sleep" });
    readonly State<string> _draft = State("");
    readonly State<List<ChatMessage>> _messages = State(new List<ChatMessage>
    {
        new("Hey! Are we still on for the launch?", IsMine: false, Sender: "Alex", Time: new DateTime(2026, 7, 15, 9, 3, 0)),
        new("Yep — 2pm works 👍", IsMine: true, Time: new DateTime(2026, 7, 15, 9, 4, 0)),
    });

    static readonly CalendarEvent[] Events =
    {
        new(new DateTime(2026, 7, 15), "Product launch", Color.Red),
        new(new DateTime(2026, 7, 15), "Team lunch", Color.Green),
        new(new DateTime(2026, 7, 22), "Sprint review", Color.Blue),
    };

    static readonly (string Name, string Role, int Age)[] People =
    {
        ("Alice", "Engineer", 30),
        ("Bob", "Designer", 25),
        ("Carol", "PM", 41),
        ("Dave", "Engineer", 36),
    };

    static readonly string[] Fruits =
    {
        "Apple", "Apricot", "Banana", "Blueberry", "Cherry", "Grape", "Mango", "Orange", "Peach", "Pear",
    };

    readonly State<bool> _notify = State(true);
    readonly State<CameraFacing> _camera = State(CameraFacing.Back);

    public override View Body =>
        new ScrollView(
            new VStack(
                new Text("Table (settings)").Font(Font.Headline),
                new TableView(
                    new TableSection("Account",
                        Cell.Label("Name", "Allan Ritchie"),
                        Cell.Switch("Notifications", _notify),
                        Cell.Navigation("Privacy", new Text("Privacy detail").Padding(20))),
                    new TableSection("Danger",
                        Cell.Button("Delete account", () => Toast.Show("Deleted"), destructive: true))),

                new Text("Image Viewer (tap)").Font(Font.Headline),
                ImageViewer.FromUrl("https://picsum.photos/400/300").ThumbnailSize(120),

                new Text("Camera").Font(Font.Headline),
                new Text("Live preview on camera-capable backends; ⚠️ placeholder otherwise.").Font(Font.Caption),
                new CameraView(_camera)
                    .Analyze(CameraAnalyzers.Barcodes)
                    .OnBarcode(b => Toast.Show($"{b.Kind}: {b.Value}"))
                    .Frame(height: 200),

                new Text("Scheduler").Font(Font.LargeTitle),
                new SchedulerCalendarView(_month, _selected, Events),
                new Text("Agenda").Font(Font.Headline),
                new SchedulerAgendaView(_selected, Events),

                new Text("Data Grid").Font(Font.Headline),
                new DataGrid<(string Name, string Role, int Age)>(People,
                    new DataGridColumn<(string Name, string Role, int Age)>("Name", x => x.Name, 100),
                    new DataGridColumn<(string Name, string Role, int Age)>("Role", x => x.Role, 100),
                    new DataGridColumn<(string Name, string Role, int Age)>("Age", x => x.Age.ToString(), 60, x => x.Age)),

                new Text("Staggered Grid").Font(Font.Headline),
                new StaggeredGrid(2,
                    Tile("A", Color.Red, 60), Tile("B", Color.Blue, 90),
                    Tile("C", Color.Green, 100), Tile("D", Color.Accent, 50),
                    Tile("E", Color.Hex("#FF9500"), 80)
                ).ByHeight(i => new[] { 60.0, 90, 100, 50, 80 }[i]),

                new Text("Carousel").Font(Font.Headline),
                new CarouselGallery(_carousel,
                    Tile("Page 1", Color.Red, 120), Tile("Page 2", Color.Green, 120), Tile("Page 3", Color.Blue, 120)
                ).Height(150),

                new Text("Reorderable (drag ≡)").Font(Font.Headline),
                new ReorderableList<string>(_tasks, t => new Text(t)).RowHeight(48),

                new Text("Country Picker").Font(Font.Headline),
                new CountryPicker(_country),

                new Text("Autocomplete").Font(Font.Headline),
                new AutoCompleteEntry(_fruit, Fruits).Placeholder("Type a fruit…"),

                new Text("Duration").Font(Font.Headline),
                new DurationPicker(_duration, seconds: true),

                new Text("Chat").Font(Font.Headline),
                new ChatView(_messages.Value, _draft)
                    .Typing()
                    .Height(220)
                    .OnSend(t =>
                    {
                        _messages.Value = new List<ChatMessage>(_messages.Value) { new(t, IsMine: true) };
                        _draft.Value = "";
                    })
            ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Data Controls");

    static View Tile(string label, SwiftColor color, double height) =>
        new ZStack(new Text(label).ForegroundColor(Color.Hex("#FFFFFF")))
            .Frame(150, height)
            .Background(color)
            .CornerRadius(10);
}
