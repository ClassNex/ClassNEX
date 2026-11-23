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
        Assert.AreEqual(TimeSpan.FromSeconds(33), f.Screen.Lifetimes.Single());
        f.Clock.Advance(3);
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
    public void DismissalCanBePreviewedWithoutEnablingItOrLoadingALesson()
    {
        using var f = new Fixture();
        f.Settings.Notification.AfterSchoolText = " ";
        f.Service.Start();
        ((INotificationService)f.Service).DebugTriggerAfterSchool();
        Assert.IsFalse(f.Settings.Notification.IsAfterSchoolNotificationEnabled);
        Assert.AreEqual("放学", f.Screen.Masks.Single());
        f.Clock.Advance(3);
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
