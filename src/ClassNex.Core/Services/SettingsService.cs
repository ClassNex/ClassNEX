using ClassNex.Models;

namespace ClassNex.Services;

/// <summary>保留 Alpha 默认数据目录和容错行为的便携安装入口；需自行处理错误时使用 SettingsStore。</summary>
public static class SettingsService
{
    /// <summary>默认便携配置的共享存储；兼容入口和显式调用使用同一把文件锁。</summary>
    public static SettingsStore Store { get; } = new(SettingsPath);

    /// <summary>独立于版本载荷的 data 根目录。</summary>
    public static string DataDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data"));

    /// <summary>便携数据下存放本机运行日志的位置。</summary>
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    /// <summary>便携安装实际读取和保存的 Settings.json 路径。</summary>
    public static string SettingsPath => Path.Combine(DataDirectory, "Settings.json");

    /// <summary>未指定外部课表时本机的默认 CSES 存储位置。</summary>
    public static string TimetableStoragePath => Path.Combine(DataDirectory, "timetable.yaml");

    /// <summary>应用崩溃日志的本机路径。</summary>
    public static string CrashLogPath => Path.Combine(LogsDirectory, "crash.log");

    /// <summary>创建 data 和日志目录，并在新目录尚不存在时尝试迁移旧安装数据。</summary>
    public static void EnsureDataDirectory()
    {
        
        
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
                
            }
        }

        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <summary>读取默认设置文件；沿用旧版容错约定，读取失败时返回默认设置。</summary>
    public static AppSettings Load()
    {
        try
        {
            return Store.Load();
        }
        catch
        {
            
        }

        return new AppSettings();
    }

    internal static bool MigrateAccountEndpoint(AppSettings settings)
    {
        if (!Uri.TryCreate(settings.AccountEndpoint, UriKind.Absolute, out var endpoint))
        {
            settings.AccountEndpoint = AppSettings.DefaultAccountEndpoint;
            return true;
        }

        if (endpoint.Host is not ("124.221.140.188" or "124.221.160.188" or "206.187.201.60" or "auth.smart-teach.cn"))
            return false;

        settings.AccountEndpoint = AppSettings.DefaultAccountEndpoint;
        return true;
    }

    internal static bool MigrateCentralEndpoint(AppSettings settings)
    {
        var value = settings.CentralManagement.ServerUrl?.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
        {
            settings.CentralManagement.ServerUrl = AppSettings.DefaultCentralEndpoint;
            return true;
        }

        if (endpoint.Host is not ("124.221.140.188" or "124.221.160.188" or "206.187.201.60"))
            return false;

        settings.CentralManagement.ServerUrl = AppSettings.DefaultCentralEndpoint;
        return true;
    }

    /// <summary>按兼容模式写入默认设置文件；需要获知 I/O 错误时改用 SettingsStore。</summary>
    public static void Save(AppSettings settings)
    {
        try
        {
            EnsureDataDirectory();
            Store.Save(settings);
        }
        catch
        {
            
        }
    }
}
