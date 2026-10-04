using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 应用级共享状态：设置与当前课表文档。
/// 各窗口通过订阅事件保持同步。
/// </summary>
public static class AppServices
{
    public static AppSettings Settings { get; private set; } = new();

    public static CsesDocument Document { get; private set; } = new();

    /// <summary>当前课表文件路径。</summary>
    public static string TimetablePath { get; private set; } = "";

    /// <summary>课表文档发生变化。</summary>
    public static event Action? DocumentChanged;

    /// <summary>应用设置发生变化。</summary>
    public static event Action? SettingsChanged;

    public static void Initialize()
    {
        SettingsService.EnsureDataDirectory();
        Settings = SettingsService.Load();
        ResolveTimetablePath();
        ReloadDocument();
    }

    /// <summary>确定当前使用的课表文件；首次运行时把内置示例复制到 data 目录。</summary>
    private static void ResolveTimetablePath()
    {
        var storage = SettingsService.TimetableStoragePath;
        if (!File.Exists(storage))
        {
            var sample = Path.Combine(AppContext.BaseDirectory, "Assets", "timetable.yaml");
            if (File.Exists(sample))
                File.Copy(sample, storage, true);
        }

        if (!string.IsNullOrWhiteSpace(Settings.TimetablePath) && File.Exists(Settings.TimetablePath))
            TimetablePath = Settings.TimetablePath!;
        else
            TimetablePath = storage;
    }

    public static void ReloadDocument()
    {
        try
        {
            Document = File.Exists(TimetablePath) ? CsesService.Load(TimetablePath) : new CsesDocument();
        }
        catch
        {
            Document = new CsesDocument();
        }

        DocumentChanged?.Invoke();
    }

    /// <summary>切换到另一个课表文件。</summary>
    public static void LoadTimetable(string path)
    {
        TimetablePath = path;
        Settings.TimetablePath = path;
        SaveSettings();
        ReloadDocument();
    }

    /// <summary>把当前文档写回课表文件。</summary>
    public static void SaveDocument()
    {
        try
        {
            CsesService.Save(Document, TimetablePath);
        }
        catch
        {
            // 忽略写入失败
        }

        DocumentChanged?.Invoke();
    }

    public static void SaveSettings()
    {
        SettingsService.Save(Settings);
        SettingsChanged?.Invoke();
    }

    /// <summary>课表文件名（用于界面展示）。</summary>
    public static string TimetableFileName =>
        string.IsNullOrEmpty(TimetablePath) ? "（无）" : Path.GetFileName(TimetablePath);
}
