using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>维护一个活动档案并保存课程、科目编辑，供桌面和插件共用。</summary>
public interface IScheduleService
{
    /// <summary>当前可编辑的档案实例；直接编辑后须调用 Save 通知其他消费者。</summary>
    ScheduleProfile Profile { get; }

    /// <summary>当前档案的保存位置；内存档案可为空。</summary>
    string FilePath { get; }

    /// <summary>不含目录的档案名，供选择器和标题使用。</summary>
    string FileName { get; }

    /// <summary>活动档案被替换或编辑后触发，未必表示已成功写盘。</summary>
    event Action? ProfileChanged;

    /// <summary>仅在当前档案已成功写入文件时触发。</summary>
    event Action? Saved;

    /// <summary>切换活动路径并读取档案；文件丢失或不能解析时使用空档案。</summary>
    void Load(string path);

    /// <summary>从当前路径重新读取，替换内存中的档案。</summary>
    void Reload();

    /// <summary>让课表缓存失效，并将有文件路径的当前档案写回磁盘。</summary>
    void Save();

    /// <summary>将课程添加到指定星期和周次计划并保存；缺少该计划时创建。</summary>
    void AddCourse(int enableDay, string weeks, Course course);

    /// <summary>从匹配的计划中移除该课程实例。</summary>
    void RemoveCourse(int enableDay, string weeks, Course course);

    /// <summary>在同一计划中用新记录替换原课程；找不到原实例时不更改。</summary>
    void UpdateCourse(int enableDay, string weeks, Course original, Course updated);

    /// <summary>建立科目、保存档案并返回新科目实例。</summary>
    Subject AddSubject(string name);

    /// <summary>删除档案中的科目定义；操作不隐式删除引用其名称的课程。</summary>
    void RemoveSubject(Subject subject);

    /// <summary>验证并打开指定文件；读取失败时保留原档案和路径，由调用方报告原因。</summary>
    void ImportFrom(string path);

    /// <summary>将当前档案写到另一个位置，不切换活动档案。</summary>
    /// <exception cref="InvalidOperationException">目标是正在使用的工作档案。</exception>
    void ExportTo(string path);
}

/// <summary>以 CSES 文件为后端的单档案编辑服务；没有文件路径时也支持内存编辑。</summary>
public sealed class ScheduleService : IScheduleService
{
    private readonly Func<AppSettings> _settings;
    private ScheduleProfile _profile = new();

    /// <summary>从访问器取得当前本机设置，以保持档案切换与上层配置一致。</summary>
    public ScheduleService(Func<AppSettings> settingsAccessor) : this(settingsAccessor, null, null) { }

    /// <summary>指定本地调课目录和教学日时钟；未指定目录时，调课与基础文件相邻但独立保存。</summary>
    public ScheduleService(Func<AppSettings> settingsAccessor, string? temporaryDirectory, Func<DateOnly>? today = null)
    {
        _settings = settingsAccessor;
        TemporarySchedules = new TemporaryScheduleStore(_profile, temporaryDirectory,
            today ?? (() => DateOnly.FromDateTime(DateTime.Today)), Raise);
    }

    /// <summary>当前基础档案对应的本地按日调课存储；切换或重新加载档案时自动切换。</summary>
    public TemporaryScheduleStore TemporarySchedules { get; }

    /// <inheritdoc />
    public ScheduleProfile Profile => _profile;

    /// <inheritdoc />
    public string FilePath { get; private set; } = "";

    /// <inheritdoc />
    public string FileName => string.IsNullOrEmpty(FilePath) ? "（无）" : Path.GetFileName(FilePath);

    /// <inheritdoc />
    public event Action? ProfileChanged;

    /// <inheritdoc />
    public event Action? Saved;

    /// <inheritdoc />
    public void Load(string path)
    {
        ScheduleProfile profile;
        try
        {
            profile = File.Exists(path) ? CsesCodec.Load(path) : new ScheduleProfile();
        }
        catch
        {
            profile = new ScheduleProfile();
        }
        Activate(path, profile);
    }

    private void Activate(string path, ScheduleProfile profile)
    {
        FilePath = path;
        _profile = profile;
        TemporarySchedules.Bind(_profile, path);
        Raise();
    }

    /// <inheritdoc />
    public void Reload() => Load(FilePath);

    /// <inheritdoc />
    public void Save() => TrySave(out _);

    /// <summary>保存工作文件并返回结果，供显式保存操作在写盘失败时给出反馈。</summary>
    public bool TrySave(out string? error)
    {
        _profile.Invalidate();
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(FilePath)) error = "尚未选择基础课表文件。";
            else AtomicFile.Write(FilePath, CsesCodec.SerializeWorkingProfile(_profile));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or YamlDotNet.Core.YamlException)
        { error = e.Message; }
        if (error is null) Saved?.Invoke();
        Raise();
        return error is null;
    }

    /// <inheritdoc />
    public void AddCourse(int enableDay, string weeks, Course course)
    {
        _profile.GetOrCreateSchedule(enableDay, weeks).Classes.Add(course);
        Save();
    }

    /// <inheritdoc />
    public void RemoveCourse(int enableDay, string weeks, Course course)
    {
        Find(enableDay, weeks)?.Classes.Remove(course);
        Save();
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Subject AddSubject(string name)
    {
        var subject = new Subject { Name = name };
        _profile.Subjects.Add(subject);
        Save();
        return subject;
    }

    /// <inheritdoc />
    public void RemoveSubject(Subject subject)
    {
        _profile.Subjects.Remove(subject);
        Save();
    }

    /// <inheritdoc />
    public void ImportFrom(string path)
    {
        var profile = CsesCodec.Load(path);
        _settings().TimetablePath = path;
        Activate(path, profile);
    }

    /// <inheritdoc />
    public void ExportTo(string path)
    {
        var target = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(FilePath) && string.Equals(Path.GetFullPath(FilePath), target,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("标准 CSES 不保留室外课程标记。请选择其他文件名或文件夹，不要覆盖正在使用的课表。");
        CsesCodec.Save(_profile, target);
    }

    private Schedule? Find(int enableDay, string weeks) =>
        _profile.Schedules.FirstOrDefault(s => s.EnableDay == enableDay && s.Weeks == weeks);

    private void Raise() => ProfileChanged?.Invoke();
}
