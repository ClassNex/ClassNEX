using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class ScheduleTests
{
    private static readonly DateTime Monday = new(2026, 11, 16);
    private static Course Period(string name, string start, string end) => new() { Subject = name, StartTime = start, EndTime = end };

    [TestMethod]
    public void WeekRotationAndHalfOpenBoundariesFollowTheClock()
    {
        var settings = new AppSettings { SingleWeekStartTime = Monday };
        var time = new TimeService(() => settings);
        var profile = new ScheduleProfile();
        profile.GetOrCreateSchedule(1, "all").Classes.Add(Period("数学", "08:00", "08:45"));
        profile.GetOrCreateSchedule(1, "odd").Classes.Add(Period("语文", "09:00", "09:45"));
        profile.GetOrCreateSchedule(1, "even").Classes.Add(Period("地理", "09:00", "09:45"));
        var start = time.GetTodaySummary(profile, Monday.AddHours(8));
        Assert.AreEqual("数学", start.Current?.Subject);
        var end = time.GetTodaySummary(profile, Monday.AddHours(8.75));
        Assert.IsNull(end.Current);
        Assert.AreEqual("语文", end.Next?.Subject);
        Assert.AreEqual("地理", time.GetTodaySummary(profile, Monday.AddDays(7).AddHours(9)).Current?.Subject);
        settings.WeekRotationOffset = 1;
        Assert.AreEqual("地理", time.GetTodaySummary(profile, Monday.AddHours(9)).Current?.Subject);
        Assert.IsTrue(time.GetTodaySummary(profile, Monday.AddDays(1)).IsEmpty);
    }

    [TestMethod]
    public void EditsInvalidateCachedSlotsButCallerCannotPoisonThem()
    {
        var settings = new AppSettings { SingleWeekStartTime = Monday };
        var schedule = new ScheduleService(() => settings);
        var time = new TimeService(() => settings);
        schedule.AddSubject("化学").Teacher = "甲";
        schedule.AddCourse(1, "all", Period("化学", "08:00", "08:45"));
        var slots = time.GetTodaySlots(schedule.Profile, Monday);
        Assert.AreEqual("甲", slots[0].Teacher);
        var first = slots[0];
        slots.Clear();
        Assert.AreSame(first, time.GetTodaySlots(schedule.Profile, Monday)[0]);
        schedule.Profile.Subjects[0].Teacher = "乙";
        schedule.Save();
        Assert.AreEqual("乙", time.GetTodaySlots(schedule.Profile, Monday)[0].Teacher);
        schedule.UpdateCourse(1, "all", schedule.Profile.Schedules[0].Classes[0], Period("化学", "10:00", "10:45"));
        Assert.IsNull(time.GetTodaySummary(schedule.Profile, Monday.AddHours(8)).Current);
    }

    [TestMethod]
    public void SettingsPersistAcrossInstancesAndPreservePreviousFileOnSerializationFailure()
    {
        var dir = Directory.CreateTempSubdirectory("classnex-core-test-");
        try
        {
            var path = Path.Combine(dir.FullName, "Settings.json");
            var store = new SettingsStore(path);
            var settings = store.Load();
            settings.ThemeMode = "dark";
            settings.WeekRotationOffset = 1;
            store.Save(settings);
            settings.FontScale = double.NaN;
            Assert.ThrowsExactly<ArgumentException>(() => store.Save(settings));
            var loaded = new SettingsStore(path).Load();
            Assert.AreEqual("dark", loaded.ThemeMode);
            Assert.AreEqual(1, loaded.WeekRotationOffset);
            Assert.HasCount(1, Directory.GetFiles(dir.FullName));
            File.WriteAllText(path, "{");
            Assert.ThrowsExactly<System.Text.Json.JsonException>(() => store.Load());
        }
        finally { dir.Delete(true); }
    }

    [TestMethod]
    public void LegacySettingsMigrateKnownEndpointsWithoutReplacingCustomOnes()
    {
        var dir = Directory.CreateTempSubdirectory("classnex-core-migrate-");
        try
        {
            var path = Path.Combine(dir.FullName, "Settings.json");
            File.WriteAllText(path, """{"accountEndpoint":"http://124.221.160.188:8000","centralManagement":{"serverUrl":"https://classroom.example/Control","clientId":"fixture"},"themeMode":"dark"}""");
            var loaded = new SettingsStore(path).Load();
            Assert.AreEqual(AppSettings.DefaultAccountEndpoint, loaded.AccountEndpoint);
            Assert.AreEqual("https://classroom.example/Control", loaded.CentralManagement.ServerUrl);
            Assert.AreEqual("fixture", loaded.CentralManagement.ClientId);
            Assert.AreEqual("dark", loaded.ThemeMode);
            Assert.AreEqual(AppSettings.DefaultAccountEndpoint, new SettingsStore(path).Load().AccountEndpoint);
        }
        finally { dir.Delete(true); }
    }

    [TestMethod]
    public void SavingAndReloadingCsesRebuildsCurrentProfileAndInterLessonBreak()
    {
        var dir = Directory.CreateTempSubdirectory("classnex-schedule-test-");
        try
        {
            var settings = new AppSettings { SingleWeekStartTime = Monday };
            var file = Path.Combine(dir.FullName, "课表.yaml");
            var schedule = new ScheduleService(() => settings);
            schedule.Load(file);
            schedule.AddCourse(1, "all", Period("语文", "08:00", "08:45"));
            schedule.AddCourse(1, "all", Period("美术", "09:00", "09:45"));
            schedule.Reload();
            var time = new TimeService(() => settings);
            var next = time.GetTodaySummary(schedule.Profile, Monday.AddHours(8.8));
            Assert.IsNull(next.Current);
            Assert.AreEqual("美术", next.Next?.Subject);
            Assert.IsTrue(TimeLayout.FromProfile(schedule.Profile).Times.Any(x => x.Kind == ClassTimeKind.Break));
        }
        finally { dir.Delete(true); }
    }
}
