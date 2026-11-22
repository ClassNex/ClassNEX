using System.Diagnostics;
using ClassNex.Models;
using ClassNex.Services;

const int rounds = 30_000;
var monday = new DateTime(2026, 11, 16, 9, 0, 0);
var settings = new AppSettings { SingleWeekStartTime = monday.Date };
var time = new TimeService(() => settings);
var profile = new ScheduleProfile();
for (var i = 0; i < 12; i++) profile.Subjects.Add(new Subject { Name = $"课程{i}", Teacher = $"教师{i}" });
for (var day = 1; day <= 5; day++)
{
    var schedule = profile.GetOrCreateSchedule(day, "all");
    for (var i = 0; i < 12; i++)
    {
        var start = TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(i * 50));
        schedule.Classes.Add(new Course { Subject = profile.Subjects[i].Name, StartTime = start.ToString("c"), EndTime = start.Add(TimeSpan.FromMinutes(40)).ToString("c") });
    }
}

for (var i = 0; i < 12_000; i++) Read(i);
var timings = new double[5];
var allocations = new double[5];
var checksum = 0;
for (var pass = 0; pass < timings.Length; pass++)
{
    var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    for (var i = 0; i < rounds; i++) checksum += Read(i);
    timings[pass] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / rounds;
    allocations[pass] = (GC.GetAllocatedBytesForCurrentThread() - beforeBytes) / (double)rounds;
}
Array.Sort(timings);
Array.Sort(allocations);
Console.WriteLine(FormattableString.Invariant($"5 weekdays x 12 lessons; 5 x {rounds} GetTodaySummary calls; median {timings[2]:F3} us/call, {allocations[2]:F1} B/call; checksum={checksum}"));

int Read(int i)
{
    var snapshot = time.GetTodaySummary(profile, monday.AddDays(i % 5).AddSeconds(i % 3600));
    return snapshot.Slots.Count + (snapshot.Current is null ? 0 : 1) + (snapshot.Next is null ? 0 : 1);
}
