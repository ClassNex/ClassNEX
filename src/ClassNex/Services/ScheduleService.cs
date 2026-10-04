using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 课表服务（白皮书 Core/Services/IScheduleService.cs）：
/// 课表增删改查、CSES 导入导出。
/// </summary>
public interface IScheduleService
{
    /// <summary>当前课表档案。</summary>
    ScheduleProfile Profile { get; }

    /// <summary>当前课表文件路径。</summary>
    string FilePath { get; }

    /// <summary>文件名（界面显示用）。</summary>
    string FileName { get; }

    /// <summary>课表数据发生变化（新增/删除/导入/重载）。</summary>
    event Action? ProfileChanged;

    /// <summary>从文件加载课表。</summary>
    void Load(string path);

    /// <summary>重新加载当前文件。</summary>
    void Reload();

    /// <summary>把当前档案写回文件。</summary>
    void Save();

    // ---------- 课程 ----------

    /// <summary>新增一节课。</summary>
    void AddCourse(int enableDay, string weeks, Course course);

    /// <summary>删除一节课。</summary>
    void RemoveCourse(int enableDay, string weeks, Course course);

    /// <summary>修改一节课（原地替换）。</summary>
    void UpdateCourse(int enableDay, string weeks, Course original, Course updated);

    // ---------- 科目 ----------

    Subject AddSubject(string name);

    void RemoveSubject(Subject subject);

    // ---------- 导入导出 ----------

    /// <summary>导入 CSES 文件并切换为当前课表。</summary>
    void ImportFrom(string path);

    /// <summary>导出为 CSES 文件。</summary>
    void ExportTo(string path);
}

public sealed class ScheduleService : IScheduleService
{
    private readonly Func<AppSettings> _settings;
    private ScheduleProfile _profile = new();

    public ScheduleService(Func<AppSettings> settingsAccessor) => _settings = settingsAccessor;

    public ScheduleProfile Profile => _profile;

    public string FilePath { get; private set; } = "";

    public string FileName => string.IsNullOrEmpty(FilePath) ? "（无）" : Path.GetFileName(FilePath);

    public event Action? ProfileChanged;

    public void Load(string path)
    {
        FilePath = path;
        try
        {
            _profile = File.Exists(path) ? CsesCodec.Load(path) : new ScheduleProfile();
        }
        catch
        {
            _profile = new ScheduleProfile();
        }

        Raise();
    }

    public void Reload() => Load(FilePath);

    public void Save()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
            return;

        try
        {
            CsesCodec.Save(_profile, FilePath);
        }
        catch
        {
            // 忽略写入失败
        }

        Raise();
    }

    public void AddCourse(int enableDay, string weeks, Course course)
    {
        _profile.GetOrCreateSchedule(enableDay, weeks).Classes.Add(course);
        Save();
    }

    public void RemoveCourse(int enableDay, string weeks, Course course)
    {
        Find(enableDay, weeks)?.Classes.Remove(course);
        Save();
    }

    public void UpdateCourse(int enableDay, string weeks, Course original, Course updated)
    {
        var schedule = Find(enableDay, weeks);
        if (schedule is null)
            return;

        var index = schedule.Classes.IndexOf(original);
        if (index < 0)
            return;

        schedule.Classes[index] = updated;
        Save();
    }

    public Subject AddSubject(string name)
    {
        var subject = new Subject { Name = name };
        _profile.Subjects.Add(subject);
        Save();
        return subject;
    }

    public void RemoveSubject(Subject subject)
    {
        _profile.Subjects.Remove(subject);
        Save();
    }

    public void ImportFrom(string path)
    {
        _settings().TimetablePath = path;
        Load(path);
    }

    public void ExportTo(string path)
    {
        CsesCodec.Save(_profile, path);
    }

    private Schedule? Find(int enableDay, string weeks) =>
        _profile.Schedules.FirstOrDefault(s => s.EnableDay == enableDay && s.Weeks == weeks);

    private void Raise() => ProfileChanged?.Invoke();
}
