using System.Text.Encodings.Web;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>
/// 应用设置的持久化。
/// 目录结构（对照 CI 的「版本应用文件夹 + 共享 data 文件夹」模型）：
///   ClassNEX\
///   ├─ 26w41e\            ← 应用文件（一个版本一个文件夹，更新=新增一个版本文件夹）
///   ├─ data\              ← 共享数据（Settings.json / timetable.yaml / Logs，跨版本保留）
///   其中本程序就运行在某个版本文件夹里，数据目录取其**上一级**的 data —— 这样换新版本文件夹
///   时数据不动。
/// </summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>共享数据目录：版本文件夹（程序所在目录）的上一级 \data。</summary>
    public static string DataDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data"));

    /// <summary>日志目录（CI：AppLogFolderPath = 数据根 \Logs）。</summary>
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    public static string SettingsPath => Path.Combine(DataDirectory, "Settings.json");

    /// <summary>默认课表文件的存储位置。</summary>
    public static string TimetableStoragePath => Path.Combine(DataDirectory, "timetable.yaml");

    /// <summary>崩溃日志。</summary>
    public static string CrashLogPath => Path.Combine(LogsDirectory, "crash.log");

    public static void EnsureDataDirectory()
    {
        // 旧布局迁移：老版本把 data 放在程序文件夹里（<exe>\data），
        // 新布局搬到上一级的共享 data。已有新 data 就不再动。
        var legacyData = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(legacyData) && !Directory.Exists(DataDirectory))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DataDirectory)!);
                Directory.Move(legacyData, DataDirectory);
            }
            catch
            {
                // 迁移失败（被占用等）就保留旧目录，下次再试
            }
        }

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                if (!string.IsNullOrWhiteSpace(json))
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch
        {
            // 设置损坏时回退到默认值
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            EnsureDataDirectory();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
            // 忽略写入失败
        }
    }
}
