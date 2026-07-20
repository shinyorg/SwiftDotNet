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
                NavButton("‹", () => _month.Value = first.AddMonths(-1)),
                new Spacer(),
                new Text(first.ToString("MMMM yyyy", CultureInfo.InvariantCulture))
                    .Font(Font.Headline).ForegroundColor(ControlPalette.OnSurface),
                new Spacer(),
                NavButton("›", () => _month.Value = first.AddMonths(1))
            ).Alignment(VerticalAlignment.Center);

            var weekdayRow = new Grid(7, Weekdays.Select(d =>
                (View)new Text(d).Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant)
                    .Frame(width: 40)).ToArray());

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

    static View NavButton(string glyph, Action action) =>
        new ZStack(new Text(glyph).Font(Font.Title).ForegroundColor(ControlPalette.Accent(PillType.Info)))
            .Frame(36, 36)
            .Background(ControlPalette.SurfaceVariant)
            .CornerRadius(18)
            .OnTapGesture(action);

    View DayCell(DateTime date)
    {
        var isSelected = _selected.Value.Date == date.Date;
        var dayEvents = _events.Where(e => e.Date.Date == date.Date).Take(3).ToList();

        // Up to three colored dots under the number, so a busy day reads at a glance.
        var dots = dayEvents.Count == 0
            ? (View)new ZStack().Frame(6, 6)
            : new HStack(dayEvents.Select(e => (View)new Circle().Frame(5, 5)
                    .ForegroundColor(isSelected ? SwiftColor.Hex("#FFFFFF") : e.Color)).ToArray())
                .Spacing(3);

        var number = new Text(date.Day.ToString(CultureInfo.InvariantCulture))
            .Font(Font.Body)
            .ForegroundColor(isSelected ? SwiftColor.Hex("#FFFFFF") : ControlPalette.OnSurface);

        return new VStack(number, dots)
            .Spacing(4)
            .Alignment(HorizontalAlignment.Center)
            .Frame(40, 48)
            .Background(isSelected ? ControlPalette.Accent(PillType.Info) : ControlPalette.Surface)
            .CornerRadius(10)
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

            if (day.Count == 0)
                return new Text("No events")
                    .Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant)
                    .Padding(14).Align(Alignment.Center);

            // Each event is a card with a colored leading bar, the time, and the title.
            var rows = day.Select(e => (View)new HStack(
                    new RoundedRectangle(2).Frame(4, 34).ForegroundColor(e.Color),
                    new VStack(
                        new Text(e.Title).Font(Font.Body).ForegroundColor(ControlPalette.OnSurface),
                        new Text(e.Date.ToString("HH:mm", CultureInfo.InvariantCulture))
                            .Font(Font.Caption).ForegroundColor(ControlPalette.OnSurfaceVariant))
                    .Spacing(2).Alignment(HorizontalAlignment.Leading),
                    new Spacer())
                .Spacing(10)
                .Alignment(VerticalAlignment.Center)
                .Padding(horizontal: 12, vertical: 8)
                .Background(ControlPalette.Surface)
                .CornerRadius(10)
                .Border(ControlPalette.Outline, 1, cornerRadius: 10)
                .Align(Alignment.Leading)).ToArray();   // claim full width (Spacer alone can't widen the row)

            return new VStack(rows).Spacing(8);
        }
    }
}
