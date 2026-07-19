namespace SwiftDotNet.Controls;

/// <summary>
/// The row primitives used inside a <see cref="TableView"/> — ported (as a focused subset) from Shiny's
/// <c>Cells/</c>. Each is a plain composite that slots into a <see cref="Section"/>, so grouped/native
/// row styling comes for free on every backend.
/// </summary>
public static class Cell
{
    /// <summary>A title with an optional trailing detail value (Shiny's <c>LabelCell</c>).</summary>
    public static View Label(string title, string? detail = null, string? icon = null)
    {
        var lead = icon is null
            ? (View)new Text(title)
            : new HStack(Image.System(icon), new Text(title)).Spacing(10).Alignment(VerticalAlignment.Center);
        return detail is null
            ? Row(lead)
            : Row(lead, new Spacer(), new Text(detail).ForegroundColor(ControlPalette.OnSurfaceVariant));
    }

    /// <summary>A toggle row bound to a bool state (Shiny's <c>SwitchCell</c>). Uses the native Toggle.</summary>
    public static View Switch(string title, State<bool> isOn) => new Toggle(title, isOn);

    /// <summary>A tappable action row (Shiny's <c>ButtonCell</c>).</summary>
    public static View Button(string title, Action action, bool destructive = false)
        => Row(new Text(title).ForegroundColor(destructive ? ControlPalette.Accent(PillType.Critical) : ControlPalette.Accent(PillType.Info)))
            .OnTapGesture(action);

    /// <summary>An inline text-entry row (Shiny's <c>EntryCell</c>).</summary>
    public static View Entry(string title, State<string> text, string placeholder = "")
        => Row(new Text(title), new Spacer(), new TextField(placeholder, text).Frame(width: 160));

    /// <summary>A checkable row (Shiny's <c>CheckboxCell</c>/<c>SimpleCheckCell</c>).</summary>
    public static View Checkbox(string title, State<bool> isChecked)
        => Row(new Text(title), new Spacer(),
                new Text(isChecked.Value ? "☑" : "☐").ForegroundColor(isChecked.Value ? ControlPalette.Accent(PillType.Info) : ControlPalette.OnSurfaceVariant))
            .OnTapGesture(() => isChecked.Value = !isChecked.Value);

    /// <summary>A row that pushes a detail page (a navigation cell).</summary>
    public static View Navigation(string title, View destination)
        => new NavigationLink(new Text(title), destination);

    static HStack Row(params View[] content) =>
        new HStack(content).Spacing(8).Alignment(VerticalAlignment.Center);
}
