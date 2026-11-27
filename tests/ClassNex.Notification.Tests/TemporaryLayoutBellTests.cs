using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Notification.Tests;

public sealed partial class NotificationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClassOffUsesOnlyTheSelectedDaysBreakNameAndExactDuration(bool independent)
    {
        var clock = new Clock(9, 19, 58);
        var date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var settings = new AppSettings();
        settings.TimeLayout.Times.Add(new() { Kind = ClassTimeKind.Break, Name = "基础课间", Start = "09:20", End = "09:29" });
        var schedule = new ScheduleService(() => settings, null, () => date);
        var plan = new TemporarySchedule(date, "周五调课", [new("体育", TimeSpan.FromHours(8), TimeSpan.FromMinutes(560)),
            new("班会", TimeSpan.FromMinutes(570), TimeSpan.FromMinutes(615))]);
        if (independent) plan = plan with { TimeLayout = [new(ClassTimeKind.Break, "补水休息", TimeSpan.FromMinutes(560), TimeSpan.FromMinutes(564.5))] };
        schedule.TemporarySchedules.Apply(plan);
        var screen = new Surface();
        using var service = new NotificationService(screen, () => settings, new TimeService(() => settings), schedule, clock);
        service.Start();
        clock.Advance(5);
        Assert.AreEqual(independent ? "补水休息" : "基础课间", screen.Masks.Single());
        Assert.AreEqual(independent ? "补水休息 4 分 30 秒" : "基础课间 9 分钟", screen.Overlays.Single().Caption);
        Assert.AreEqual("班会", screen.Overlays.Single().Subject);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DismissalWaitsForFinalActivityButIgnoresPointMarkersAndOtherDates(bool atMidnight)
    {
        var clock = atMidnight ? new Clock(23, 59, 51) : new Clock(10, 19, 51);
        var date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var settings = new AppSettings();
        settings.Notification.IsAfterSchoolNotificationEnabled = true;
        settings.TimeLayout.Times.Add(new() { Kind = ClassTimeKind.Break, Name = "基础延时", Start = "10:15", End = "22:00" });
        var schedule = new ScheduleService(() => settings, null, () => date);
        var end = atMidnight ? TimeSpan.FromDays(1) : TimeSpan.FromMinutes(620);
        var marker = atMidnight ? end - TimeSpan.FromSeconds(2) : end + TimeSpan.FromHours(1);
        schedule.TemporarySchedules.Apply(new TemporarySchedule(date, "运动会闭幕", [new("班会", TimeSpan.FromMinutes(570), TimeSpan.FromMinutes(615))])
        { TimeLayout = [new(ClassTimeKind.Action, "颁奖", end - TimeSpan.FromMinutes(5), end), new(ClassTimeKind.Separator, "分隔", marker, marker)] });
        var screen = new Surface();
        using var service = new NotificationService(screen, () => settings, new TimeService(() => settings), schedule, clock);
        service.Start();
        clock.Advance(8);
        Assert.IsEmpty(screen.Masks);
        clock.Advance(1);
        Assert.AreEqual("放学", screen.Masks.Single());
        clock.Advance(50);
        Assert.HasCount(1, screen.Masks);
    }
}
