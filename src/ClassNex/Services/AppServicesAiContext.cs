using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

internal sealed class AppServicesAiContext(Func<AppSettings> settings, IScheduleService schedule, ITimeService time, string workspaceRoot) : IAiApplicationContext
{
    public AppSettings Settings => settings();

    public ScheduleProfile Profile => schedule.Profile;

    public IReadOnlyList<CourseSlot> GetTodaySlots(DateTime date) =>
        time.GetTodaySlots(schedule.Profile, date);

    public CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now) =>
        time.GetCurrentCourse(slots, now);

    public CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now) =>
        time.GetNextCourse(slots, now);

    public string WorkspaceRoot => workspaceRoot;

    public string Version => typeof(AppServicesAiContext).Assembly
        .GetName()
        .Version?
        .ToString() ?? "26w46d";
}
