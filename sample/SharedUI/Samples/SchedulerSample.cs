using System;
using SwiftDotNet;
using SwiftDotNet.Controls;

namespace SwiftDotNet.Sample;

/// <summary>The Scheduler suite: month calendar with event dots + the selected day's agenda.</summary>
public sealed class SchedulerSample : View
{
    readonly State<DateTime> _month = State(new DateTime(2026, 7, 1));
    readonly State<DateTime> _selected = State(new DateTime(2026, 7, 15));

    static readonly CalendarEvent[] Events =
    {
        new(new DateTime(2026, 7, 15), "Product launch", Color.Red),
        new(new DateTime(2026, 7, 15), "Team lunch", Color.Green),
        new(new DateTime(2026, 7, 22), "Sprint review", Color.Blue),
    };

    public override View Body =>
        new ScrollView(new VStack(
            new SchedulerCalendarView(_month, _selected, Events),
            new Text("Agenda").Font(Font.Headline),
            new SchedulerAgendaView(_selected, Events)
        ).Spacing(16).Alignment(HorizontalAlignment.Leading)
        ).Padding(20).NavigationTitle("Scheduler");
}
