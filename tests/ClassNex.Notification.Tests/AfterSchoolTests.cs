using System.Text.Json;
using ClassNex.Core.Abstractions;
using ClassNex.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Notification.Tests;

public sealed partial class NotificationTests
{
    [TestMethod]
    public void LastLessonCanEndWithOneDismissalAndACustomMessage()
    {
        using var f = new Fixture(new Clock(15, 59, 59));
        f.Lesson("美术", "美", "15:20", "16:00");
        f.Settings.Notification.IsAfterSchoolNotificationEnabled = true;
        f.Settings.Notification.AfterSchoolText = "带好课本，明天见。";
        f.Service.Start();
        f.Clock.Advance(1);
        f.Service.RefreshSchedule();
        CollectionAssert.AreEqual(new[] { "放学" }, f.Screen.Masks);
        Assert.AreEqual(TimeSpan.FromSeconds(35), f.Screen.Lifetimes.Single());
        f.Clock.Advance(4.99);
        Assert.IsEmpty(f.Screen.Overlays);
        f.Clock.Advance(0.01);
        var detail = f.Screen.Overlays.Single();
        Assert.AreEqual("带好课本，明天见。", detail.Message);
        Assert.AreEqual("", detail.Subject);
        Assert.AreEqual(TimeSpan.FromSeconds(30), detail.Track);
        Assert.IsFalse(detail.Vertical);
        f.Clock.Advance(45);
        Assert.AreEqual(1, f.Screen.Hides);
        Assert.HasCount(1, f.Screen.Masks);
    }

    [TestMethod]
    public void LastCourseStillRingsAndDismissalWaitsForTheFinalValidBreakToEnd()
    {
        using var f = new Fixture(new Clock(15, 59, 59));
        f.Lesson("美术", "美", "15:20", "16:00");
        f.Settings.Notification.IsAfterSchoolNotificationEnabled = true;
        f.Settings.TimeLayout.Times.AddRange([
            new() { Kind = ClassTimeKind.Break, Name = "整理书包", Start = "16:00:17", End = "16:01:11" },
            new() { Kind = ClassTimeKind.Action, Name = "值日", Start = "17:30", End = "17:45" },
            new() { Kind = ClassTimeKind.Break, Name = "大课间", Start = "16:00", End = "16:00:17" },
            new() { Kind = ClassTimeKind.Separator, Start = "17:00", End = "17:10" },
            new() { Kind = ClassTimeKind.Break, Start = "坏数据", End = "19:00" },
            new() { Kind = ClassTimeKind.Break, Start = "23:00", End = "19:00" }
        ]);
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual("大课间", f.Screen.Masks.Single());
        f.Clock.Advance(70);
        Assert.HasCount(1, f.Screen.Masks);
        f.Clock.Advance(1);
        f.Service.RefreshSchedule();
        f.Clock.Advance(90);
        CollectionAssert.AreEqual(new[] { "大课间", "放学" }, f.Screen.Masks);
    }

    [TestMethod]
    public void AnUnassignedLaterClassAndItsAdjacentBreaksDoNotExtendAShortDay()
    {
        using var f = new Fixture(new Clock(11, 59, 59));
        f.Lesson("美术", "美", "11:20", "12:00");
        f.Settings.Notification.IsAfterSchoolNotificationEnabled = true;
        f.Settings.TimeLayout.Times.AddRange([
            new() { Kind = ClassTimeKind.Break, Start = "12:00", End = "12:10" },
            new() { Kind = ClassTimeKind.Break, Start = "12:10", End = "13:00" },
            new() { Kind = ClassTimeKind.Class, Start = "13:00", End = "13:45" },
            new() { Kind = ClassTimeKind.Break, Start = "13:45", End = "14:00" }
        ]);
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual("放学", f.Screen.Masks.Single());
        f.Clock.Advance(7200);
        Assert.HasCount(1, f.Screen.Masks);
    }

    [TestMethod]
    public void WakingAfterTheFinalBreakDoesNotReplayDismissalOrItsPrecedingBell()
    {
        using var f = new Fixture(new Clock(15, 59, 59));
        f.Lesson("美术", "美", "15:20", "16:00");
        f.Settings.Notification.IsAfterSchoolNotificationEnabled = true;
        f.Settings.TimeLayout.Times.Add(new() { Kind = ClassTimeKind.Break, Start = "16:00", End = "16:01:11" });
        f.Service.Start();
        f.Clock.Sleep(90);
        f.Service.RefreshSchedule();
        Assert.IsEmpty(f.Screen.Masks);
    }

