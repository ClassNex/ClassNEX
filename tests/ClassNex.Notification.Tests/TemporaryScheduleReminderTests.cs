using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Notification.Tests;

public sealed partial class NotificationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ClassroomReschedulesTheBellWhenADayPlanIsAppliedOrWithdrawn(bool withdraw)
    {
        var clock = new Clock(7, 56, 58);
        clock.Sleep(TimeSpan.FromDays(4).TotalSeconds);
        var date = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        Assert.AreEqual(DayOfWeek.Friday, date.DayOfWeek);
        var settings = new AppSettings();
        var schedule = new ScheduleService(() => settings, null, () => date);
        schedule.AddCourse(5, "all", new() { Subject = "语文", StartTime = "09:00", EndTime = "09:45" });
        var screen = new Surface();
        using var service = new NotificationService(screen, () => settings, new TimeService(() => settings), schedule, clock);
        service.Start();
        schedule.TemporarySchedules.Apply(new(date, "校运动会周五调课", [new("体育", TimeSpan.FromHours(8), TimeSpan.FromMinutes(560)),
            new("班会", TimeSpan.FromMinutes(570), TimeSpan.FromMinutes(615))]));
        if (withdraw) schedule.TemporarySchedules.Remove(date);
        clock.Advance(5);
        if (withdraw) { Assert.IsEmpty(screen.Masks); return; }
        Assert.AreEqual("即将上课", screen.Masks.Single());
        Assert.AreEqual("体育", screen.Overlays.Single().Subject);
        Assert.AreEqual("距上课还剩 177 秒", screen.Overlays.Single().Caption);
    }
}
