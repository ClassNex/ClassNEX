using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>按本地日期保存单日调课，不更改基础档案，也不触发基础课表的上传。</summary>
public sealed class TemporaryScheduleStore
{
    private readonly string? _directory;
    private readonly Func<DateOnly> _today;
    private readonly Action _changed;
    private readonly object _gate = new();
    private ScheduleProfile _profile;
    private string? _path;

    internal TemporaryScheduleStore(ScheduleProfile profile, string? directory, Func<DateOnly> today, Action changed)
        => (_profile, _directory, _today, _changed) = (profile, directory, today, changed);

    /// <summary>无法安全读取已有调课时的原因；重新加载成功前禁止覆盖原文件。</summary>
    public string? LoadError { get; private set; }

    internal void Bind(ScheduleProfile profile, string baseFile)
    {
        lock (_gate)
        {
            _profile = profile;
            _path = null;
            LoadError = null;
            if (string.IsNullOrWhiteSpace(baseFile)) return;
            try
            {
                _path = StoragePath(baseFile);
                using var file = File.OpenRead(_path);
                var saved = JsonSerializer.Deserialize<TemporarySchedule[]>(file)
                    ?? throw new InvalidDataException("调课记录为空。");
                var entries = ImmutableSortedDictionary.CreateBuilder<DateOnly, TemporarySchedule>();
                var today = _today();
                foreach (var plan in saved)
                {
                    if (plan is null) throw new InvalidDataException("调课记录缺失。");
                    if (plan.Date < today) continue;
                    if (entries.ContainsKey(plan.Date)) throw new InvalidDataException("同一天存在两份调课记录。");
                    entries.Add(plan.Date, Normalize(plan));
                }
                _profile.TemporarySchedules = entries.ToImmutable();
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
            {
                LoadError = $"本地调课未载入，已保留原文件。{e.Message}";
            }
        }
    }

    /// <summary>设置所选日期的唯一临时安排；过去的日期、重叠时段和无效课程均不写入。</summary>
    public void Apply(TemporarySchedule plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        lock (_gate)
        {
            var today = _today();
            if (plan.Date < today) throw new ArgumentException("请选择今天或之后的日期。");
            var normalized = Normalize(plan);
            Commit(_profile.TemporarySchedules.SetItem(plan.Date, normalized), today);
        }
        _changed();
    }

    /// <summary>提前撤下某天的临时安排；其余日期和基础档案保持原样。</summary>
    public bool Remove(DateOnly date)
    {
        lock (_gate)
        {
            if (!_profile.TemporarySchedules.ContainsKey(date)) return false;
            Commit(_profile.TemporarySchedules.Remove(date), _today());
        }
        _changed();
        return true;
    }

    private void Commit(ImmutableSortedDictionary<DateOnly, TemporarySchedule> entries, DateOnly today)
    {
        if (LoadError is not null) throw new InvalidOperationException(LoadError);
        entries = entries.RemoveRange(entries.Keys.TakeWhile(date => date < today));
        if (_path is not null) AtomicFile.Write(_path, JsonSerializer.Serialize(entries.Values));
        _profile.TemporarySchedules = entries;
        _profile.Invalidate();
    }

    internal void SaveRenamedSubject(string from, string to, Action saveBase)
    {
        lock (_gate)
        {
            if (LoadError is not null) throw new InvalidOperationException(LoadError);
            var original = _profile.TemporarySchedules;
            var updated = original;
            foreach (var (date, plan) in original)
            {
                if (!plan.Lessons.Any(l => l.Subject == from)) continue;
                updated = updated.SetItem(date, plan with
                {
                    Lessons = plan.Lessons.Select(l => l.Subject == from ? l with { Subject = to } : l).ToImmutableArray()
                });
            }
            if (!ReferenceEquals(original, updated) && _path is { } path)
            {
                var previous = File.ReadAllText(path);
                AtomicFile.Write(path, JsonSerializer.Serialize(updated.Values));
                try { saveBase(); }
                catch
                {
                    try { AtomicFile.Write(path, previous); }
                    catch (Exception restoreError) when (restoreError is IOException or UnauthorizedAccessException)
                    {
                        LoadError = "基础课表未保存，且无法还原本地调课文件。请检查文件权限后重新加载。";
                        throw new IOException(LoadError, restoreError);
                    }
                    throw;
                }
            }
            else saveBase();
            _profile.TemporarySchedules = updated;
        }
    }

    private static TemporarySchedule Normalize(TemporarySchedule plan)
    {
        if (string.IsNullOrWhiteSpace(plan.Name)) throw new ArgumentException("请填写这份临时课表的名称。");
        if (plan.Lessons.IsDefault) throw new ArgumentException("调课记录缺少课程列表。");
        foreach (var lesson in plan.Lessons)
        {
            if (lesson is null || string.IsNullOrWhiteSpace(lesson.Subject)) throw new ArgumentException("每节课都需要填写科目。");
            if (lesson.Start < TimeSpan.Zero || lesson.End > TimeSpan.FromDays(1) || lesson.End <= lesson.Start)
                throw new ArgumentException($"“{lesson.Subject}”的结束时间必须晚于开始时间，且不能跨天。");
        }
        var sorted = plan.Lessons.OrderBy(lesson => lesson.Start).Select(lesson => lesson with { Subject = lesson.Subject.Trim() }).ToImmutableArray();
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].Start < sorted[i - 1].End)
                throw new ArgumentException($"“{sorted[i - 1].Subject}”和“{sorted[i].Subject}”的时间重叠。");
        return plan with { Name = plan.Name.Trim(), Lessons = sorted, TimeLayout = NormalizeLayout(plan.TimeLayout, sorted) };
    }

    private static ImmutableArray<TemporaryTimeItem>? NormalizeLayout(ImmutableArray<TemporaryTimeItem>? layout, ImmutableArray<TemporaryLesson> lessons)
    {
        if (layout is not { } items) return null;
        if (items.IsDefault) throw new ArgumentException("缺少当天作息列表。");
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name)) throw new ArgumentException("请为每项当天作息填写名称。");
            if (item.Kind is not (ClassTimeKind.Break or ClassTimeKind.Action or ClassTimeKind.Separator))
                throw new ArgumentException("课程起止请在当天课程中编辑。");
            if (item.Start < TimeSpan.Zero || item.Start >= TimeSpan.FromDays(1) || item.End > TimeSpan.FromDays(1) || item.End < item.Start ||
                (item.Kind == ClassTimeKind.Break && item.Start == item.End) || (item.Kind == ClassTimeKind.Separator && item.Start != item.End))
                throw new ArgumentException($"“{item.Name}”的作息时间无效。课间须有时长，分隔点须为当天的单一时刻，不能跨天。");
            if (item.End > item.Start && lessons.FirstOrDefault(l => l.Start < item.End && item.Start < l.End) is { } overlap)
                throw new ArgumentException($"“{item.Name}”和课程“{overlap.Subject}”的时间重叠。");
        }
        var sorted = items.OrderBy(i => i.Start).Select(i => i with { Name = i.Name.Trim() }).ToImmutableArray();
        foreach (var kind in new[] { ClassTimeKind.Break, ClassTimeKind.Action })
        {
            TemporaryTimeItem? previous = null;
            foreach (var item in sorted.Where(i => i.Kind == kind && i.End > i.Start))
            {
                if (previous is not null && item.Start < previous.End) throw new ArgumentException($"“{previous.Name}”和“{item.Name}”的时间重叠。");
                previous = item;
            }
        }
        return sorted;
    }

    private string StoragePath(string baseFile)
    {
        var file = Path.GetFullPath(baseFile);
        if (_directory is null) return file + ".classnex-days.json";
        var directory = Path.GetFullPath(_directory);
        var relative = Path.GetRelativePath(Path.GetDirectoryName(directory)!, file);
        var key = Path.IsPathRooted(relative) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? "file:" + file : "data:" + relative;
        if (OperatingSystem.IsWindows()) key = key.ToUpperInvariant();
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))) + ".json");
    }
}
