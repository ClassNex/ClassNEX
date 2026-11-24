using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>编辑作息时间表并维护关联课程的起止时间。</summary>
public interface ITimeLayoutService
{
    /// <summary>当前设置持有的可编辑时间表。</summary>
    TimeLayout Layout { get; }

    /// <summary>本次时间表更改已交给设置存储时触发。</summary>
    event Action? Changed;

    /// <summary>仅在时间表为空且档案有课程计划时从档案派生布局。</summary>
    void EnsureFromProfile(ScheduleProfile profile);

    /// <summary>加入项目，并按起始时间重排后保存。</summary>
    void Add(ClassTime time);

    /// <summary>尝试在指定项目后插入；间隙不足可缩短时段，失败返回 null 并给出原因。</summary>
    ClassTime? InsertNext(ClassTime? after, ClassTimeKind kind, out string notice);

    /// <summary>复制项目到可用时间位置；冲突时通过 notice 提供说明。</summary>
    ClassTime? Duplicate(ClassTime source, out string notice);

    /// <summary>移除指定时间项目并保存。</summary>
    void Remove(ClassTime time);

    /// <summary>修改名称、类别及时间，并传播对应时段的起止时间变化到档案课程。</summary>
    void Update(ClassTime original, ClassTime updated);

    /// <summary>与相对位置上的项目交换起止时间，再按时间排序。</summary>
    void Move(ClassTime time, int delta);

    /// <summary>清空当前时间表，不删除档案中的科目或授课记录。</summary>
    void Clear();

    /// <summary>写回当前设置并通知时间表订阅者。</summary>
    void Save();
}

/// <summary>基于当前设置的作息编辑器，在课时调整时同步匹配的 CSES 记录。</summary>
public sealed class TimeLayoutService : ITimeLayoutService
{
    private readonly Func<AppSettings> _settings;
    private readonly IScheduleService _schedule;
    private readonly Action _saveSettings;

    /// <summary>连接当前设置、活动档案及设置保存回调。</summary>
    public TimeLayoutService(
        Func<AppSettings> settingsAccessor,
        IScheduleService schedule,
        Action saveSettings)
    {
        _settings = settingsAccessor;
        _schedule = schedule;
        _saveSettings = saveSettings;
    }

    /// <inheritdoc />
    public TimeLayout Layout => _settings().TimeLayout;

    /// <inheritdoc />
    public event Action? Changed;

    /// <inheritdoc />
    public void EnsureFromProfile(ScheduleProfile profile)
    {
        if (!Layout.IsEmpty || profile.Schedules.Count == 0 || ReferenceEquals(Layout, profile.ImportedTimeLayout))
            return;

        _settings().TimeLayout = profile.ImportedTimeLayout ?? TimeLayout.FromProfile(profile);
        _saveSettings();
        Changed?.Invoke();
    }

    /// <inheritdoc />
    public void Add(ClassTime time)
    {
        Layout.Times.Add(time);
        Sort();
        Save();
    }

    /// <inheritdoc />
    public ClassTime? InsertNext(ClassTime? after, ClassTimeKind kind, out string notice)
    {
        notice = "";
        var settings = _settings();
        var isPeriod = kind is ClassTimeKind.Class or ClassTimeKind.Break;
        var duration = kind switch
        {
            ClassTimeKind.Class => TimeSpan.FromMinutes(settings.DefaultOnClassTimePointMinutes),
            ClassTimeKind.Break => TimeSpan.FromMinutes(settings.DefaultBreakingTimePointMinutes),
            _ => TimeSpan.Zero,
        };
        var baseTime = Resolve(isPeriod ? after?.End : after?.Start) ?? new TimeSpan(8, 0, 0);

        if (kind == ClassTimeKind.Separator
            && Layout.Times.Any(t => t.Kind == ClassTimeKind.Separator
                && ClassTime.SameTime(t.Start, Format(baseTime))))
        {
            notice = "这里已经存在一条分割线。";
            return null;
        }

        if (kind == ClassTimeKind.Action
            && Layout.Times.Any(t => t.Kind == ClassTimeKind.Action
                && ClassTime.SameTime(t.Start, Format(baseTime))))
        {
            notice = "这里已经存在一个行动。";
            return null;
        }

        if (isPeriod && after is not null && Layout.Times.IndexOf(after) is var index && index >= 0)
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
            Name = kind switch
            {
                ClassTimeKind.Class => $"第{Layout.Times.Count(t => t.Kind == ClassTimeKind.Class) + 1}节",
                ClassTimeKind.Break => "课间",
                ClassTimeKind.Separator => "分割线",
                _ => "行动",
            },
            Start = Format(baseTime),
            End = Format(baseTime + duration),
            Kind = kind,
        };

        Add(time);
        return time;
    }

    /// <inheritdoc />
    public ClassTime? Duplicate(ClassTime source, out string notice)
    {
        notice = "";
        if (source.Kind == ClassTimeKind.Separator
            && Layout.Times.Any(t => t.Kind == ClassTimeKind.Separator
                && ClassTime.SameTime(t.Start, source.Start)))
        {
            notice = "这里已经存在一条分割线。";
            return null;
        }

        if (source.Kind == ClassTimeKind.Action
            && Layout.Times.Any(t => t.Kind == ClassTimeKind.Action
                && ClassTime.SameTime(t.Start, source.Start)))
        {
            notice = "这里已经存在一个行动。";
            return null;
        }

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

    /// <inheritdoc />
    public void Remove(ClassTime time)
    {
        Layout.Times.Remove(time);
        Save();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public void Clear()
    {
        Layout.Times.Clear();
        Save();
    }

    /// <inheritdoc />
    public void Save()
    {
        if (_schedule.Profile.ImportedTimeLayout != null && !string.IsNullOrEmpty(_schedule.FilePath))
        {
            _schedule.Profile.ImportedTimeLayout = Layout;
            _schedule.Save();
        }
        _saveSettings();
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
