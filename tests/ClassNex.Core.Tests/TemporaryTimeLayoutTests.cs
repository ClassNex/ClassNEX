using System.Collections.Immutable;
using ClassNex.Models;
using ClassNex.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ClassNex.Core.Tests;

[TestClass]
public sealed class TemporaryTimeLayoutTests
{
    private static readonly DateOnly Friday = new(2026, 11, 27);
    private static TimeSpan At(int h, int m = 0) => TimeSpan.FromMinutes(h * 60 + m);
    private static TemporarySchedule Plan => new(Friday, "运动会", [new("体育", At(8), At(9, 20)), new("班会", At(9, 30), At(10, 15))]);
    private static readonly TemporaryTimeItem Water = new(ClassTimeKind.Break, "补水休息", At(9, 20), At(9, 30));
    private static readonly TemporaryTimeItem Awards = new(ClassTimeKind.Action, "颁奖", At(9, 23), At(9, 27));
    private static readonly TemporaryTimeItem Flag = new(ClassTimeKind.Separator, "集合点", At(9, 27), At(9, 27));

    [TestMethod]
    public void RestartKeepsDateLayoutLocalAndRestoringItDoesNotWithdrawLessons()
    {
        var folder = Directory.CreateTempSubdirectory("classnex-day-layout-");
        try
        {
            var source = Path.Combine(folder.FullName, "weekly.yaml");
            var profile = new ScheduleProfile();
            profile.GetOrCreateSchedule(5, "all").Classes.Add(new() { Subject = "语文", StartTime = "08:00", EndTime = "08:45" });
            CsesCodec.Save(profile, source);
            var original = File.ReadAllBytes(source);
            var baseLayout = new TimeLayout { Times = [new() { Kind = ClassTimeKind.Break, Name = "基础大课间", Start = "08:45", End = "09:00" }] };
            var settings = new AppSettings { TimeLayout = baseLayout };
            var time = new TimeService(() => settings);
            ScheduleService Open() { var s = new ScheduleService(() => settings, folder.FullName, () => Friday); s.Load(source); return s; }
            var s = Open();
            var uploads = 0;
            s.Saved += () => uploads++;
            s.TemporarySchedules.Apply(Plan);
            var path = Directory.GetFiles(folder.FullName, "*.json").Single();
            Assert.DoesNotContain("TimeLayout", File.ReadAllText(path));
            s = Open();
            Assert.IsNull(s.Profile.TemporarySchedules[Friday].TimeLayout);
            using var day = new DayScheduleCache(time, s);
            var start = Friday.ToDateTime(TimeOnly.MinValue);
            s.Saved += () => uploads++;
            s.TemporarySchedules.Apply(Plan with { TimeLayout = [Flag, Water, Awards] });
            Assert.AreEqual("补水休息", day.Read(start).GetActiveTimeItem(At(9, 20))?.Name);
            Assert.AreEqual("颁奖", day.Read(start).GetActiveTimeItem(At(9, 23))?.Name);
            Assert.AreEqual("补水休息", day.Read(start).GetActiveTimeItem(At(9, 27))?.Name);
            Assert.IsNull(day.Read(start).GetActiveTimeItem(At(9, 30)));
            Assert.AreEqual("班会", day.Read(start.AddMinutes(570)).Current?.Subject);
            Assert.IsNull(day.Read(start.AddDays(1)).IndependentTimeLayout);
            Assert.AreEqual("基础大课间", day.Read(start.AddDays(1)).ResolveTimeLayout(baseLayout).Single().Name);
            Assert.AreEqual("颁奖", time.GetTodaySummary(Open().Profile, start).GetActiveTimeItem(At(9, 25))?.Name);
            Assert.DoesNotContain("补水休息", CsesCodec.SerializeWorkingProfile(s.Profile));
            s.TemporarySchedules.Apply(Plan with { TimeLayout = [] });
            Assert.IsEmpty(time.GetTodaySummary(Open().Profile, start).ResolveTimeLayout(baseLayout));
            s.TemporarySchedules.Apply(Plan);
            Assert.AreEqual("基础大课间", day.Read(start).ResolveTimeLayout(baseLayout).Single().Name);
            Assert.AreEqual("体育", day.Read(start.AddHours(8)).Current?.Subject);
            Assert.AreEqual(0, uploads);
            CollectionAssert.AreEqual(original, File.ReadAllBytes(source));
            Assert.AreEqual("基础大课间", settings.TimeLayout.Times.Single().Name);
        }
        finally { foreach (var file in folder.EnumerateFiles()) file.Delete(); folder.Delete(); }
    }

    [TestMethod]
    public void InvalidOverlapsAndNextMidnightMarkersLeaveTheExistingDayUntouched()
    {
        var settings = new AppSettings();
        var s = new ScheduleService(() => settings, null, () => Friday);
        s.TemporarySchedules.Apply(Plan with { TimeLayout = [Water, Awards, Flag] });
        ImmutableArray<TemporaryTimeItem>[] invalid = [[Water with { Start = At(9, 19) }], [Water, Water], [Awards, Awards],
            [Water with { End = Water.Start }], [Flag with { End = At(10) }], [Flag with { Start = At(24), End = At(24) }],
            [Water with { Start = At(23, 59), End = At(24, 1) }], [Water with { Kind = ClassTimeKind.Class }]];
        foreach (var items in invalid)
        {
            Assert.ThrowsExactly<ArgumentException>(() => s.TemporarySchedules.Apply(Plan with { TimeLayout = items }));
            Assert.HasCount(3, s.Profile.TemporarySchedules[Friday].TimeLayout!.Value);
        }
        var late = Water with { Start = At(23, 59), End = At(24) };
        s.TemporarySchedules.Apply(Plan with { TimeLayout = [late, Flag with { Start = TimeSpan.Zero, End = TimeSpan.Zero }] });
        var time = new TimeService(() => settings);
        var start = Friday.ToDateTime(TimeOnly.MinValue);
        Assert.IsTrue(TemporaryTimeItem.TryClock("24:00", out var midnight));
        Assert.AreEqual(At(24), midnight);
        Assert.IsFalse(TemporaryTimeItem.TryClock("24:01", out _));
        Assert.AreEqual(late, time.GetTodaySummary(s.Profile, start).GetActiveTimeItem(At(24) - TimeSpan.FromTicks(1)));
        Assert.IsNull(time.GetTodaySummary(s.Profile, start.AddDays(1)).GetActiveTimeItem(TimeSpan.Zero));
    }
}
