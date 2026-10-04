using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 时间表服务（白皮书 Models/TimeLayout.cs 的管理侧）：
/// 节次的增删改查，并把时间改动同步到课表中使用该时段的课程。
/// </summary>
public interface ITimeLayoutService
{
    /// <summary>当前时间表。</summary>
    TimeLayout Layout { get; }

    event Action? Changed;

    /// <summary>时间表为空时，从课表反推一份。</summary>
    void EnsureFromProfile(ScheduleProfile profile);

    /// <summary>新增节次（按开始时间自动排序）。</summary>
    void Add(ClassTime time);

    /// <summary>删除节次。</summary>
    void Remove(ClassTime time);

    /// <summary>修改节次（时间变化会同步到课表）。</summary>
    void Update(ClassTime original, ClassTime updated);

    /// <summary>上移 / 下移（delta = -1 / +1）。</summary>
    void Move(ClassTime time, int delta);

    /// <summary>清空。</summary>
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

        // 交换开始时间，保持排序语义
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

    /// <summary>把「旧时段」的课程整体平移到「新时段」。</summary>
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
}
