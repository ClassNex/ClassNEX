using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 应用组合根。集中持有五个服务：
/// 设置、课表、时间、时间表、组件。
/// </summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();

    public static IScheduleService Schedule { get; private set; } = null!;

    public static ITimeService Time { get; private set; } = null!;

    public static ITimeLayoutService TimeLayout { get; private set; } = null!;

    public static IWidgetService Widgets { get; private set; } = null!;

    /// <summary>应用设置发生变化。</summary>
    public static event Action? SettingsChanged;

    public static void Initialize()
    {
        SettingsService.EnsureDataDirectory();
        Settings = SettingsService.Load();

        if (Settings.Widgets.Count == 0)
            Settings.Widgets = AppSettings.CreateDefaultWidgets();

        Time = new TimeService(() => Settings);
        Schedule = new ScheduleService(() => Settings);
        TimeLayout = new TimeLayoutService(() => Settings, Schedule);
        Widgets = new WidgetService(() => Settings);

        // 课表加载后自动补全时间表
        Schedule.ProfileChanged += () => TimeLayout.EnsureFromProfile(Schedule.Profile);

        Schedule.Load(ResolveTimetablePath());
        TimeLayout.EnsureFromProfile(Schedule.Profile);
    }

    /// <summary>切换课表文件。</summary>
    public static void LoadTimetable(string path)
    {
        Settings.TimetablePath = path;
        SettingsService.Save(Settings);
        Schedule.Load(path);
    }

    /// <summary>重新加载当前课表文件。</summary>
    public static void ReloadTimetable()
    {
        Schedule.Reload();
        TimeLayout.EnsureFromProfile(Schedule.Profile);
    }

    public static void SaveSettings()
    {
        SettingsService.Save(Settings);
        SettingsChanged?.Invoke();
    }

    public static string TimetableFileName => Schedule.FileName;

    public static string TimetablePath => Schedule.FilePath;

    /// <summary>确定当前课表文件；首次运行把内置示例复制到 data 目录。</summary>
    private static string ResolveTimetablePath()
    {
        var storage = SettingsService.TimetableStoragePath;
        if (!File.Exists(storage))
        {
            var sample = CsesCodec.SamplePath;
            if (File.Exists(sample))
            {
                try
                {
                    File.Copy(sample, storage, true);
                }
                catch
                {
                    // 忽略复制失败
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(Settings.TimetablePath) &&
            File.Exists(Settings.TimetablePath))
        {
            return Settings.TimetablePath!;
        }

        return storage;
    }
}
