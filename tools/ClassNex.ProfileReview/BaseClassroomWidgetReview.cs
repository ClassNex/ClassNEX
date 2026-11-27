using Avalonia.Controls;
using Avalonia.VisualTree;
using ClassNex.Models;
using ClassNex.Services;
using ClassNex.Widgets;

internal static partial class Program
{
    private static void ReviewBaseClassroomWidget(DateTime date, Action<bool, string> check)
    {
        var settings = new AppSettings();
        var schedule = new ScheduleService(() => settings);
        var profile = schedule.Profile;
        profile.GetOrCreateSchedule(TimeService.DayNumberOf(date), "all").Classes =
            [new() { Subject = "语文", StartTime = "08:00", EndTime = "08:40" },
             new() { Subject = "数学", StartTime = "09:10", EndTime = "09:50" }];
        settings.TimeLayout.Times =
            [new() { Kind = ClassTimeKind.Break, Name = "大课间", Start = "08:42", End = "09:05" },
             new() { Kind = ClassTimeKind.Action, Name = "眼保健操", Start = "08:50", End = "08:55" },
             new() { Kind = ClassTimeKind.Action, Name = "放学集合", Start = "09:50", End = "10:00" }];
        var time = new TimeService(() => settings);
        using var days = new DayScheduleCache(time, schedule);
        var widget = new ScheduleWidget();
        void At(int hour, int minute, int second, string? title, string? remaining, string description)
        {
            var now = date.Date.Add(new TimeSpan(hour, minute, second));
            widget.Refresh(new WidgetContext { Profile = profile, Settings = settings, Now = now, Today = days.Read(now) });
            var labels = widget.View.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToArray();
            check(title is null ? widget.CurrentProgress is null : labels.Contains(title) && labels.Contains(remaining) && widget.CurrentProgress != null, description);
        }

        At(8, 41, 0, null, null, "the base timetable does not invent a break before its configured start");
        At(8, 45, 13, "大课间", "-19:47", "the base break displays its name and time until its own end, not the next lesson");
        At(8, 52, 10, "眼保健操", "-2:50", "a base activity temporarily replaces the enclosing break with its own countdown");
        At(8, 55, 0, "大课间", "-10:00", "the named base break resumes exactly when the activity ends");
        At(8, 52, 10, "眼保健操", "-2:50", "moving the classroom clock backward inside the same lesson gap reselects the activity");
        new TimeLayoutService(() => settings, schedule, () => { }).Update(settings.TimeLayout.Times[1],
            new() { Kind = ClassTimeKind.Action, Name = "室内操", Start = "08:50", End = "08:56" });
        At(8, 52, 10, "室内操", "-3:50", "saving an in-place base period edit immediately changes the same widget at the same clock instant");
        At(9, 5, 0, null, null, "a base break stops at its exclusive end even before the next lesson");
        At(9, 59, 20, "放学集合", "-0:40", "a base activity after the last lesson retains its final countdown");
        At(10, 0, 0, null, null, "the final base activity disappears at its exact ending");
        settings.TimeLayout = new();
        At(8, 41, 0, "课间休息", "-29:00", "a legacy schedule without a timed break or activity still infers the gap between lessons");
    }
}
