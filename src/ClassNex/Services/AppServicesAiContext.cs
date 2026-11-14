using ClassNex.Core.Abstractions;
using ClassNex.Models;

namespace ClassNex.Services;

public sealed class AppServicesAiContext : IAiApplicationContext
{
    public AppSettings Settings => AppServices.Settings;

    public ScheduleProfile Profile => AppServices.Schedule.Profile;

    public IReadOnlyList<CourseSlot> GetTodaySlots(DateTime date) =>
        AppServices.Time.GetTodaySlots(AppServices.Schedule.Profile, date);

    public CourseSlot? GetCurrentCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now) =>
        AppServices.Time.GetCurrentCourse(slots, now);

    public CourseSlot? GetNextCourse(IReadOnlyList<CourseSlot> slots, TimeSpan now) =>
        AppServices.Time.GetNextCourse(slots, now);

    public string WorkspaceRoot => SettingsService.DataDirectory;

    public string Version => typeof(AppServicesAiContext).Assembly
        .GetName()
        .Version?
        .ToString() ?? "26w45b";
}
