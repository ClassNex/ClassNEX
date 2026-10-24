using ClassNex.Models;

namespace ClassNex.Services;





public interface ITimeLayoutService
{
    
    TimeLayout Layout { get; }

    event Action? Changed;

    
    void EnsureFromProfile(ScheduleProfile profile);

    
    void Add(ClassTime time);

    ClassTime? InsertNext(ClassTime? after, ClassTimeKind kind, out string notice);

    ClassTime? Duplicate(ClassTime source, out string notice);

    
    void Remove(ClassTime time);

    
    void Update(ClassTime original, ClassTime updated);

    
    void Move(ClassTime time, int delta);

    
    void Clear();

    void Save();
}

public sealed class TimeLayoutService : ITimeLayoutService
{
    private readonly Func<AppSettings> _settings;
    private readonly IScheduleService _schedule;

    public TimeLayoutService(Func<AppSettings> settingsAccessor, IScheduleService schedule)
    {
        _settings = settingsAccessor;
        _schedule = schedule;
    }

    public TimeLayout Layout => _settings().TimeLayout;

    public event Action? Changed;

    public void EnsureFromProfile(ScheduleProfile profile)
    {
        if (!Layout.IsEmpty || profile.Schedules.Count == 0)
            return;

        _settings().TimeLayout = TimeLayout.FromProfile(profile);
        Save();
    }

    public void Add(ClassTime time)
    {
        Layout.Times.Add(time);
        Sort();
        Save();
    }

    public ClassTime? InsertNext(ClassTime? after, ClassTimeKind kind, out string notice)
    {
        notice = "";
        var settings = _settings();
        var duration = TimeSpan.FromMinutes(kind == ClassTimeKind.Class
            ? settings.DefaultOnClassTimePointMinutes
            : settings.DefaultBreakingTimePointMinutes);
        var baseTime = Resolve(after?.End) ?? new TimeSpan(8, 0, 0);

        if (after is not null && Layout.Times.IndexOf(after) is var index && index >= 0)
        {
            var next = index + 1 < Layout.Times.Count ? Layout.Times[index + 1] : null;
            if (next is not null && Resolve(next.Start) is { } nextStart)
            {
                if (nextStart <= baseTime)
                {
                    if (index != 0)
                    {
                        notice = "没有合适的位置来插入新的时间点。";
                        return null;
                    }

                    var ownStart = Resolve(after.Start) ?? baseTime;
                    baseTime = ownStart - duration;
                    notice = "已向前插入了新的时间点。";
                }

                if (nextStart < baseTime + duration)
                {
                    duration = nextStart - baseTime;
                    notice = notice.Length > 0 ? notice + " " : "";
                    notice += "没有足够的空间完全插入该时间点，已缩短时间点长度。";
                }
            }
        }

        var time = new ClassTime
        {
            Name = kind == ClassTimeKind.Class
                ? $"第{Layout.Times.Count(t => t.Kind == ClassTimeKind.Class) + 1}节"
                : "课间",
            Start = Format(baseTime),
            End = Format(baseTime + duration),
            Kind = kind,
        };

        Add(time);
        return time;
    }

    public ClassTime? Duplicate(ClassTime source, out string notice)
    {
        notice = "";
        var start = Resolve(source.Start);
        var end = Resolve(source.End);
        if (start is null || end is null)
        {
            notice = "没有足够的空间来复制时间点。";
            return null;
        }

        var duration = end.Value - start.Value;
        var baseTime = end.Value;
        var copyEnd = baseTime + duration;
        var maxTime = new TimeSpan(23, 59, 59);

        if (baseTime >= maxTime)
        {
            notice = "没有足够的空间来复制时间点。";
            return null;
        }

        var index = Layout.Times.IndexOf(source);
        if (index >= 0 && index + 1 < Layout.Times.Count)
        {
            var next = Layout.Times[index + 1];
            if (Resolve(next.Start) is { } nextStart)
            {
                if (nextStart <= baseTime)
                {
                    if (index != 0)
                    {
                        notice = "没有合适的位置来复制时间点。";
                        return null;
                    }

                    baseTime = start.Value - duration;
                    copyEnd = start.Value;
                    notice = "已向前插入了新的时间点。";
                }

                if (nextStart < copyEnd)
                {
                    copyEnd = nextStart;
                    notice = notice.Length > 0 ? notice + " " : "";
                    notice += "没有足够的空间完全复制该时间点，已缩短时间点长度。";
                }
            }
        }

        copyEnd = TimeSpan.FromTicks(Math.Clamp(copyEnd.Ticks, TimeSpan.Zero.Ticks, maxTime.Ticks));
        if (copyEnd <= baseTime)
        {
            notice = "没有足够的空间来复制时间点。";
            return null;
        }

        var copy = new ClassTime
        {
            Name = source.Kind == ClassTimeKind.Class
                ? $"第{Layout.Times.Count(t => t.Kind == ClassTimeKind.Class) + 1}节"
                : source.Name,
            Start = Format(baseTime),
            End = Format(copyEnd),
            Kind = source.Kind,
        };

        Add(copy);
        return copy;
    }

    public void Remove(ClassTime time)
    {
        Layout.Times.Remove(time);
        Save();
    }

    public void Update(ClassTime original, ClassTime updated)
    {
        var oldStart = original.Start;
        var oldEnd = original.End;

        original.Name = updated.Name;
        original.Start = updated.Start;
        original.End = updated.End;
        original.Kind = updated.Kind;

        Sort();

        var moved = !ClassTime.SameTime(oldStart, updated.Start) || !ClassTime.SameTime(oldEnd, updated.End);
        if (moved)
            PropagateToCourses(oldStart, oldEnd, updated.Start, updated.End);

        Save();
    }

    public void Move(ClassTime time, int delta)
    {
        var index = Layout.Times.IndexOf(time);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Layout.Times.Count)
            return;

        
        var other = Layout.Times[target];
        (time.Start, time.End, other.Start, other.End) =
            (other.Start, other.End, time.Start, time.End);

        Sort();
        Save();
    }

    public void Clear()
    {
        Layout.Times.Clear();
        Save();
    }

    public void Save()
    {
        AppServices.SaveSettings();
        Changed?.Invoke();
    }

    
    private void PropagateToCourses(string oldStart, string oldEnd, string newStart, string newEnd)
    {
        var changed = false;

        foreach (var schedule in _schedule.Profile.Schedules)
        {
            foreach (var course in schedule.Classes)
            {
                var courseStart = ClassTime.ToShortTime(course.StartTime);
                var courseEnd = ClassTime.ToShortTime(course.EndTime);

                if (!ClassTime.SameTime(courseStart, oldStart) || !ClassTime.SameTime(courseEnd, oldEnd))
                    continue;

                course.StartTime = ClassTime.ToCsesTime(newStart);
                course.EndTime = ClassTime.ToCsesTime(newEnd);
                changed = true;
            }
        }

        if (changed)
            _schedule.Save();
    }

    private void Sort() =>
        Layout.Times.Sort((a, b) => ParseKey(a).CompareTo(ParseKey(b)));

    private static TimeSpan ParseKey(ClassTime time) =>
        TimeSpan.TryParse(time.Start, out var t) ? t : TimeSpan.MaxValue;

    private static TimeSpan? Resolve(string? text) =>
        TimeSpan.TryParse(text, out var t) ? t : null;

    private static string Format(TimeSpan t) => t.ToString(@"hh\:mm");
}
