using System.Globalization;

namespace SwiftDotNet.Controls;

/// <summary>A calendar event for the <see cref="SchedulerCalendarView"/>: a date, a title, and a tint.</summary>
public sealed record CalendarEvent(DateTime Date, string Title, SwiftColor Color);

/// <summary>
/// A month calendar with event dots and day selection — ported (in reduced form) from Shiny's Scheduler
/// suite. Pure composite: a header with prev/next, a weekday row, and a 6×7 day grid via the built-in
/// <see cref="Grid"/>. Binds the visible month and the selected day to <c>State</c>s; days with events
/// show a colored dot. Pair with <see cref="SchedulerAgendaView"/> to list the selected day's events.
/// </summary>
public sealed class SchedulerCalendarView : View
{
    readonly State<DateTime> _month;      // any day within the visible month
    readonly State<DateTime> _selected;
    readonly IReadOnlyList<CalendarEvent> _events;

    static readonly string[] Weekdays = { "S", "M", "T", "W", "T", "F", "S" };

    public SchedulerCalendarView(State<DateTime> month, State<DateTime> selected, IReadOnlyList<CalendarEvent> events)
    {
        _month = month;
        _selected = selected;
        _events = events;
    }

    public override View Body
    {
        get
        {
            var first = new DateTime(_month.Value.Year, _month.Value.Month, 1);
            var leading = (int)first.DayOfWeek;               // Sunday-first grid
            var daysInMonth = DateTime.DaysInMonth(first.Year, first.Month);

            var header = new HStack(
                new Text("‹").Font(Font.Title).OnTapGesture(() => _month.Value = first.AddMonths(-1)),
                new Spacer(),
                new Text(first.ToString("MMMM yyyy", CultureInfo.InvariantCulture)).Font(Font.Headline),
                new Spacer(),
                new Text("›").Font(Font.Title).OnTapGesture(() => _month.Value = first.AddMonths(1))
            ).Alignment(VerticalAlignment.Center);

            var weekdayRow = new Grid(7, Weekdays.Select(d =>
                (View)new Text(d).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant)).ToArray());

            // 42 cells (6 weeks): leading blanks, then the days.
            var cells = new View[42];
            for (var i = 0; i < 42; i++)
            {
                var dayNum = i - leading + 1;
                if (dayNum < 1 || dayNum > daysInMonth)
                {
                    cells[i] = new Text("");
                    continue;
                }
                var date = new DateTime(first.Year, first.Month, dayNum);
                cells[i] = DayCell(date);
            }

            return new VStack(header, weekdayRow, new Grid(7, cells).Spacing(4))
                .Spacing(10)
                .Padding(12)
                .Background(ControlPalette.Surface)
                .CornerRadius(14)
                .Border(ControlPalette.Outline, 1, cornerRadius: 14);
        }
    }

    View DayCell(DateTime date)
    {
        var isSelected = _selected.Value.Date == date.Date;
        var hasEvent = _events.Any(e => e.Date.Date == date.Date);
        var dot = hasEvent
            ? (View)new Circle().Frame(5, 5).ForegroundColor(_events.First(e => e.Date.Date == date.Date).Color)
            : new Text("").Frame(5, 5);

        return new VStack(
                new Text(date.Day.ToString(CultureInfo.InvariantCulture))
                    .Font(Font.Body)
                    .ForegroundColor(isSelected ? SwiftColor.Hex("#FFFFFF") : ControlPalette.OnSurface),
                dot)
            .Spacing(2)
            .Alignment(HorizontalAlignment.Center)
            .Frame(36, 40)
            .Background(isSelected ? ControlPalette.Accent(PillType.Info) : ControlPalette.Surface)
            .CornerRadius(8)
            .OnTapGesture(() => _selected.Value = date);
    }
}

/// <summary>A simple agenda list of the events on the <see cref="SchedulerCalendarView"/>'s selected day.</summary>
public sealed class SchedulerAgendaView : View
{
    readonly State<DateTime> _selected;
    readonly IReadOnlyList<CalendarEvent> _events;

    public SchedulerAgendaView(State<DateTime> selected, IReadOnlyList<CalendarEvent> events)
    {
        _selected = selected;
        _events = events;
    }

    public override View Body
    {
        get
        {
            var day = _events.Where(e => e.Date.Date == _selected.Value.Date)
                .OrderBy(e => e.Date).ToList();

            var rows = day.Count == 0
                ? new View[] { new Text("No events").ForegroundColor(ControlPalette.OnSurfaceVariant).Padding(8) }
                : day.Select(e => (View)new HStack(
                        new Circle().Frame(10, 10).ForegroundColor(e.Color),
                        new Text(e.Date.ToString("HH:mm", CultureInfo.InvariantCulture)).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant),
                        new Text(e.Title))
                    .Spacing(10).Alignment(VerticalAlignment.Center).Padding(Edge.Vertical, 6)).ToArray();

            return new VStack(rows).Spacing(2).Alignment(HorizontalAlignment.Leading);
        }
    }
}
