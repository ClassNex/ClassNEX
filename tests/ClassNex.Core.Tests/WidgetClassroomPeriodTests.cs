using System.Text.Json;
using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class WidgetClassroomPeriodTests
{
    private static readonly DateTime Monday = new(2026, 11, 23);
    private static readonly TimeService Clock = new(() => new AppSettings());
    private static readonly ScheduleProfile Timetable = new()
    {
        Schedules = [new() { EnableDay = 1, Classes =
            [new() { Subject = "语文", StartTime = "08:00", EndTime = "08:40" },
             new() { Subject = "数学", StartTime = "08:50", EndTime = "09:30" }] }]
    };

    private static TimeSpan At(int hour, int minute = 0) => new(hour, minute, 0);
    private static TodaySummary Day(TimeSpan now) => Clock.GetTodaySummary(Timetable, Monday + now);
    private static WidgetClassroomPeriod Period(TimeSpan now, params TemporaryTimeItem[] periods) =>
        WidgetClassroomPeriod.At(Day(now), now, periods);

    [TestMethod]
    public void LessonAndInferredBreakChangeAtTheBell()
    {
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, At(8)), Period(At(8) - TimeSpan.FromTicks(1)));
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Class, false, At(8, 40)), Period(At(8)));
        Assert.AreEqual(ClassTimeKind.Class, Period(At(8, 40) - TimeSpan.FromTicks(1)).Kind);
        var pause = Period(At(8, 40));
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Break, false, At(8, 50)), pause);
        Assert.IsTrue(pause.Allows(WidgetDisplayWhen.Break));
        Assert.IsFalse(pause.Allows(WidgetDisplayWhen.Lesson));
        Assert.AreEqual(ClassTimeKind.Break, Period(At(8, 50) - TimeSpan.FromTicks(1)).Kind);
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Class, false, At(9, 30)), Period(At(8, 50)));
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, TimeSpan.FromDays(1)), Period(At(9, 30)));
    }

    [TestMethod]
    public void ExplicitBreaksLeaveIdleGapsAndGiveWayToActivities()
    {
        TemporaryTimeItem[] periods =
        [new(ClassTimeKind.Break, "课间", At(8, 41), At(8, 49)),
         new(ClassTimeKind.Action, "集合", At(8, 43), At(8, 45)),
         new(ClassTimeKind.Action, "定点铃", At(8, 42), At(8, 42))];
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, At(8, 41)), Period(At(8, 40), periods));
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Break, false, At(8, 43)), Period(At(8, 42), periods));
        var activity = Period(At(8, 43), periods);
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Action, false, At(8, 45)), activity);
        Assert.IsTrue(activity.Allows(WidgetDisplayWhen.Activity));
        Assert.IsFalse(activity.Allows(WidgetDisplayWhen.Break));
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Break, false, At(8, 49)), Period(At(8, 45), periods));
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, At(8, 50)), Period(At(8, 49), periods));
    }

    [TestMethod]
    public void EmptyIndependentTimeLayoutSuppressesBasePeriodsAndInferredBreaks()
    {
        var now = At(8, 40);
        var day = Day(now) with { IndependentTimeLayout = [] };
        TemporaryTimeItem[] periods = [new(ClassTimeKind.Action, "基础作息的活动", now, At(8, 50))];
        Assert.AreEqual(ClassTimeKind.Action, Period(now, periods).Kind);
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, At(8, 50)), WidgetClassroomPeriod.At(day, now, periods));
        Assert.AreEqual(new WidgetClassroomPeriod(null, false, At(8, 50)), WidgetClassroomPeriod.At(day, now, []));
    }

    [TestMethod]
    public void EmptyDaysIgnoreBasePeriodsButCanContainAnIndependentActivity()
    {
        var now = At(8, 40);
        var day = Clock.GetTodaySummary(Timetable, Monday.AddDays(1) + now);
        var assembly = new TemporaryTimeItem(ClassTimeKind.Action, "集合", now, At(9));
        var quiet = WidgetClassroomPeriod.At(day, now, [assembly]);
        Assert.AreEqual(new WidgetClassroomPeriod(null, true, TimeSpan.FromDays(1)), quiet);
        Assert.IsTrue(quiet.Allows(WidgetDisplayWhen.NoLessonsToday));
        Assert.IsFalse(quiet.Allows(WidgetDisplayWhen.Activity));

        var independent = day with { IndependentTimeLayout = [assembly] };
        var occupied = WidgetClassroomPeriod.At(independent, now, []);
        Assert.AreEqual(new WidgetClassroomPeriod(ClassTimeKind.Action, true, At(9)), occupied);
        Assert.IsTrue(occupied.Allows(WidgetDisplayWhen.Activity));
        Assert.IsTrue(occupied.Allows(WidgetDisplayWhen.NoLessonsToday));
        Assert.AreEqual(quiet, WidgetClassroomPeriod.At(independent, At(9), []));
    }

    [TestMethod]
    public void SettingsWithoutAConditionAndFutureEnumValuesBothKeepTheWidgetVisible()
    {
        var legacy = JsonSerializer.Deserialize<WidgetConfig>("""{"Type":"clock"}""")!;
        var future = JsonSerializer.Deserialize<WidgetConfig>("""{"Type":"clock","DisplayWhen":99}""")!;
        Assert.AreEqual(WidgetDisplayWhen.Always, legacy.DisplayWhen);
        Assert.AreEqual((WidgetDisplayWhen)99, future.Copy().DisplayWhen);
        var idle = Period(At(7, 59));
        Assert.IsTrue(idle.Allows(legacy.DisplayWhen));
        Assert.IsTrue(idle.Allows(future.DisplayWhen));
    }
}
