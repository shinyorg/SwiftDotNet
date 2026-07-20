using System;
using System.Collections.Generic;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>Collections: table, tree, data grid, staggered/carousel/reorderable, swipe-to-action.</summary>
public sealed class CollectionsSample : View
{
    readonly State<bool> _notify = State(true);
    readonly State<int> _carousel = State(0);
    readonly State<List<string>> _tasks = State(new List<string> { "Buy milk", "Walk the dog", "Write code", "Sleep" });

    static readonly (string Name, string Role, int Age)[] People =
        { ("Alice", "Engineer", 30), ("Bob", "Designer", 25), ("Carol", "PM", 41), ("Dave", "Engineer", 36) };

    public override View Body =>
        new ScrollView(new VStack(
            new Text("Table (settings)").Font(Font.Headline),
            new TableView(
                new TableSection("Account",
                    Cell.Label("Name", "Allan Ritchie"),
                    Cell.Switch("Notifications", _notify),
                    Cell.Navigation("Privacy", new Text("Privacy detail").Padding(20))),
                new TableSection("Danger",
                    Cell.Button("Delete account", () => Toast.Show("Deleted"), destructive: true))),

            new Text("Tree").Font(Font.Headline),
            new TreeView(
                new TreeNode("Documents", "folder",
                    new TreeNode("Resume.pdf", "doc"),
                    new TreeNode("Photos", "folder", new TreeNode("beach.jpg", "photo"), new TreeNode("hike.jpg", "photo"))),
                new TreeNode("Music", "folder", new TreeNode("song.mp3", "music"))
            ).OnSelect(n => Toast.Show($"Selected {n.Label}")),

            new Text("Data Grid").Font(Font.Headline),
            new DataGrid<(string Name, string Role, int Age)>(People,
                new DataGridColumn<(string Name, string Role, int Age)>("Name", x => x.Name, 100),
                new DataGridColumn<(string Name, string Role, int Age)>("Role", x => x.Role, 100),
                new DataGridColumn<(string Name, string Role, int Age)>("Age", x => x.Age.ToString(), 60, x => x.Age)),

            new Text("Staggered Grid").Font(Font.Headline),
            new StaggeredGrid(2,
                Tile("A", Color.Red, 60), Tile("B", Color.Blue, 90), Tile("C", Color.Green, 100),
                Tile("D", Color.Accent, 50), Tile("E", Color.Hex("#FF9500"), 80)
            ).ByHeight(i => new[] { 60.0, 90, 100, 50, 80 }[i]),

            new Text("Carousel (built-in paged TabView)").Font(Font.Headline),
            new TabView(Tile("Page 1", Color.Red, 120), Tile("Page 2", Color.Green, 120), Tile("Page 3", Color.Blue, 120))
                .Paged().SelectedIndex(_carousel).Frame(height: 150),

            new Text("Reorderable (drag ≡)").Font(Font.Headline),
            new ReorderableList<string>(_tasks, t => new Text(t)).RowHeight(48),

            new Text("Swipe row (drag left)").Font(Font.Headline),
            new SwipeContainer(
                new HStack(new Text("Swipe me left"), new Spacer()).Padding(Edge.Horizontal, 12),
                new SwipeAction("Delete", Color.Red, () => Toast.Show("Deleted"))).RowHeight(48)
        ).Spacing(14).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Collections");

    static View Tile(string label, SwiftColor color, double height) =>
        new ZStack(new Text(label).ForegroundColor(Color.Hex("#FFFFFF")))
            .Frame(150, height).Background(color).CornerRadius(10);
}
