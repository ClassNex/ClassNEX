using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class TemporaryScheduleTests
{
    private static readonly DateTime Monday = new(2026, 11, 23);
    private static DateOnly Day(int offset = 0) => DateOnly.FromDateTime(Monday.AddDays(offset));
    private static TemporarySchedule SportsDay(int offset = 0) => new(Day(offset), "校运动会调课",
        [new("体育", TimeSpan.FromHours(8), TimeSpan.FromHours(10))]);

    private sealed class Fixture : IDisposable
    {
        internal readonly DirectoryInfo Directory = System.IO.Directory.CreateTempSubdirectory("classnex-day-plan-");
        internal readonly AppSettings Settings = new() { SingleWeekStartTime = Monday };
        internal readonly TimeService Time;
        internal ScheduleService Schedule { get; }
        internal DateOnly Today = Day(-1);
        internal string FileName => Path.Combine(Directory.FullName, "七年级课表.yaml");
        internal string Overrides => Path.Combine(Directory.FullName, "TemporarySchedules");

        internal Fixture()
        {
            Time = new(() => Settings);
            Schedule = Open();
            Schedule.Profile.Subjects.Add(new() { Name = "体育", Teacher = "张老师", SimplifiedName = "体" });
            Schedule.Profile.ImportedTimeLayout = new() { Times = [
                new() { Kind = ClassTimeKind.Break, Name = "大课间", Start = "10:00", End = "10:25" },
                new() { Kind = ClassTimeKind.Action, Name = "眼保健操", Start = "10:20", End = "10:20" }] };
            Schedule.AddCourse(1, "odd", new() { Subject = "语文", StartTime = "08:00", EndTime = "08:45" });
            Schedule.AddCourse(1, "even", new() { Subject = "班会", StartTime = "08:00", EndTime = "08:45" });
            Schedule.AddCourse(2, "all", new() { Subject = "历史", StartTime = "08:00", EndTime = "08:45" });
        }

        internal ScheduleService Open()
        {
            var service = new ScheduleService(() => Settings, Overrides, () => Today);
            service.Load(FileName);
            return service;
        }
        public void Dispose() => Directory.Delete(true);
    }

    [TestMethod]
    public void DateOverrideWinsOnlyOnThatDateAndKeepsNormalWeekAndSubjectData()
    {
        using var f = new Fixture();
        using var cache = new DayScheduleCache(f.Time, f.Schedule);
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        Assert.AreEqual("语文", f.Time.GetDaySlots(f.Schedule.Profile, 1, "odd").Single().Subject);
        Assert.IsEmpty(cache.Read(Monday.AddTicks(-1)).Slots);
        Assert.AreEqual("体育", cache.Read(Monday).Next?.Subject);
        Assert.AreEqual("张老师", cache.Read(Monday.AddHours(8)).Current?.Teacher);
        Assert.AreEqual("体", f.Time.GetTodaySlots(f.Schedule.Profile, Monday).Single().DisplayName);
        Assert.IsNull(cache.Read(Monday.AddHours(10)).Current);
        Assert.AreEqual("体育", cache.Read(Monday.AddDays(1).AddTicks(-1)).Slots.Single().Subject);
        Assert.AreEqual("历史", cache.Read(Monday.AddDays(1)).Next?.Subject);
        Assert.AreEqual("班会", cache.Read(Monday.AddDays(7)).Next?.Subject);
    }

    [TestMethod]
    public void ReplaceCancelAndNoSchoolInvalidateTheLiveCacheWithoutPublishingBaseEdits()
    {
        using var f = new Fixture();
        using var cache = new DayScheduleCache(f.Time, f.Schedule);
        var changes = 0;
        var saved = 0;
        f.Schedule.ProfileChanged += () => changes++;
        f.Schedule.Saved += () => saved++;
        Assert.AreEqual("语文", cache.Read(Monday).Next?.Subject);
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        f.Schedule.TemporarySchedules.Apply(SportsDay(1));
        Assert.AreEqual("体育", cache.Read(Monday).Next?.Subject);
        f.Schedule.TemporarySchedules.Apply(new(Day(), "运动会停课", []));
        Assert.IsTrue(cache.Read(Monday).IsEmpty);
        Assert.IsTrue(f.Schedule.TemporarySchedules.Remove(Day()));
        Assert.IsFalse(f.Schedule.TemporarySchedules.Remove(Day()));
        Assert.AreEqual("语文", cache.Read(Monday).Next?.Subject);
        Assert.AreEqual("体育", cache.Read(Monday.AddDays(1)).Next?.Subject);
        Assert.AreEqual(4, changes);
        Assert.AreEqual(0, saved);
    }

    [TestMethod]
    public void SubjectRenameKeepsLessonMetadataAndOnlyUpdatesTheActiveProfile()
    {
        using var f = new Fixture();
        var otherPath = Path.Combine(f.Directory.FullName, "八年级课表.yaml");
        var other = new ScheduleService(() => f.Settings, f.Overrides, () => f.Today);
        other.Load(otherPath);
        other.AddSubject("体育");
        other.TemporarySchedules.Apply(SportsDay());
        f.Schedule.AddCourse(1, "even", new() { Subject = "体育", StartTime = "09:00", EndTime = "09:45" });
        f.Schedule.TemporarySchedules.Apply(SportsDay() with
        {
            TimeLayout = [new(ClassTimeKind.Action, "体育", TimeSpan.FromHours(10), TimeSpan.FromHours(10))]
        });
        var subject = f.Schedule.Profile.FindSubject("体育")!;
        using var cache = new DayScheduleCache(f.Time, f.Schedule);
        Assert.AreEqual("体育", cache.Read(Monday).Next?.Subject);
        var saved = 0;
        f.Schedule.Saved += () => saved++;
        Assert.IsTrue(f.Schedule.TryUpdateSubject(subject, " 体育与健康 ", "体", "陈老师", true, out var error), error);
        Assert.AreEqual(1, saved);
        Assert.AreEqual("体育与健康", cache.Read(Monday).Next?.Subject);
        var restarted = f.Open();
        Assert.IsNull(restarted.Profile.FindSubject("体育"));
        Assert.IsTrue(restarted.Profile.FindSubject("体育与健康")!.IsOutDoor);
        Assert.AreEqual("体育与健康", restarted.Profile.GetCourses(1, "even").Last().Subject);
        Assert.AreEqual("陈老师", f.Time.GetTodaySummary(restarted.Profile, Monday).Next?.Teacher);
        Assert.AreEqual("体育", restarted.Profile.TemporarySchedules[Day()].TimeLayout!.Value[0].Name);
        other.Reload();
        Assert.AreEqual("体育", other.Profile.TemporarySchedules[Day()].Lessons.Single().Subject);
        Assert.IsNotNull(other.Profile.FindSubject("体育"));
    }

    [TestMethod]
    public void SubjectRenameRejectsDuplicatesAndRestoresTemporaryFileWhenBaseSaveFails()
    {
        using var f = new Fixture();
        f.Schedule.AddSubject("信息科技");
        var subject = f.Schedule.Profile.FindSubject("体育")!;
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        var path = Directory.GetFiles(f.Overrides).Single();
        var original = File.ReadAllText(path);
        Assert.IsFalse(f.Schedule.TryUpdateSubject(subject, " 信息科技 ", null, null, true, out _));
        Assert.AreEqual(original, File.ReadAllText(path));
        using (File.Open(f.FileName, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            if (!OperatingSystem.IsWindows()) return;
            Assert.IsFalse(f.Schedule.TryUpdateSubject(subject, "体育与健康", null, null, true, out var error));
            Assert.IsNotNull(error);
        }
        Assert.AreEqual("体育", subject.Name);
        Assert.AreEqual("体育", f.Schedule.Profile.TemporarySchedules[Day()].Lessons.Single().Subject);
        Assert.AreEqual(original, File.ReadAllText(path));
        Assert.IsNotNull(f.Open().Profile.FindSubject("体育"));
    }

    [TestMethod]
    public void AnEmptyProfileRecognizesOnlyItsExplicitSchoolDays()
    {
        using var f = new Fixture();
        CsesCodec.Save(new ScheduleProfile(), f.FileName);
        f.Schedule.Reload();
        var profile = f.Schedule.Profile;
        Assert.IsFalse(profile.IsTimetableConfigured(Day()));
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        Assert.IsTrue(profile.IsTimetableConfigured(Day()));
        Assert.AreEqual("体育", f.Time.GetTodaySummary(profile, Monday).Next?.Subject);
        Assert.IsFalse(profile.IsTimetableConfigured(Day(1)));
        f.Schedule.TemporarySchedules.Apply(new(Day(), "停课", []));
        Assert.IsTrue(profile.IsTimetableConfigured(Day()));
        Assert.IsTrue(f.Time.GetTodaySummary(profile, Monday).IsEmpty);
        f.Schedule.TemporarySchedules.Remove(Day());
        Assert.IsFalse(profile.IsTimetableConfigured(Day()));
    }

    [TestMethod]
    public void RestartExportAndSwitchKeepTheSourceAndOtherProfilesUntouched()
    {
        using var f = new Fixture();
        var original = File.ReadAllBytes(f.FileName);
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        var restart = f.Open();
        Assert.IsNull(restart.TemporarySchedules.LoadError);
        Assert.AreEqual("体育", f.Time.GetTodaySummary(restart.Profile, Monday).Next?.Subject);
        Assert.AreEqual("眼保健操", restart.Profile.ImportedTimeLayout!.Times[1].Name);
        Assert.AreEqual(ClassTimeKind.Break, restart.Profile.ImportedTimeLayout.Times[0].Kind);
        var copy = Path.Combine(f.Directory.FullName, "交换课表.yaml");
        restart.ExportTo(copy);
        Assert.IsFalse(File.ReadAllText(copy).Contains("校运动会", StringComparison.Ordinal));
        Assert.IsFalse(CsesCodec.SerializeWorkingProfile(restart.Profile).Contains("校运动会", StringComparison.Ordinal));
        restart.ImportFrom(copy);
        Assert.IsEmpty(restart.Profile.TemporarySchedules);
        Assert.AreEqual("语文", f.Time.GetTodaySummary(restart.Profile, Monday).Next?.Subject);
        restart.ImportFrom(f.FileName);
        Assert.AreEqual("体育", f.Time.GetTodaySummary(restart.Profile, Monday).Next?.Subject);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(f.FileName));
        f.Today = Day(1);
        Assert.IsEmpty(f.Open().Profile.TemporarySchedules);
    }

    [TestMethod]
    public void InvalidPlansNeverReplaceTheLastWorkingCopy()
    {
        using var f = new Fixture();
        f.Schedule.TemporarySchedules.Apply(SportsDay());
        var file = Directory.GetFiles(f.Overrides).Single();
        var before = File.ReadAllBytes(file);
        var overlapping = SportsDay() with { Lessons = [new("体育", TimeSpan.FromHours(8), TimeSpan.FromHours(9)), new("班会", TimeSpan.FromMinutes(525), TimeSpan.FromMinutes(570))] };
        Assert.ThrowsExactly<ArgumentException>(() => f.Schedule.TemporarySchedules.Apply(overlapping));
        Assert.ThrowsExactly<ArgumentException>(() => f.Schedule.TemporarySchedules.Apply(SportsDay(-2)));
        Assert.ThrowsExactly<ArgumentException>(() => f.Schedule.TemporarySchedules.Apply(SportsDay() with { Lessons = [new(" ", TimeSpan.Zero, TimeSpan.FromHours(2))] }));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(file));
        File.WriteAllText(file, "{损坏");
        f.Schedule.Reload();
        Assert.IsNotNull(f.Schedule.TemporarySchedules.LoadError);
        Assert.AreEqual("语文", f.Time.GetTodaySummary(f.Schedule.Profile, Monday).Next?.Subject);
        Assert.ThrowsExactly<InvalidOperationException>(() => f.Schedule.TemporarySchedules.Apply(SportsDay()));
        Assert.AreEqual("{损坏", File.ReadAllText(file));
    }

    [TestMethod]
    public void CannotOpenMalformedYamlOrPublishAnOverrideThatFailedToWrite()
    {
        using var f = new Fixture();
        var original = f.Schedule.Profile;
        var broken = Path.Combine(f.Directory.FullName, "损坏.yaml");
        File.WriteAllText(broken, "schedules: [ {\n");
        Assert.Throws<YamlDotNet.Core.YamlException>(() => f.Schedule.ImportFrom(broken));
        Assert.AreSame(original, f.Schedule.Profile);
        Assert.AreEqual(f.FileName, f.Schedule.FilePath);
        var changes = 0;
        f.Schedule.ProfileChanged += () => changes++;
        File.WriteAllText(f.Overrides, "占用调课存储路径");
        Assert.Throws<IOException>(() => f.Schedule.TemporarySchedules.Apply(SportsDay()));
        Assert.IsEmpty(original.TemporarySchedules);
        Assert.AreEqual(0, changes);
        Assert.AreEqual("语文", f.Time.GetTodaySummary(original, Monday).Next?.Subject);
    }

    [TestMethod]
    public void TwoExternalFilesWithTheSameNameDoNotShareTemporaryLessons()
    {
        using var f = new Fixture();
        foreach (var grade in new[] { "初一", "初二" })
        {
            var folder = Directory.CreateDirectory(Path.Combine(f.Directory.FullName, grade));
            var path = Path.Combine(folder.FullName, "课表.yaml");
            File.Copy(f.FileName, path);
            f.Schedule.ImportFrom(path);
            Assert.IsEmpty(f.Schedule.Profile.TemporarySchedules);
            f.Schedule.TemporarySchedules.Apply(SportsDay() with { Name = grade + "活动日" });
        }
        f.Schedule.ImportFrom(Path.Combine(f.Directory.FullName, "初一", "课表.yaml"));
        Assert.AreEqual("初一活动日", f.Schedule.Profile.TemporarySchedules[Day()].Name);
        Assert.IsTrue(f.Schedule.TemporarySchedules.Remove(Day()));
        f.Schedule.ImportFrom(Path.Combine(f.Directory.FullName, "初二", "课表.yaml"));
        Assert.AreEqual("初二活动日", f.Schedule.Profile.TemporarySchedules[Day()].Name);
    }
}
