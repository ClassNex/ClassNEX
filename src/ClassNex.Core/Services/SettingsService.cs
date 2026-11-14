using System.Text.Encodings.Web;
using System.Text.Json;
using ClassNex.Models;

namespace ClassNex.Services;










public static class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    
    public static string DataDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "data"));

    
    public static string LogsDirectory => Path.Combine(DataDirectory, "Logs");

    public static string SettingsPath => Path.Combine(DataDirectory, "Settings.json");

    
    public static string TimetableStoragePath => Path.Combine(DataDirectory, "timetable.yaml");

    
    public static string CrashLogPath => Path.Combine(LogsDirectory, "crash.log");

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
            
        }
    }
}
