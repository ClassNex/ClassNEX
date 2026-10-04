using System.Text.Encodings.Web;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>应用设置的持久化（data/Settings.json）。</summary>
public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>数据目录（与程序同级的 data 文件夹，便于便携使用）。</summary>
    public static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    public static string SettingsPath => Path.Combine(DataDirectory, "Settings.json");

    /// <summary>默认课表文件的存储位置。</summary>
    public static string TimetableStoragePath => Path.Combine(DataDirectory, "timetable.yaml");

    public static void EnsureDataDirectory() => Directory.CreateDirectory(DataDirectory);

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