    [TestMethod]
    public void IntermediateBreakAndLateResumeAreNotDismissals()
    {
        using var f = new Fixture(new Clock(14, 59, 59));
        f.Lesson("历史", "历", "14:20", "15:00");
        f.Lesson("音乐", "音", "15:10", "15:50");
        f.Settings.Notification.IsAfterSchoolNotificationEnabled = true;
        f.Service.Start();
        f.Clock.Advance(1);
        Assert.AreEqual(f.Settings.Notification.ClassOffMaskText, f.Screen.Masks.Single());
        f.Clock.Sleep(11 * 60);
        f.Service.RefreshSchedule();
        f.Clock.Sleep(40 * 60);
        f.Service.RefreshSchedule();
        Assert.DoesNotContain("放学", f.Screen.Masks);
    }

    [TestMethod]
    [DataRow(15, 30, "当前是今天最后一节课")]
    [DataRow(16, 30, "今天的课程已结束")]
    public void NonemptyDayWithoutANextLessonNeverPreviewsAFabricatedCourse(int hour, int minute, string status)
    {
        using var f = new Fixture(new Clock(hour, minute));
        f.Lesson("历史", "历", "09:00", "09:45");
        f.Lesson("美术", "美", "15:20", "16:00");
        f.Settings.TimeLayout.Times.Add(new() { Kind = ClassTimeKind.Break, Name = "大课间", Start = "16:00", End = "16:10" });
        f.Service.Start();
        f.Service.DebugTriggerClassPreparing();
        f.Clock.Advance(3);
        f.Service.DebugTriggerClassOff();
        f.Clock.Advance(5);
        CollectionAssert.AreEqual(new[] { status, "大课间" }, f.Screen.Masks);
        Assert.IsEmpty(f.Screen.Overlays);
    }

    [TestMethod]
    public void EmptyTimetableCanStillDemonstratePreparationAndBreak()
    {
        using var f = new Fixture(new Clock(11, 15));
        f.Service.Start();
        f.Service.DebugTriggerClassPreparing();
        f.Clock.Advance(3);
        f.Service.DebugTriggerClassOff();
        f.Clock.Advance(2);
        Assert.HasCount(2, f.Screen.Overlays);
        Assert.IsTrue(f.Screen.Overlays.All(page => page.Subject == "演示课程"));
    }

    [TestMethod]
    public void DismissalCanBePreviewedWithoutEnablingItOrLoadingALesson()
    {
        using var f = new Fixture();
        f.Settings.Notification.AfterSchoolText = " ";
        f.Service.Start();
        ((INotificationService)f.Service).DebugTriggerAfterSchool();
        Assert.IsFalse(f.Settings.Notification.IsAfterSchoolNotificationEnabled);
        Assert.AreEqual("放学", f.Screen.Masks.Single());
        f.Clock.Advance(5);
        Assert.AreEqual(NotificationSettings.DefaultAfterSchoolText, f.Screen.Overlays.Single().Message);
    }

    [TestMethod]
    public void LegacySettingsKeepLastBellAndNewDismissalSettingsRoundTrip()
    {
        var legacy = JsonSerializer.Deserialize<AppSettings>("""{"Notification":{"ClassOffMaskText":"下课啦"}}""")!;
        Assert.IsFalse(legacy.Notification.IsAfterSchoolNotificationEnabled);
        Assert.AreEqual("下课啦", legacy.Notification.ClassOffMaskText);
        legacy.Notification.IsAfterSchoolNotificationEnabled = true;
        legacy.Notification.AfterSchoolText = "明天见。";
        legacy.UseLocalReminderSound = true;
        legacy.Notification.IsSoundEnabled = true;
        legacy.Notification.SoundVolume = 34;
        var synced = JsonSerializer.Deserialize<AppSettings>(CentralSettingsPayload.Serialize(legacy), JsonSerializerOptions.Web)!;
        Assert.IsTrue(synced.Notification.IsAfterSchoolNotificationEnabled);
        Assert.AreEqual(legacy.Notification.AfterSchoolText, synced.Notification.AfterSchoolText);
        Assert.IsTrue(synced.Notification.IsSoundEnabled);
        Assert.AreEqual(34, synced.Notification.SoundVolume);
        Assert.IsFalse(synced.UseLocalReminderSound);
        CentralSettingsPayload.PreserveLocal(synced, legacy);
        Assert.IsTrue(synced.UseLocalReminderSound);
    }
}
