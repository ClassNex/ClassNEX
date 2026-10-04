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

    /// <summary>内置示例课表版本。改版示例数据时递增，用于让旧安装自动更新。</summary>
    private const int SampleSeedVersion = 3;

    public static void Initialize()
    {
        SettingsService.EnsureDataDirectory();
        Settings = SettingsService.Load();

        if (Settings.Widgets.Count == 0)
            Settings.Widgets = AppSettings.CreateDefaultWidgets();

        // 迁移：旧版本默认 0.55 → 对齐 CI ComponentLayouts.BackgroundOpacity = 0.5
        if (Math.Abs(Settings.BackgroundOpacity - 0.55) < 0.001)
            Settings.BackgroundOpacity = Styles.CiPalette.CardOpacity;

        // 示例课表升级：仅当用户没有指定自定义课表文件时，重新播种并重建时间表
        if (Settings.SampleSeedVersion < SampleSeedVersion)
        {
            if (string.IsNullOrWhiteSpace(Settings.TimetablePath))
            {
                try
                {
                    var sample = CsesCodec.SamplePath;
                    if (File.Exists(sample))
                        File.Copy(sample, SettingsService.TimetableStoragePath, true);

                    Settings.TimeLayout = new TimeLayout();
                }
                catch
                {
                    // 忽略播种失败
                }
            }

            Settings.SampleSeedVersion = SampleSeedVersion;
            SettingsService.Save(Settings);
        }

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
