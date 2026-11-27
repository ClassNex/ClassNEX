using ClassNex.Models;
using ClassNex.Services;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ClassroomScheduleContentTests
{
    private static readonly CourseSlot First = new("语文", "语", null, null, new(8, 0, 0), new(8, 40, 0));
    private static readonly CourseSlot Last = new("数学", "数", null, null, new(9, 10, 0), new(9, 50, 0));
    private static TodaySummary Day(TimeSpan now) => new(1, "周一", "11/23", "odd", [First, Last],
        First.Contains(now) ? First : Last.Contains(now) ? Last : null, now < First.Start ? First : now < Last.Start ? Last : null);

    [TestMethod]
    public void DisplayCountdownUsesTheActualIntervalNotTheVisibilityBoundaryOrNextBell()
    {
        var pause = new TemporaryTimeItem(ClassTimeKind.Break, "大课间", new(8, 42, 0), new(9, 5, 0));
        var exercise = new TemporaryTimeItem(ClassTimeKind.Action, "眼保健操", new(8, 50, 0), new(8, 55, 0));
        var assembly = new TemporaryTimeItem(ClassTimeKind.Action, "放学集合", Last.End, new(10, 0, 0));
        var point = new TemporaryTimeItem(ClassTimeKind.Action, "定点铃", exercise.End, exercise.End);
        TemporaryTimeItem[] periods = [point, exercise, assembly, pause];

        void Check(TimeSpan now, TemporaryTimeItem? active, TimeSpan until)
        {
            var state = WidgetClassroomPeriod.At(Day(now), now, periods, out var display);
            Assert.AreEqual(active?.Kind, state.Kind);
            Assert.AreEqual(until, state.Until);
            Assert.AreEqual(active?.Name, display?.Title);
            Assert.AreEqual(active?.Start, display?.Start);
            Assert.AreEqual(active?.End, display?.End);
        }
        Check(First.End, null, pause.Start);
        Check(pause.Start, pause, exercise.Start);
        Check(exercise.Start, exercise, exercise.End);
        Check(exercise.End, pause, pause.End);
        Check(pause.End, null, Last.Start);
        Check(Last.End, assembly, assembly.End);
        Check(assembly.End, null, TimeSpan.FromDays(1));
    }

    [TestMethod]
    public void DisplayHonorsAnExplicitDayOffIndependentActivitiesAndLegacyGaps()
    {
        var now = new TimeSpan(8, 45, 0);
        var day = Day(now);
        var basePause = new TemporaryTimeItem(ClassTimeKind.Break, "基础课间", First.End, Last.Start);
        var meeting = basePause with { Kind = ClassTimeKind.Action, Name = "班会" };
        WidgetClassroomPeriod.At(day with { Slots = [], Current = null, Next = null }, now, [basePause], out var dayOff);
        Assert.IsNull(dayOff);
        WidgetClassroomPeriod.At(day with { IndependentTimeLayout = [] }, now, [basePause], out var cleared);
        Assert.IsNull(cleared);
        WidgetClassroomPeriod.At(day with { Slots = [], Current = null, Next = null, IndependentTimeLayout = [meeting] }, now, [basePause], out var activity);
        Assert.AreEqual((meeting.Name, meeting.Start, meeting.End), activity);
        WidgetClassroomPeriod.At(day, now, [meeting with { End = meeting.Start }], out var inferred);
        Assert.AreEqual(("课间休息", First.End, Last.Start), inferred);
        WidgetClassroomPeriod.At(Day(Last.Start), Last.Start, [basePause with { End = Last.End }], out var lesson);
        Assert.AreEqual((Last.Subject, Last.Start, Last.End), lesson);
    }

    [TestMethod]
    public void ClockStringsAreParsedOnceUntilTheLocalOrReplacedTemplateChanges()
    {
        var original = new ClassTime { Name = "大课间", Kind = ClassTimeKind.Break, Start = "08:42:12", End = "09:05" };
        var settings = new AppSettings { TimeLayout = new() { Times = [original] } };
        var service = new TimeLayoutService(() => settings, new ScheduleService(() => settings), () => { });
        var first = service.Layout.GetClassroomPeriods();
        Assert.AreSame(first, service.Layout.GetClassroomPeriods());
        Assert.AreEqual(new TimeSpan(8, 42, 12), first.Single().Start);
        service.Update(original, new() { Name = "课后活动", Kind = ClassTimeKind.Action, Start = "09:50", End = "10:00" });
        var changed = service.Layout.GetClassroomPeriods();
        Assert.AreNotSame(first, changed);
        Assert.AreEqual("大课间", first.Single().Name);
        Assert.AreEqual("课后活动", changed.Single().Name);
        Assert.AreSame(changed, service.Layout.GetClassroomPeriods());
        service.Clear();
        Assert.IsEmpty(service.Layout.GetClassroomPeriods());
        settings.TimeLayout = new() { Times = [new() { Name = "晚自习活动", Kind = ClassTimeKind.Action, Start = "20:50", End = "21:00" }] };
        Assert.AreEqual("晚自习活动", service.Layout.GetClassroomPeriods().Single().Name);
    }
}
